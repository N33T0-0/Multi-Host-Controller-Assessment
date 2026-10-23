namespace Client_Agent.Services;

/// <summary>
/// Implements WSL, Ansible, and Docker prerequisite checks using the IWslProcessRunner.
/// </summary>
public class WslPrerequisiteChecker : IWslPrerequisiteChecker
{
    private readonly IWslProcessRunner _runner;
    private readonly ILogger<WslPrerequisiteChecker> _logger;

    public WslPrerequisiteChecker(IWslProcessRunner runner, ILogger<WslPrerequisiteChecker> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PrerequisiteCheckResult> CheckWslAndDistributionAsync(string distribution, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _runner.ExecuteWslAsync(
                distribution,
                ["/bin/true"],
                timeout: TimeSpan.FromSeconds(15),
                cancellationToken: cancellationToken);

            if (result.ExitCode != 0)
            {
                var details = !string.IsNullOrWhiteSpace(result.StandardError)
                    ? result.StandardError
                    : result.StandardOutput;

                _logger.LogWarning("WSL distribution '{Distribution}' check failed (ExitCode: {ExitCode}): {Details}",
                    distribution, result.ExitCode, details);

                return new PrerequisiteCheckResult(false,
                    $"WSL distribution '{distribution}' is not available or failed to respond. (Exit code: {result.ExitCode}{(string.IsNullOrWhiteSpace(details) ? "" : $", Details: {details}")})");
            }

            return new PrerequisiteCheckResult(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to invoke WSL executable 'wsl.exe'.");
            return new PrerequisiteCheckResult(false, $"WSL executable 'wsl.exe' is not accessible: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<PrerequisiteCheckResult> CheckAnsibleAndDockerAsync(string distribution, string ansibleCommand, CancellationToken cancellationToken)
    {
        // 1. Check Ansible
        try
        {
            var ansibleResult = await _runner.ExecuteWslAsync(
                distribution,
                [ansibleCommand, "--version"],
                timeout: TimeSpan.FromSeconds(20),
                cancellationToken: cancellationToken);

            if (ansibleResult.ExitCode != 0)
            {
                var details = !string.IsNullOrWhiteSpace(ansibleResult.StandardError)
                    ? ansibleResult.StandardError
                    : ansibleResult.StandardOutput;

                _logger.LogWarning("Ansible executable '{Command}' check failed in distribution '{Distribution}': {Details}",
                    ansibleCommand, distribution, details);

                return new PrerequisiteCheckResult(false,
                    $"Ansible executable '{ansibleCommand}' is not available in WSL distribution '{distribution}'. Please install Ansible in the distribution.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to verify Ansible in WSL distribution '{Distribution}'.", distribution);
            return new PrerequisiteCheckResult(false, $"Failed to verify Ansible in WSL: {ex.Message}");
        }

        // 2. Check Docker daemon reachability
        try
        {
            var dockerResult = await _runner.ExecuteWslAsync(
                distribution,
                ["docker", "info"],
                timeout: TimeSpan.FromSeconds(25),
                cancellationToken: cancellationToken);

            if (dockerResult.ExitCode != 0)
            {
                var details = !string.IsNullOrWhiteSpace(dockerResult.StandardError)
                    ? dockerResult.StandardError
                    : dockerResult.StandardOutput;

                _logger.LogWarning("Docker daemon check failed in distribution '{Distribution}': {Details}",
                    distribution, details);

                return new PrerequisiteCheckResult(false,
                    $"Docker daemon is not reachable inside WSL distribution '{distribution}'. Verify Docker Engine or Docker Desktop WSL integration is active.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to verify Docker daemon in WSL distribution '{Distribution}'.", distribution);
            return new PrerequisiteCheckResult(false, $"Failed to verify Docker daemon in WSL: {ex.Message}");
        }

        return new PrerequisiteCheckResult(true);
    }
}

