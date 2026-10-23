using Client_Agent.Configuration;
using Client_Agent.Models;
using Client_Agent.Services;
using Microsoft.Extensions.Options;

namespace Client_Agent;

/// <summary>
/// Polls for deployment assignments and executes them inside WSL using Ansible.
/// </summary>
public sealed class DeploymentWorker : BackgroundService
{
    private readonly ClientAgentOptions _options;
    private readonly IAgentIdProvider _agentIdProvider;
    private readonly IMasterHostClient _masterHostClient;
    private readonly IWslProcessRunner _processRunner;
    private readonly IWslPrerequisiteChecker _prerequisiteChecker;
    private readonly IDeploymentStager _deploymentStager;
    private readonly ILogger<DeploymentWorker> _logger;

    public DeploymentWorker(
        IOptions<ClientAgentOptions> options,
        IAgentIdProvider agentIdProvider,
        IMasterHostClient masterHostClient,
        IWslProcessRunner processRunner,
        IWslPrerequisiteChecker prerequisiteChecker,
        IDeploymentStager deploymentStager,
        ILogger<DeploymentWorker> logger)
    {
        _options = options.Value;
        _agentIdProvider = agentIdProvider;
        _masterHostClient = masterHostClient;
        _processRunner = processRunner;
        _prerequisiteChecker = prerequisiteChecker;
        _deploymentStager = deploymentStager;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _options.Validate();
        string agentId;
        try
        {
            agentId = _agentIdProvider.GetOrCreateAgentId();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deployment polling could not start because agent identity is unavailable.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.DeploymentPollIntervalSeconds), stoppingToken);

                // Check that WSL and configured distribution are available before attempting to claim work
                var wslCheck = await _prerequisiteChecker.CheckWslAndDistributionAsync(_options.WslDistribution, stoppingToken);
                if (!wslCheck.Success)
                {
                    _logger.LogWarning("WSL check failed for distribution '{Distribution}': {Error}. Waiting before polling.",
                        _options.WslDistribution, wslCheck.ErrorMessage);
                    continue;
                }

                var pollResult = await _masterHostClient.ClaimDeploymentAsync(agentId, stoppingToken);
                if (pollResult.Status == DeploymentPollStatus.Deactivated)
                {
                    _logger.LogWarning("Master Host deactivated agent {AgentId}; deployment worker is stopping.", agentId);
                    return;
                }

                if (pollResult.Status == DeploymentPollStatus.Claimed && pollResult.Job is not null)
                {
                    await ExecuteDeploymentAsync(agentId, pollResult.Job, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Deployment polling cycle encountered an unexpected error; polling will resume.");
            }
        }
    }

    private async Task ExecuteDeploymentAsync(string agentId, DeploymentAssignment job, CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting execution for claimed deployment job {JobId} ({PackageCode} {Version}).",
            job.JobId, job.PackageCode, job.Version);

        // 1. Check Ansible and Docker inside the WSL distribution
        var prereqCheck = await _prerequisiteChecker.CheckAnsibleAndDockerAsync(
            _options.WslDistribution, _options.AnsiblePlaybookCommand, stoppingToken);

        if (!prereqCheck.Success)
        {
            var msg = $"Prerequisite check failed: {prereqCheck.ErrorMessage}";
            _logger.LogWarning("Job {JobId} failed prerequisites: {Message}", job.JobId, msg);
            await SafeCompleteAsync(job, agentId, "Failed", msg, stoppingToken);
            return;
        }

        // 2. Stage playbooks and configuration
        StagedJobContext stagedContext;
        try
        {
            stagedContext = await _deploymentStager.StageJobAsync(
                job, _options.WslDistribution, _options.LinuxWorkDirectory, stoppingToken);
        }
        catch (Exception ex)
        {
            var msg = $"Job staging failed: {ex.Message}";
            _logger.LogError(ex, "Failed to stage job {JobId}.", job.JobId);
            await SafeCompleteAsync(job, agentId, "Failed", msg, stoppingToken);
            return;
        }

        await using (stagedContext)
        {
            // 3. Install Step
            await ReportProgressAsync(job, agentId, "Running", "Installing", "Executing install playbook.", stoppingToken);

            var installResult = await RunPlaybookAsync(
                stagedContext.LinuxJobDirectory,
                stagedContext.InstallPlaybookFileName,
                stagedContext.ExtraVarsFileName,
                stoppingToken);

            if (installResult.ExitCode != 0 || installResult.TimedOut || installResult.Cancelled)
            {
                var failureMessage = FormatExecutionError("Install", installResult, _options.PlaybookTimeoutSeconds);
                _logger.LogWarning("Job {JobId} install step failed: {Error}", job.JobId, failureMessage);

                await HandleFailureAndRollbackAsync(job, agentId, stagedContext, failureMessage, stoppingToken);
                return;
            }

            // 4. Verification Step
            await ReportProgressAsync(job, agentId, "Verifying", "Verifying", "Install playbook succeeded. Executing verification playbook.", stoppingToken);

            var verifyResult = await RunPlaybookAsync(
                stagedContext.LinuxJobDirectory,
                stagedContext.VerifyPlaybookFileName,
                stagedContext.ExtraVarsFileName,
                stoppingToken);

            if (verifyResult.ExitCode != 0 || verifyResult.TimedOut || verifyResult.Cancelled)
            {
                var failureMessage = FormatExecutionError("Verification", verifyResult, _options.PlaybookTimeoutSeconds);
                _logger.LogWarning("Job {JobId} verification step failed: {Error}", job.JobId, failureMessage);

                await HandleFailureAndRollbackAsync(job, agentId, stagedContext, failureMessage, stoppingToken);
                return;
            }

            // 5. Success
            _logger.LogInformation("Job {JobId} completed successfully.", job.JobId);
            await CompleteAsync(job, agentId, "Succeeded", null, stoppingToken);
        }
    }

    private async Task HandleFailureAndRollbackAsync(
        DeploymentAssignment job,
        string agentId,
        StagedJobContext stagedContext,
        string failureMessage,
        CancellationToken stoppingToken)
    {
        var hasRollback = !string.IsNullOrWhiteSpace(stagedContext.RollbackPlaybookFileName);

        if (!hasRollback)
        {
            _logger.LogInformation("Job {JobId} has no rollback playbook. Reporting Failed.", job.JobId);
            await SafeCompleteAsync(job, agentId, "Failed", failureMessage, stoppingToken);
            return;
        }

        if (stoppingToken.IsCancellationRequested)
        {
            _logger.LogWarning("Agent shutdown was requested for Job {JobId}. Skipping rollback execution.", job.JobId);
            await SafeCompleteAsync(job, agentId, "Failed", $"{failureMessage} (Rollback skipped due to agent shutdown)", CancellationToken.None);
            return;
        }

        // Execute Rollback
        await ReportProgressAsync(job, agentId, "RollingBack", "RollingBack", $"{failureMessage} Starting rollback playbook.", stoppingToken);

        var rollbackResult = await RunPlaybookAsync(
            stagedContext.LinuxJobDirectory,
            stagedContext.RollbackPlaybookFileName!,
            stagedContext.ExtraVarsFileName,
            stoppingToken);

        if (rollbackResult.ExitCode == 0 && !rollbackResult.TimedOut && !rollbackResult.Cancelled)
        {
            _logger.LogInformation("Job {JobId} rollback succeeded.", job.JobId);
            await SafeCompleteAsync(job, agentId, "RolledBack", $"{failureMessage} Rollback succeeded.", stoppingToken);
        }
        else
        {
            var rollbackError = FormatExecutionError("Rollback", rollbackResult, _options.PlaybookTimeoutSeconds);
            _logger.LogWarning("Job {JobId} rollback failed: {Error}", job.JobId, rollbackError);
            await SafeCompleteAsync(job, agentId, "RollbackFailed", $"{failureMessage} Rollback failed: {rollbackError}", stoppingToken);
        }
    }

    private async Task<ProcessExecutionResult> RunPlaybookAsync(
        string linuxJobDir,
        string playbookFileName,
        string extraVarsFileName,
        CancellationToken cancellationToken)
    {
        var execArgs = new[]
        {
            _options.AnsiblePlaybookCommand,
            playbookFileName,
            "-i",
            "localhost,",
            "-c",
            "local",
            "-e",
            $"@{extraVarsFileName}"
        };

        return await _processRunner.ExecuteWslAsync(
            _options.WslDistribution,
            execArgs,
            workingDirectory: linuxJobDir,
            timeout: TimeSpan.FromSeconds(_options.PlaybookTimeoutSeconds),
            gracePeriod: TimeSpan.FromSeconds(_options.ProcessGracePeriodSeconds),
            cancellationToken: cancellationToken);
    }

    private async Task ReportProgressAsync(
        DeploymentAssignment job,
        string agentId,
        string status,
        string step,
        string message,
        CancellationToken cancellationToken)
    {
        var report = new DeploymentProgressReport
        {
            ClientAgentId = agentId,
            Status = status,
            CurrentStep = step,
            Message = message
        };

        var ok = await _masterHostClient.ReportDeploymentProgressAsync(job.JobId, report, cancellationToken);
        if (!ok)
        {
            throw new InvalidOperationException($"Master Host did not accept progress '{status}' for job {job.JobId}.");
        }
    }

    private async Task CompleteAsync(
        DeploymentAssignment job,
        string agentId,
        string status,
        string? message,
        CancellationToken cancellationToken)
    {
        var report = new DeploymentCompletionReport
        {
            ClientAgentId = agentId,
            Status = status,
            ErrorMessage = status == "Succeeded" ? null : message
        };

        var ok = await _masterHostClient.CompleteDeploymentAsync(job.JobId, report, cancellationToken);
        if (!ok)
        {
            throw new InvalidOperationException($"Master Host did not accept completion '{status}' for job {job.JobId}.");
        }
    }

    private async Task SafeCompleteAsync(
        DeploymentAssignment job,
        string agentId,
        string status,
        string? message,
        CancellationToken cancellationToken)
    {
        try
        {
            // Use short timeout with uncancelled token if agent is shutting down
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var token = cancellationToken.IsCancellationRequested ? timeoutCts.Token : cancellationToken;
            await CompleteAsync(job, agentId, status, message, token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to submit completion report '{Status}' for job {JobId}.", status, job.JobId);
        }
    }

    private static string FormatExecutionError(string stepName, ProcessExecutionResult result, int timeoutSeconds)
    {
        if (result.TimedOut)
        {
            return $"{stepName} playbook timed out after {timeoutSeconds}s.";
        }
        if (result.Cancelled)
        {
            return $"{stepName} playbook execution was cancelled.";
        }

        var details = ExtractConciseOutput(result.StandardError, result.StandardOutput);
        return $"{stepName} playbook failed with exit code {result.ExitCode}: {details}";
    }

    private static string ExtractConciseOutput(string stderr, string stdout)
    {
        var text = !string.IsNullOrWhiteSpace(stderr) ? stderr : stdout;
        if (string.IsNullOrWhiteSpace(text)) return "(no process output)";

        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(l => l.Trim())
                        .Where(l => !string.IsNullOrEmpty(l))
                        .ToArray();

        var tail = string.Join(" | ", lines.TakeLast(3));
        if (tail.Length > 500)
        {
            tail = tail.Substring(tail.Length - 500);
        }
        return tail;
    }
}
