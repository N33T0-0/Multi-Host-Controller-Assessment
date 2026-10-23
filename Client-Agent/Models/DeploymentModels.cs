namespace Client_Agent.Models;

public sealed class DeploymentClaimRequest
{
    public string ClientAgentId { get; set; } = string.Empty;
}

public sealed class DeploymentAssignment
{
    public long JobId { get; set; }
    public string PackageCode { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string InstallPlaybookYaml { get; set; } = string.Empty;
    public string VerificationPlaybookYaml { get; set; } = string.Empty;
    public string? RollbackPlaybookYaml { get; set; }
    public string? Configuration { get; set; }
}

public sealed class DeploymentProgressReport
{
    public string ClientAgentId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? CurrentStep { get; set; }
    public string? Message { get; set; }
}

public sealed class DeploymentCompletionReport
{
    public string ClientAgentId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}

public enum DeploymentPollStatus { Empty, Claimed, Deactivated, AuthenticationFailed, Failed }
public sealed record DeploymentPollResult(DeploymentPollStatus Status, DeploymentAssignment? Job = null);
