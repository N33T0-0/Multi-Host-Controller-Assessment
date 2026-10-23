namespace Client_Agent.Services;

/// <summary>
/// Result of an executed WSL process.
/// </summary>
public sealed record ProcessExecutionResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    bool Cancelled);

/// <summary>
/// Abstraction for invoking processes inside WSL using explicit argument lists.
/// </summary>
public interface IWslProcessRunner
{
    /// <summary>
    /// Executes a command in the specified WSL distribution with explicit argument lists.
    /// </summary>
    Task<ProcessExecutionResult> ExecuteWslAsync(
        string distribution,
        IReadOnlyList<string> execArgs,
        string? workingDirectory = null,
        TimeSpan? timeout = null,
        TimeSpan? gracePeriod = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts a Windows file or directory path into its absolute WSL mount path using wslpath.
    /// </summary>
    Task<string> ConvertWindowsPathToWslAsync(
        string distribution,
        string windowsPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the home directory for the default user in the specified WSL distribution.
    /// </summary>
    Task<string> ResolveLinuxHomeDirectoryAsync(
        string distribution,
        CancellationToken cancellationToken = default);
}

