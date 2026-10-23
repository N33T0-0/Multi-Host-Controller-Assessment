namespace Multi_Host_Controller_Assessment.Data.Entities;

public class DeploymentInstruction
{
    public long Id { get; set; }
    public string PackageCode { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string InstallPlaybookYaml { get; set; } = string.Empty;
    public string VerificationPlaybookYaml { get; set; } = string.Empty;
    public string? RollbackPlaybookYaml { get; set; }
    public string? DefaultConfiguration { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}
