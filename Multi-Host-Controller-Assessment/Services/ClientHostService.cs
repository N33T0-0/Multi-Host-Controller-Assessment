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
        if (string.IsNullOrWhiteSpace(request.ClientSecret))
        {
            return new RegisterHostResult
            {
                Status = RegisterStatus.CredentialRejected,
                ErrorMessage = "ClientSecret is required."
            };
        }

        byte[] secretHash;
        try
        {
            secretHash = ClientAgentCredentialHelper.ComputeSecretHash(request.ClientSecret);
        }
        catch (Exception ex)
        {
            return new RegisterHostResult
            {
                Status = RegisterStatus.CredentialRejected,
                ErrorMessage = $"Failed to process client secret: {ex.Message}"
            };
        }

        var semaphore = RegistrationLocks.GetOrAdd(trimmedId, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(cancellationToken);
        try
        {
            await using var transaction = _dbContext.Database.ProviderName == "Microsoft.EntityFrameworkCore.SqlServer"
                ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                : null;
            var existingHost = await _dbContext.ClientHosts
                .FirstOrDefaultAsync(h => h.ClientAgentId == trimmedId, cancellationToken);

            if (existingHost != null && existingHost.IsDeleted)
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

            var matchingCredentials = await _dbContext.ClientAgentCredentials
                .Where(c => c.SecretHash == secretHash)
                .ToListAsync(cancellationToken);

            if (matchingCredentials.Count == 0)
            {
                _logger.LogWarning("Registration rejected for '{ClientAgentId}': No matching provisioned credential found.", trimmedId);
                return new RegisterHostResult
                {
                    Status = RegisterStatus.CredentialRejected,
                    ErrorMessage = "No matching provisioned credential found."
                };
            }

            if (matchingCredentials.Count > 1)
            {
                _logger.LogError("Registration rejected for '{ClientAgentId}': Data integrity error: multiple credentials share the same hash.", trimmedId);
                return new RegisterHostResult
                {
                    Status = RegisterStatus.CredentialRejected,
                    ErrorMessage = "Data integrity error: duplicate credential hashes found."
                };
            }

            var credential = matchingCredentials[0];

            if (credential.RevokedAt != null)
            {
                _logger.LogWarning("Registration rejected for '{ClientAgentId}': Credential has been revoked.", trimmedId);
                return new RegisterHostResult
                {
                    Status = RegisterStatus.CredentialRejected,
                    ErrorMessage = "Credential has been revoked."
                };
            }

            if (credential.ExpiresAt != null && credential.ExpiresAt <= DateTime.UtcNow)
            {
                _logger.LogWarning("Registration rejected for '{ClientAgentId}': Credential has expired.", trimmedId);
                return new RegisterHostResult
                {
                    Status = RegisterStatus.CredentialRejected,
                    ErrorMessage = "Credential has expired."
                };
            }

            // Credential is unbound (ClientHostId is null)
            if (credential.ClientHostId == null)
            {
                if (existingHost != null)
                {
                    var hostAlreadyHasOtherCred = await _dbContext.ClientAgentCredentials
                        .AnyAsync(c => c.ClientHostId == existingHost.Id && c.Id != credential.Id, cancellationToken);

                    if (hostAlreadyHasOtherCred)
                    {
                        _logger.LogWarning("Registration rejected for '{ClientAgentId}': Host already has a bound credential.", trimmedId);
                        return new RegisterHostResult
                        {
                            Status = RegisterStatus.CredentialRejected,
                            ErrorMessage = "Host already has a bound credential; registration cannot rotate credentials."
                        };
                    }

                    // Bind to existing host
                    credential.ClientHostId = existingHost.Id;
                    existingHost.ClientAgentName = request.ClientAgentName.Trim();
                    existingHost.ClientAgentOS = request.ClientAgentOS.Trim();
                    existingHost.ClientAgentVersion = request.ClientAgentVersion.Trim();
                    existingHost.Status = true;
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    if (transaction is not null) await transaction.CommitAsync(cancellationToken);

                    _logger.LogInformation("Bound unbound credential to existing host '{ClientAgentId}'.", trimmedId);
                    return new RegisterHostResult
                    {
                        Status = RegisterStatus.ActiveExisting,
                        Host = existingHost
                    };
                }

                // New host creation and binding
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

                if (_dbContext.Database.ProviderName != "Microsoft.EntityFrameworkCore.SqlServer")
                {
                    newHost.ClientRegistrationDateTime = DateTime.Now;
                }

                _dbContext.ClientHosts.Add(newHost);
                await _dbContext.SaveChangesAsync(cancellationToken);

                credential.ClientHostId = newHost.Id;
                await _dbContext.SaveChangesAsync(cancellationToken);
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);

                _logger.LogInformation("Registered new client agent '{ClientAgentId}' and bound credential.", trimmedId);

                return new RegisterHostResult
                {
                    Status = RegisterStatus.Created,
                    Host = newHost
                };
            }

            // Credential is already bound
            if (existingHost != null && credential.ClientHostId == existingHost.Id)
            {
                // Idempotent retry with same host and same secret
                existingHost.ClientAgentName = request.ClientAgentName.Trim();
                existingHost.ClientAgentOS = request.ClientAgentOS.Trim();
                existingHost.ClientAgentVersion = request.ClientAgentVersion.Trim();
                existingHost.Status = true;
                await _dbContext.SaveChangesAsync(cancellationToken);
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);

                _logger.LogInformation("Idempotent registration retry for agent '{ClientAgentId}' accepted.", trimmedId);
                return new RegisterHostResult
                {
                    Status = RegisterStatus.ActiveExisting,
                    Host = existingHost
                };
            }

            _logger.LogWarning("Registration rejected for '{ClientAgentId}': Credential is bound to another host.", trimmedId);
            return new RegisterHostResult
            {
                Status = RegisterStatus.CredentialRejected,
                ErrorMessage = "Credential is already bound to another host."
            };
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict during registration for '{ClientAgentId}'.", trimmedId);
            throw;
        }
        finally
        {
            semaphore.Release();
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
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var hostId = await _dbContext.ClientHosts
                .Where(host => host.ClientAgentId == trimmedId)
                .Select(host => (long?)host.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (hostId is null)
            {
                await transaction.CommitAsync(cancellationToken);
                _logger.LogWarning("Deactivation rejected: Client agent '{ClientAgentId}' was not found.", trimmedId);
                return false;
            }

            var updatedRows = await _dbContext.ClientHosts
                .Where(host => host.Id == hostId.Value && !host.IsDeleted)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(host => host.IsDeleted, true)
                        .SetProperty(host => host.DeletedAt, host => DateTime.Now),
                    cancellationToken);

            var now = DateTime.UtcNow;
            await _dbContext.ClientAgentCredentials
                .Where(c => c.ClientHostId == hostId.Value && c.RevokedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.RevokedAt, now), cancellationToken);

            if (updatedRows > 0)
            {
                await transaction.CommitAsync(cancellationToken);
                _logger.LogInformation(
                    "Client agent '{ClientAgentId}' was soft-deactivated.",
                    trimmedId);
                return true;
            }

            var alreadyExists = await _dbContext.ClientHosts
                .AnyAsync(host => host.ClientAgentId == trimmedId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
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

            // Revoke all credentials bound to this host
            var credentials = await _dbContext.ClientAgentCredentials
                .Where(c => c.ClientHostId == host.Id && c.RevokedAt == null)
                .ToListAsync(cancellationToken);
            foreach (var cred in credentials)
            {
                cred.RevokedAt = DateTime.UtcNow;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Client agent '{ClientAgentId}' successfully soft-deactivated at {DeletedAt} and {Count} credentials revoked.",
                trimmedId,
                host.DeletedAt,
                credentials.Count);
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
