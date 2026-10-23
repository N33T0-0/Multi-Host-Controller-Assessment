namespace Multi_Host_Controller_Assessment.Deployments;

public sealed record DeploymentInstructionInput(
    string PackageCode,
    string PackageName,
    string Version,
    string Platform,
    string InstallPlaybookYaml,
    string VerificationPlaybookYaml,
    string? RollbackPlaybookYaml,
    string? DefaultConfiguration);

public sealed class DeploymentInstructionView
{
    public long Id { get; init; }
    public string PackageCode { get; init; } = string.Empty;
    public string PackageName { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Platform { get; init; } = string.Empty;
    public string InstallPlaybookYaml { get; init; } = string.Empty;
    public string VerificationPlaybookYaml { get; init; } = string.Empty;
    public string? RollbackPlaybookYaml { get; init; }
    public string? DefaultConfiguration { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed class DeploymentJobView
{
    public long Id { get; init; }
    public string PackageCode { get; init; } = string.Empty;
    public string PackageName { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? CurrentStep { get; init; }
    public DateTime RequestedAt { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public bool? Success { get; init; }
    public string? ErrorMessage { get; init; }
    public bool LogsUnavailable { get; init; }
    public IReadOnlyList<DeploymentJobLogView> Logs { get; init; } = Array.Empty<DeploymentJobLogView>();
}

public sealed class DeploymentJobLogView
{
    public DateTime LoggedAt { get; init; }
    public string EventName { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed class DeploymentClaimView
{
    public long JobId { get; init; }
    public string PackageCode { get; init; } = string.Empty;
    public string PackageName { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string InstallPlaybookYaml { get; init; } = string.Empty;
    public string VerificationPlaybookYaml { get; init; } = string.Empty;
    public string? RollbackPlaybookYaml { get; init; }
    public string? Configuration { get; init; }
}

public sealed record DeploymentClaimResult(string State, DeploymentClaimView? Job = null);
public sealed record DeploymentOperationResult(bool Succeeded, string? Error = null, long? JobId = null);

public sealed class ClaimDeploymentRequest
{
    public string ClientAgentId { get; set; } = string.Empty;
}

public sealed class DeploymentProgressRequest
{
    public string ClientAgentId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? CurrentStep { get; set; }
    public string? Message { get; set; }
}

public sealed class DeploymentCompletionRequest
{
    public string ClientAgentId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}

public sealed class DeploymentValidationException(string message) : Exception(message);
public sealed class DeploymentConflictException(string message) : Exception(message);
public sealed class DeploymentLogStorageUnavailableException(string message, Exception innerException) : Exception(message, innerException);
