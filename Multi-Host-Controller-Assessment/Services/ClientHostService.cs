using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Multi_Host_Controller_Assessment.Data;
using Multi_Host_Controller_Assessment.Data.Entities;
using Multi_Host_Controller_Assessment.Models.Requests;

namespace Multi_Host_Controller_Assessment.Services;

/// <summary>
/// Implementation of client agent registration and heartbeat handling.
/// </summary>
public class ClientHostService : IClientHostService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> RegistrationLocks = new();
    private readonly MasterHostDbContext _dbContext;
    private readonly ILogger<ClientHostService> _logger;

    public ClientHostService(MasterHostDbContext dbContext, ILogger<ClientHostService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<RegisterHostResult> RegisterHostAsync(
        RegisterClientAgentRequest request,
        CancellationToken cancellationToken = default)
    {
        var trimmedId = request.ClientAgentId.Trim();
        var semaphore = RegistrationLocks.GetOrAdd(trimmedId, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(cancellationToken);
        try
        {
            var existingHost = await _dbContext.ClientHosts
                .FirstOrDefaultAsync(h => h.ClientAgentId == trimmedId, cancellationToken);

            if (existingHost != null)
            {
                // Refresh existing host metadata and re-enable status, preserving original registration timestamp and identity
                existingHost.ClientAgentName = request.ClientAgentName.Trim();
                existingHost.ClientAgentOS = request.ClientAgentOS.Trim();
                existingHost.ClientAgentVersion = request.ClientAgentVersion.Trim();
                existingHost.Status = true;

                await _dbContext.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Refreshed registration for existing client agent '{ClientAgentId}'.",
                    trimmedId);

                return new RegisterHostResult
                {
                    IsNewRegistration = false,
                    Host = existingHost
                };
            }

            var newHost = new ClientHost
            {
                ClientAgentId = trimmedId,
                ClientAgentName = request.ClientAgentName.Trim(),
                ClientAgentOS = request.ClientAgentOS.Trim(),
                ClientAgentVersion = request.ClientAgentVersion.Trim(),
                Status = true
            };

            // In non-SQL Server providers (e.g., InMemory test provider), supply timestamp if SQL default expression cannot execute
            if (_dbContext.Database.ProviderName != "Microsoft.EntityFrameworkCore.SqlServer")
            {
                newHost.ClientRegistrationDateTime = DateTime.UtcNow;
            }

            try
            {
                _dbContext.ClientHosts.Add(newHost);
                await _dbContext.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Registered new client agent '{ClientAgentId}'.",
                    trimmedId);

                return new RegisterHostResult
                {
                    IsNewRegistration = true,
                    Host = newHost
                };
            }
            catch (DbUpdateException ex)
            {
                // Concurrency fallback if another process inserted the record concurrently or a DB unique constraint triggered
                _logger.LogWarning(
                    ex,
                    "Concurrent registration conflict detected for '{ClientAgentId}'. Falling back to refresh.",
                    trimmedId);

                // Reload the existing record
                var refreshed = await _dbContext.ClientHosts
                    .AsNoTracking()
                    .FirstOrDefaultAsync(h => h.ClientAgentId == trimmedId, cancellationToken);

                if (refreshed != null)
                {
                    refreshed.ClientAgentName = request.ClientAgentName.Trim();
                    refreshed.ClientAgentOS = request.ClientAgentOS.Trim();
                    refreshed.ClientAgentVersion = request.ClientAgentVersion.Trim();
                    refreshed.Status = true;
                    _dbContext.ClientHosts.Update(refreshed);
                    await _dbContext.SaveChangesAsync(cancellationToken);

                    return new RegisterHostResult
                    {
                        IsNewRegistration = false,
                        Host = refreshed
                    };
                }

                throw;
            }
        }
        finally
        {
            semaphore.Release();
            // Prune locks if no one is waiting to prevent unbounded growth
            if (semaphore.CurrentCount == 1)
            {
                RegistrationLocks.TryRemove(trimmedId, out _);
            }
        }
    }

    /// <inheritdoc />
    public async Task<HeartbeatResult?> RecordHeartbeatAsync(
        string clientAgentId,
        JsonElement? resourceUsageMetrics,
        CancellationToken cancellationToken = default)
    {
        var trimmedId = clientAgentId.Trim();

        var host = await _dbContext.ClientHosts
            .FirstOrDefaultAsync(h => h.ClientAgentId == trimmedId, cancellationToken);

        if (host == null)
        {
            _logger.LogWarning(
                "Heartbeat rejected: Client agent '{ClientAgentId}' is not registered.",
                trimmedId);
            return null;
        }

        // Set host status to active
        host.Status = true;

        string? metricsText = ParseMetrics(resourceUsageMetrics);

        var log = new ClientHostHeartbeatLog
        {
            ClientAgentId = trimmedId,
            ResourceUsageMetrics = metricsText
        };

        if (_dbContext.Database.ProviderName != "Microsoft.EntityFrameworkCore.SqlServer")
        {
            log.LoggedDateTime = DateTime.UtcNow;
        }

        _dbContext.ClientHostHeartbeatLogs.Add(log);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Logged heartbeat for client agent '{ClientAgentId}'.",
            trimmedId);

        return new HeartbeatResult
        {
            ClientAgentId = trimmedId,
            LoggedDateTime = log.LoggedDateTime == default ? DateTime.UtcNow : log.LoggedDateTime,
            Status = host.Status
        };
    }

    private static string? ParseMetrics(JsonElement? resourceUsageMetrics)
    {
        if (!resourceUsageMetrics.HasValue)
        {
            return null;
        }

        var element = resourceUsageMetrics.Value;
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;

            case JsonValueKind.String:
                var stringValue = element.GetString();
                if (string.IsNullOrWhiteSpace(stringValue))
                {
                    return null;
                }

                // Validate that the supplied string is valid JSON text
                try
                {
                    using var parsed = JsonDocument.Parse(stringValue);
                    return stringValue;
                }
                catch (JsonException ex)
                {
                    throw new ArgumentException("ResourceUsageMetrics contains invalid JSON text.", nameof(resourceUsageMetrics), ex);
                }

            case JsonValueKind.Object:
            case JsonValueKind.Array:
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                return element.GetRawText();

            default:
                return null;
        }
    }
}

