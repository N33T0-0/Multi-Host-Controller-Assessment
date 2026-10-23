namespace Client_Agent.Services;

/// <summary>
/// Result of a prerequisite verification check.
/// </summary>
public sealed record PrerequisiteCheckResult(bool Success, string? ErrorMessage = null);

/// <summary>
/// Verifies prerequisites for running WSL, Ansible, and Docker.
/// </summary>
public interface IWslPrerequisiteChecker
{
    /// <summary>
    /// Checks that wsl.exe is available and the specified distribution can execute commands.
    /// </summary>
    Task<PrerequisiteCheckResult> CheckWslAndDistributionAsync(string distribution, CancellationToken cancellationToken);

    /// <summary>
    /// Checks that Ansible and the Docker daemon are available inside the specified WSL distribution.
    /// </summary>
    Task<PrerequisiteCheckResult> CheckAnsibleAndDockerAsync(string distribution, string ansibleCommand, CancellationToken cancellationToken);
}

