using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Multi_Host_Controller_Assessment.Configuration;
using Multi_Host_Controller_Assessment.Data;

namespace Multi_Host_Controller_Assessment.Dashboard;

/// <summary>
/// EF Core–backed implementation of the dashboard query service.
///
/// Timestamp basis:
///   The heartbeat LoggedDateTime column is a SQL Server DATETIME populated by
///   GETDATE(), which represents SQL Server local time without timezone metadata.
///   To compare it consistently, this service computes the threshold boundary
///   using DateTime.Now (local server time) rather than DateTime.UtcNow, so both
///   the stored value and the comparison basis are in the same time zone.
///   If the web server and SQL Server run in different time zones this comparison
///   will drift; that case is documented here as a known limitation.
/// </summary>
public class DashboardQueryService : IDashboardQueryService
{
    private readonly MasterHostDbContext _db;
    private readonly DashboardOptions _options;

    public DashboardQueryService(MasterHostDbContext db, IOptions<DashboardOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<HostSummaryViewModel>> GetHostSummariesAsync(
        CancellationToken cancellationToken = default)
    {
        var thresholdBoundary = DateTime.Now.AddSeconds(-_options.OnlineThresholdSeconds);

        // Single query: group heartbeat logs by ClientAgentID to get MAX(LoggedDateTime)
        // then left-join to ClientHosts so hosts with no heartbeats appear as offline.
        var latestHeartbeats = await _db.ClientHostHeartbeatLogs
            .AsNoTracking()
            .GroupBy(h => h.ClientAgentId)
            .Select(g => new
            {
                ClientAgentId = g.Key,
                LatestAt = g.Max(h => h.LoggedDateTime)
            })
            .ToDictionaryAsync(x => x.ClientAgentId, x => (DateTime?)x.LatestAt, cancellationToken);

        var hosts = await _db.ClientHosts
            .AsNoTracking()
            .OrderBy(h => h.ClientAgentName)
            .Select(h => new
            {
                h.ClientAgentId,
                h.ClientAgentName,
                h.ClientAgentOS,
                h.ClientAgentVersion,
                h.IsDeleted,
                h.DeletedAt
            })
            .ToListAsync(cancellationToken);

        var result = hosts.Select(h =>
        {
            var latestAt = latestHeartbeats.TryGetValue(h.ClientAgentId, out var t) ? t : null;
            var isOnline = latestAt.HasValue && latestAt.Value >= thresholdBoundary;
            return new HostSummaryViewModel
            {
                ClientAgentId = h.ClientAgentId,
                ClientAgentName = h.ClientAgentName,
                ClientAgentOS = h.ClientAgentOS,
                ClientAgentVersion = h.ClientAgentVersion,
                LatestHeartbeatAt = latestAt,
                IsOnline = isOnline,
                IsDeactivated = h.IsDeleted,
                DeletedAt = h.DeletedAt
            };
        }).ToList();

        return result.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<HostDetailViewModel?> GetHostDetailAsync(
        string clientAgentId,
        CancellationToken cancellationToken = default)
    {
        var thresholdBoundary = DateTime.Now.AddSeconds(-_options.OnlineThresholdSeconds);

        var host = await _db.ClientHosts
            .AsNoTracking()
            .Where(h => h.ClientAgentId == clientAgentId)
            .Select(h => new
            {
                h.ClientAgentId,
                h.ClientAgentName,
                h.ClientAgentOS,
                h.ClientAgentVersion,
                h.ClientRegistrationDateTime,
                h.IsDeleted,
                h.DeletedAt
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (host is null)
        {
            return null;
        }

        // Retrieve latest heartbeat log for this agent
        var latestLog = await _db.ClientHostHeartbeatLogs
            .AsNoTracking()
            .Where(l => l.ClientAgentId == clientAgentId)
            .OrderByDescending(l => l.LoggedDateTime)
            .Select(l => new { l.LoggedDateTime, l.ResourceUsageMetrics })
            .FirstOrDefaultAsync(cancellationToken);

        DateTime? latestAt = latestLog?.LoggedDateTime;
        var isOnline = latestAt.HasValue && latestAt.Value >= thresholdBoundary;

        var (processMetrics, hostEnvMetrics, parseWarning) =
            ParseMetrics(latestLog?.ResourceUsageMetrics);

        return new HostDetailViewModel
        {
            ClientAgentId = host.ClientAgentId,
            ClientAgentName = host.ClientAgentName,
            ClientAgentOS = host.ClientAgentOS,
            ClientAgentVersion = host.ClientAgentVersion,
            RegistrationDateTime = host.ClientRegistrationDateTime,
            LatestHeartbeatAt = latestAt,
            IsOnline = isOnline,
            IsDeactivated = host.IsDeleted,
            DeletedAt = host.DeletedAt,
            ProcessMetrics = processMetrics,
            HostEnvironmentMetrics = hostEnvMetrics,
            MetricsParseWarning = parseWarning
        };
    }

    /// <summary>
    /// Defensively parses the Client Agent metrics JSON. Returns nulls for
    /// absent/malformed data rather than throwing.
    ///
    /// Expected payload shape (from Client Agent ResourceMetricsProvider):
    /// {
    ///   "scope": "process_and_host_environment",
    ///   "process": {
    ///     "cpuUsagePercentage": 12.5,
    ///     "cpuUsagePercentageUnit": "percent_normalized_to_total_processors",
    ///     "workingSetBytes": 123456789,
    ///     "workingSetUnit": "bytes",
    ///     "privateMemoryBytes": 98765432,
    ///     "privateMemoryUnit": "bytes"
    ///   },
    ///   "host": {
    ///     "processorCount": 8,
    ///     "totalAvailableMemoryBytes": 17179869184,
    ///     "totalAvailableMemoryUnit": "bytes",
    ///     "operatingSystem": "Microsoft Windows 10.0.22000"
    ///   }
    /// }
    /// </summary>
    public static (ProcessMetricsViewModel? process, HostEnvironmentMetricsViewModel? host, bool parseWarning)
        ParseMetrics(string? metricsJson)
    {
        if (string.IsNullOrWhiteSpace(metricsJson))
        {
            return (null, null, false);
        }

        try
        {
            using var doc = JsonDocument.Parse(metricsJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, null, false);
            }

            ProcessMetricsViewModel? process = null;
            HostEnvironmentMetricsViewModel? hostEnv = null;

            if (root.TryGetProperty("process", out var procEl))
            {
                process = new ProcessMetricsViewModel
                {
                    CpuUsagePercentage = TryGetDouble(procEl, "cpuUsagePercentage"),
                    WorkingSetBytes = TryGetLong(procEl, "workingSetBytes"),
                    PrivateMemoryBytes = TryGetLong(procEl, "privateMemoryBytes"),
                    CpuUsageUnit = TryGetString(procEl, "cpuUsagePercentageUnit")
                        ?? "percent_normalized_to_total_processors"
                };
            }

            if (root.TryGetProperty("host", out var hostEl))
            {
                hostEnv = new HostEnvironmentMetricsViewModel
                {
                    ProcessorCount = TryGetInt(hostEl, "processorCount"),
                    TotalAvailableMemoryBytes = TryGetLong(hostEl, "totalAvailableMemoryBytes"),
                    OperatingSystem = TryGetString(hostEl, "operatingSystem")
                };
            }

            return (process, hostEnv, false);
        }
        catch (JsonException)
        {
            // Malformed JSON — surface a warning instead of crashing the page
            return (null, null, true);
        }
    }

    private static double? TryGetDouble(JsonElement el, string propertyName)
    {
        if (el.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number)
        {
            return prop.GetDouble();
        }
        return null;
    }

    private static long? TryGetLong(JsonElement el, string propertyName)
    {
        if (el.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number &&
            prop.TryGetInt64(out var val))
        {
            return val;
        }
        return null;
    }

    private static int? TryGetInt(JsonElement el, string propertyName)
    {
        if (el.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number &&
            prop.TryGetInt32(out var val))
        {
            return val;
        }
        return null;
    }

    private static string? TryGetString(JsonElement el, string propertyName)
    {
        if (el.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString();
        }
        return null;
    }
}
