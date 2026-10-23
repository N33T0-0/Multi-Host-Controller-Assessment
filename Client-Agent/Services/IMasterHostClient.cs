using Client_Agent.Models;

namespace Client_Agent.Services;

public enum HeartbeatResultStatus
{
    Success,
    NotFound,
    Deactivated,
    Failed
}

public enum RegistrationResultStatus
{
    Success,
    Deactivated,
    Failed
}

/// <summary>
/// HTTP client interface for communicating with the Master Host API.
/// </summary>
public interface IMasterHostClient
{
    /// <summary>
    /// Registers the agent with Master Host via POST /api/master-host/register.
    /// Returns the registration outcome, including permanent deactivation.
    /// </summary>
    Task<RegistrationResultStatus> RegisterAsync(RegisterAgentRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Submits a heartbeat and optional metrics to Master Host via POST /api/master-host/heartbeat/{id}.
    /// </summary>
    Task<HeartbeatResultStatus> SendHeartbeatAsync(string clientAgentId, object? metrics, CancellationToken cancellationToken);

    Task<DeploymentPollResult> ClaimDeploymentAsync(string clientAgentId, CancellationToken cancellationToken);

    Task<bool> ReportDeploymentProgressAsync(long jobId, DeploymentProgressReport report, CancellationToken cancellationToken);

    Task<bool> CompleteDeploymentAsync(long jobId, DeploymentCompletionReport report, CancellationToken cancellationToken);
}

