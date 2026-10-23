using Client_Agent.Models;

namespace Client_Agent.Services;

/// <summary>
/// Encapsulates the paths and cleanup context for a staged deployment job.
/// </summary>
public sealed class StagedJobContext : IAsyncDisposable
{
    public string WindowsStagingPath { get; }
    public string LinuxJobDirectory { get; }
    public string InstallPlaybookFileName { get; }
    public string VerifyPlaybookFileName { get; }
    public string? RollbackPlaybookFileName { get; }
    public string ExtraVarsFileName { get; }
    public Func<Task>? CleanupAction { get; set; }

    public StagedJobContext(
        string windowsStagingPath,
        string linuxJobDirectory,
        string installPlaybookFileName,
        string verifyPlaybookFileName,
        string? rollbackPlaybookFileName,
        string extraVarsFileName)
    {
        WindowsStagingPath = windowsStagingPath;
        LinuxJobDirectory = linuxJobDirectory;
        InstallPlaybookFileName = installPlaybookFileName;
        VerifyPlaybookFileName = verifyPlaybookFileName;
        RollbackPlaybookFileName = rollbackPlaybookFileName;
        ExtraVarsFileName = extraVarsFileName;
    }

    public async ValueTask DisposeAsync()
    {
        if (CleanupAction is not null)
        {
            await CleanupAction();
        }
    }
}

/// <summary>
/// Validates and stages deployment playbooks and variables into Windows temp and WSL Linux filesystems.
/// </summary>
public interface IDeploymentStager
{
    /// <summary>
    /// Validates playbooks and configuration, creates isolated staging directories, and copies files into the WSL work directory.
    /// </summary>
    Task<StagedJobContext> StageJobAsync(
        DeploymentAssignment job,
        string distribution,
        string linuxWorkDirectoryRoot,
        CancellationToken cancellationToken);
}

