namespace Multi_Host_Controller_Assessment.Data.Entities;

public class DeploymentJob
{
    public long Id { get; set; }
    public long DeploymentInstructionId { get; set; }
    public long ClientHostId { get; set; }
    public string? RequestedBy { get; set; }
    public string? Configuration { get; set; }
    public string Status { get; set; } = DeploymentJobStatuses.Pending;
    public string? CurrentStep { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public bool? Success { get; set; }
    public string? ErrorMessage { get; set; }
}

public static class DeploymentJobStatuses
{
    public const string Pending = "Pending";
    public const string Assigned = "Assigned";
    public const string Running = "Running";
    public const string Verifying = "Verifying";
    public const string RollingBack = "RollingBack";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string RolledBack = "RolledBack";
    public const string RollbackFailed = "RollbackFailed";
    public const string Cancelled = "Cancelled";

    public static readonly string[] Ongoing = [Pending, Assigned, Running, Verifying, RollingBack];
    public static readonly string[] Terminal = [Succeeded, Failed, RolledBack, RollbackFailed, Cancelled];
}
