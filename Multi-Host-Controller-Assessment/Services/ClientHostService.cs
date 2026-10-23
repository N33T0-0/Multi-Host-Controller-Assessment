using System.Collections.Concurrent;
using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Multi_Host_Controller_Assessment.Data;
using Multi_Host_Controller_Assessment.Data.Entities;
using Multi_Host_Controller_Assessment.Models.Requests;

namespace Multi_Host_Controller_Assessment.Services;

/// <summary>
/// Implementation of client agent registration, heartbeat handling, and deactivation.
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
                if (existingHost.IsDeleted)
                {
                    _logger.LogWarning(
                        "Registration rejected for deactivated client agent '{ClientAgentId}'.",
                        trimmedId);

                    return new RegisterHostResult
                    {
                        Status = RegisterStatus.Deactivated,
                        Host = existingHost
                    };
                }

                // Existing active host: do NOT modify any fields (preserve name, OS, version, status, timestamps)
                _logger.LogInformation(
                    "Registration received for existing active client agent '{ClientAgentId}'. Preserving unchanged record.",
                    trimmedId);

                return new RegisterHostResult
                {
                    Status = RegisterStatus.ActiveExisting,
                    Host = existingHost
                };
            }

            var newHost = new ClientHost
            {
                ClientAgentId = trimmedId,
                ClientAgentName = request.ClientAgentName.Trim(),
                ClientAgentOS = request.ClientAgentOS.Trim(),
                ClientAgentVersion = request.ClientAgentVersion.Trim(),
                Status = true,
                IsDeleted = false,
                DeletedAt = null
            };

            // In non-SQL Server providers (e.g., InMemory test provider), supply timestamp if SQL default expression cannot execute
            if (_dbContext.Database.ProviderName != "Microsoft.EntityFrameworkCore.SqlServer")
            {
                newHost.ClientRegistrationDateTime = DateTime.Now;
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
                    Status = RegisterStatus.Created,
                    Host = newHost
                };
            }
            catch (DbUpdateException ex)
            {
                // Concurrency fallback if another process inserted the record concurrently or a DB unique constraint triggered
                _logger.LogWarning(
                    ex,
                    "Concurrent registration conflict detected for '{ClientAgentId}'. Loading existing record.",
                    trimmedId);

                // Reload the existing record
                var reloaded = await _dbContext.ClientHosts
                    .AsNoTracking()
                    .FirstOrDefaultAsync(h => h.ClientAgentId == trimmedId, cancellationToken);

                if (reloaded != null)
                {
                    return new RegisterHostResult
                    {
                        Status = reloaded.IsDeleted ? RegisterStatus.Deactivated : RegisterStatus.ActiveExisting,
                        Host = reloaded
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
    public async Task<HeartbeatResult> RecordHeartbeatAsync(
        string clientAgentId,
        JsonElement? resourceUsageMetrics,
        CancellationToken cancellationToken = default)
    {
        var trimmedId = clientAgentId.Trim();

        // Hold the host row/range lock through the heartbeat insert. This serializes
        // heartbeat writes with a concurrent soft-deactivation on SQL Server.
        await using var transaction = _dbContext.Database.ProviderName == "Microsoft.EntityFrameworkCore.SqlServer"
            ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        var host = await _dbContext.ClientHosts
            .FirstOrDefaultAsync(h => h.ClientAgentId == trimmedId, cancellationToken);

        if (host == null)
        {
            _logger.LogWarning(
                "Heartbeat rejected: Client agent '{ClientAgentId}' is not registered.",
                trimmedId);
            return new HeartbeatResult
            {
                Status = HeartbeatStatus.NotFound,
                ClientAgentId = trimmedId
            };
        }

        if (host.IsDeleted)
        {
            _logger.LogWarning(
                "Heartbeat rejected: Client agent '{ClientAgentId}' has been deactivated. Performing no writes.",
                trimmedId);
            return new HeartbeatResult
            {
                Status = HeartbeatStatus.Deactivated,
                ClientAgentId = trimmedId
            };
        }

        // Set active host status to true
        host.Status = true;

        string? metricsText = ParseMetrics(resourceUsageMetrics);

        var log = new ClientHostHeartbeatLog
        {
            ClientAgentId = trimmedId,
            ResourceUsageMetrics = metricsText
        };

        if (_dbContext.Database.ProviderName != "Microsoft.EntityFrameworkCore.SqlServer")
        {
            log.LoggedDateTime = DateTime.Now;
        }

        _dbContext.ClientHostHeartbeatLogs.Add(log);
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        _logger.LogInformation(
            "Logged heartbeat for client agent '{ClientAgentId}'.",
            trimmedId);

        return new HeartbeatResult
        {
            Status = HeartbeatStatus.Success,
            ClientAgentId = trimmedId,
            LoggedDateTime = log.LoggedDateTime == default ? DateTime.Now : log.LoggedDateTime,
            HostStatus = host.Status
        };
    }

    /// <inheritdoc />
    public async Task<bool> DeactivateHostAsync(
        string clientAgentId,
        CancellationToken cancellationToken = default)
    {
        var trimmedId = clientAgentId.Trim();

        if (_dbContext.Database.ProviderName == "Microsoft.EntityFrameworkCore.SqlServer")
        {
            // Conditional update makes concurrent/repeated deactivation idempotent,
            // preserves the first DeletedAt value, and uses SQL Server local time.
            var updatedRows = await _dbContext.ClientHosts
                .Where(host => host.ClientAgentId == trimmedId && !host.IsDeleted)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(host => host.IsDeleted, true)
                        .SetProperty(host => host.DeletedAt, host => DateTime.Now),
                    cancellationToken);

            if (updatedRows > 0)
            {
                _logger.LogInformation(
                    "Client agent '{ClientAgentId}' was soft-deactivated.",
                    trimmedId);
                return true;
            }

            var alreadyExists = await _dbContext.ClientHosts
                .AnyAsync(host => host.ClientAgentId == trimmedId, cancellationToken);
            if (alreadyExists)
            {
                _logger.LogInformation(
                    "Client agent '{ClientAgentId}' is already deactivated; preserving its original deletion timestamp.",
                    trimmedId);
            }
            else
            {
                _logger.LogWarning(
                    "Deactivation rejected: Client agent '{ClientAgentId}' was not found.",
                    trimmedId);
            }

            return alreadyExists;
        }

        var host = await _dbContext.ClientHosts
            .FirstOrDefaultAsync(h => h.ClientAgentId == trimmedId, cancellationToken);

        if (host == null)
        {
            _logger.LogWarning(
                "Deactivation rejected: Client agent '{ClientAgentId}' was not found.",
                trimmedId);
            return false;
        }

        if (!host.IsDeleted)
        {
            host.IsDeleted = true;
            // Use DateTime.Now consistent with SQL Server GETDATE() local time representation
            host.DeletedAt = DateTime.Now;

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Client agent '{ClientAgentId}' successfully soft-deactivated at {DeletedAt}.",
                trimmedId,
                host.DeletedAt);
        }
        else
        {
            _logger.LogInformation(
                "Client agent '{ClientAgentId}' is already deactivated. Preserving original timestamp {DeletedAt}.",
                trimmedId,
                host.DeletedAt);
        }

        return true;
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
