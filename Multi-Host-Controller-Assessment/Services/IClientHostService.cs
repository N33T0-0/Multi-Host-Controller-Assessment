using System.Text.Json;
using Multi_Host_Controller_Assessment.Data.Entities;
using Multi_Host_Controller_Assessment.Models.Requests;

namespace Multi_Host_Controller_Assessment.Services;

/// <summary>
/// Result of a client agent registration operation.
/// </summary>
public class RegisterHostResult
{
    public bool IsNewRegistration { get; set; }

    public ClientHost Host { get; set; } = null!;
}

/// <summary>
/// Result of a recorded heartbeat operation.
/// </summary>
public class HeartbeatResult
{
    public string ClientAgentId { get; set; } = string.Empty;

    public DateTime LoggedDateTime { get; set; }

    public bool Status { get; set; }
}

/// <summary>
/// Service contract managing client agent registration, status, and heartbeats.
/// </summary>
public interface IClientHostService
{
    /// <summary>
    /// Registers a new client agent or refreshes metadata for an existing agent idempotently.
    /// </summary>
    Task<RegisterHostResult> RegisterHostAsync(RegisterClientAgentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a heartbeat log entry and ensures the host is marked active.
    /// Returns null if the client agent is not registered.
    /// </summary>
    Task<HeartbeatResult?> RecordHeartbeatAsync(string clientAgentId, JsonElement? resourceUsageMetrics, CancellationToken cancellationToken = default);
}

