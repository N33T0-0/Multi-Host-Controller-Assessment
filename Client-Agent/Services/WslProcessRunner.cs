using System.Diagnostics;
using System.Text;

namespace Client_Agent.Services;

/// <summary>
/// Executes WSL processes using System.Diagnostics.Process with ArgumentList (shell execution disabled).
/// </summary>
public class WslProcessRunner : IWslProcessRunner
{
    private const int MaxOutputLength = 65536; // 64 KB cap
    private readonly ILogger<WslProcessRunner> _logger;

    public WslProcessRunner(ILogger<WslProcessRunner> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ProcessExecutionResult> ExecuteWslAsync(
        string distribution,
        IReadOnlyList<string> execArgs,
        string? workingDirectory = null,
        TimeSpan? timeout = null,
        TimeSpan? gracePeriod = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(distribution))
            throw new ArgumentException("Distribution name cannot be null or whitespace.", nameof(distribution));
        if (execArgs == null || execArgs.Count == 0)
            throw new ArgumentException("Executable arguments must contain at least one argument.", nameof(execArgs));

        var psi = new ProcessStartInfo
        {
            FileName = "wsl.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("--distribution");
        psi.ArgumentList.Add(distribution);

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            psi.ArgumentList.Add("--cd");
            psi.ArgumentList.Add(workingDirectory);
        }

        psi.ArgumentList.Add("--exec");
        foreach (var arg in execArgs)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        var stdoutSb = new StringBuilder();
        var stderrSb = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (stdoutSb)
            {
                if (stdoutSb.Length < MaxOutputLength)
                {
                    var remaining = MaxOutputLength - stdoutSb.Length;
                    if (e.Data.Length > remaining)
                        stdoutSb.AppendLine(e.Data.Substring(0, remaining));
                    else
                        stdoutSb.AppendLine(e.Data);
                }
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (stderrSb)
            {
                if (stderrSb.Length < MaxOutputLength)
                {
                    var remaining = MaxOutputLength - stderrSb.Length;
                    if (e.Data.Length > remaining)
                        stderrSb.AppendLine(e.Data.Substring(0, remaining));
                    else
                        stderrSb.AppendLine(e.Data);
                }
            }
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start wsl.exe process.");
            throw;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = timeout.HasValue && timeout.Value > TimeSpan.Zero
            ? new CancellationTokenSource(timeout.Value)
            : null;
        using var linkedCts = timeoutCts is not null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token)
            : null;

        var effectiveToken = linkedCts?.Token ?? cancellationToken;
        bool timedOut = false;
        bool cancelled = false;

        try
        {
            await process.WaitForExitAsync(effectiveToken);
            // Ensure remaining stdout/stderr events are processed
            process.WaitForExit();
        }
        catch (OperationCanceledException)
        {
            if (timeoutCts?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested)
            {
                timedOut = true;
                _logger.LogWarning("WSL process execution timed out after {TimeoutSeconds}s.", timeout?.TotalSeconds);
            }
            else
            {
                cancelled = true;
                _logger.LogInformation("WSL process execution was cancelled.");
            }

            try
            {
                var grace = gracePeriod ?? TimeSpan.FromSeconds(5);
                if (!process.HasExited && grace > TimeSpan.Zero)
                {
                    try
                    {
                        using var graceCts = new CancellationTokenSource(grace);
                        await process.WaitForExitAsync(graceCts.Token);
                    }
                    catch (OperationCanceledException) { }
                }

                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while terminating WSL process.");
            }
        }

        return new ProcessExecutionResult(
            process.HasExited ? process.ExitCode : -1,
            stdoutSb.ToString().Trim(),
            stderrSb.ToString().Trim(),
            timedOut,
            cancelled);
    }

    /// <inheritdoc />
    public async Task<string> ConvertWindowsPathToWslAsync(
        string distribution,
        string windowsPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(windowsPath))
            throw new ArgumentException("Windows path cannot be null or whitespace.", nameof(windowsPath));

        var result = await ExecuteWslAsync(
            distribution,
            ["wslpath", "-a", "-u", windowsPath],
            timeout: TimeSpan.FromSeconds(15),
            cancellationToken: cancellationToken);

        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            throw new InvalidOperationException(
                $"wslpath failed to convert path '{windowsPath}'. ExitCode: {result.ExitCode}, Stderr: {result.StandardError}");
        }

        var converted = result.StandardOutput.Trim();
        if (!converted.StartsWith("/"))
        {
            throw new InvalidOperationException(
                $"wslpath returned an invalid Linux path: '{converted}' for Windows path '{windowsPath}'.");
        }
        return converted;
    }

    /// <inheritdoc />
    public async Task<string> ResolveLinuxHomeDirectoryAsync(
        string distribution,
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteWslAsync(
            distribution,
            ["printenv", "HOME"],
            timeout: TimeSpan.FromSeconds(15),
            cancellationToken: cancellationToken);

        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            throw new InvalidOperationException(
                $"Failed to resolve HOME directory in WSL distribution '{distribution}'. ExitCode: {result.ExitCode}, Stderr: {result.StandardError}");
        }

        var home = result.StandardOutput.Trim();
        if (!home.StartsWith("/"))
        {
            throw new InvalidOperationException(
                $"Invalid HOME directory '{home}' returned from WSL distribution '{distribution}'.");
        }
        return home;
    }
}

