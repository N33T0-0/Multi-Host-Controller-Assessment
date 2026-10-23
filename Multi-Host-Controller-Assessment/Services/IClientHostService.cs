using System.Text.Json;
using Multi_Host_Controller_Assessment.Data.Entities;
using Multi_Host_Controller_Assessment.Models.Requests;

namespace Multi_Host_Controller_Assessment.Services;

/// <summary>
/// Status outcome of a registration attempt.
/// </summary>
public enum RegisterStatus
{
    Created,
    ActiveExisting,
    Deactivated,
    CredentialRejected
}

/// <summary>
/// Result of a client agent registration operation.
/// </summary>
public class RegisterHostResult
{
    public RegisterStatus Status { get; set; }

    public bool IsNewRegistration => Status == RegisterStatus.Created;

    public ClientHost? Host { get; set; }

    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Status outcome of a heartbeat submission attempt.
/// </summary>
public enum HeartbeatStatus
{
    Success,
    NotFound,
    Deactivated
}

/// <summary>
/// Result of a recorded heartbeat operation.
/// </summary>
public class HeartbeatResult
{
    public HeartbeatStatus Status { get; set; }

    public string ClientAgentId { get; set; } = string.Empty;

    public DateTime LoggedDateTime { get; set; }

    public bool HostStatus { get; set; }
}

/// <summary>
/// Service contract managing client agent registration, status, heartbeats, and deactivation.
/// </summary>
public interface IClientHostService
{
    /// <summary>
    /// Registers a new client agent or returns the unchanged existing record.
    /// Returns Deactivated if the host exists but is soft-deleted.
    /// </summary>
    Task<RegisterHostResult> RegisterHostAsync(RegisterClientAgentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a heartbeat log entry and marks an active host status to 1.
    /// Returns NotFound if unregistered, or Deactivated if soft-deleted (without writing logs).
    /// </summary>
    Task<HeartbeatResult> RecordHeartbeatAsync(string clientAgentId, JsonElement? resourceUsageMetrics, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deactivates an existing client host.
    /// Sets isDeleted = 1 and DeletedAt once. Idempotent: repeating preserves original DeletedAt.
    /// Returns true if host was found; false if not found.
    /// </summary>
    Task<bool> DeactivateHostAsync(string clientAgentId, CancellationToken cancellationToken = default);
}
