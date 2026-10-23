using System.Data;
using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Multi_Host_Controller_Assessment.Data;
using Multi_Host_Controller_Assessment.Data.Entities;

namespace Multi_Host_Controller_Assessment.Deployments;

public sealed class DeploymentService : IDeploymentService
{
    private readonly MasterHostDbContext _db;
    private readonly ILogger<DeploymentService> _logger;
    private static readonly string[] ActiveStatuses = DeploymentJobStatuses.Ongoing;

    public DeploymentService(MasterHostDbContext db, ILogger<DeploymentService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DeploymentInstructionView>> GetInstructionsAsync(bool activeOnly = false, CancellationToken cancellationToken = default)
    {
        var query = _db.DeploymentInstructions.AsNoTracking();
        if (activeOnly) query = query.Where(i => i.IsActive);
        var instructions = await query.OrderBy(i => i.PackageCode).ThenByDescending(i => i.Version).ToListAsync(cancellationToken);
        return instructions.Select(ToView).ToArray();
    }

    public async Task<DeploymentInstructionView?> GetInstructionAsync(long id, CancellationToken cancellationToken = default)
    {
        var item = await _db.DeploymentInstructions.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        return item is null ? null : ToView(item);
    }

    public async Task<DeploymentInstructionView> CreateInstructionAsync(DeploymentInstructionInput input, CancellationToken cancellationToken = default)
    {
        ValidateInstruction(input);
        if (await _db.DeploymentInstructions.AnyAsync(i => i.PackageCode == input.PackageCode.Trim() && i.Version == input.Version.Trim(), cancellationToken))
            throw new DeploymentConflictException("An instruction with this package code and version already exists.");
        var item = FromInput(input);
        item.IsActive = true;
        item.CreatedAt = DateTime.UtcNow;
        _db.DeploymentInstructions.Add(item);
        await _db.SaveChangesAsync(cancellationToken);
        return ToView(item);
    }

    public async Task<DeploymentOperationResult> UpdateInstructionAsync(long id, DeploymentInstructionInput input, CancellationToken cancellationToken = default)
    {
        ValidateInstruction(input);
        var item = await _db.DeploymentInstructions.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null) return new(false, "Deployment instruction was not found.");
        if (await _db.DeploymentInstructions.AnyAsync(i => i.Id != id && i.PackageCode == input.PackageCode.Trim() && i.Version == input.Version.Trim(), cancellationToken))
            throw new DeploymentConflictException("An instruction with this package code and version already exists.");
        CopyInput(input, item);
        await _db.SaveChangesAsync(cancellationToken);
        return new(true, JobId: id);
    }

    public async Task<DeploymentOperationResult> SetInstructionActiveAsync(long id, bool isActive, CancellationToken cancellationToken = default)
    {
        var item = await _db.DeploymentInstructions.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null) return new(false, "Deployment instruction was not found.");
        item.IsActive = isActive;
        await _db.SaveChangesAsync(cancellationToken);
        return new(true, JobId: id);
    }

    public async Task<IReadOnlyList<DeploymentInstructionView>> GetCompatibleInstructionsAsync(string clientAgentId, CancellationToken cancellationToken = default)
    {
        var host = await _db.ClientHosts.AsNoTracking().FirstOrDefaultAsync(h => h.ClientAgentId == clientAgentId, cancellationToken);
        if (host is null || host.IsDeleted) return Array.Empty<DeploymentInstructionView>();
        var instructions = await GetInstructionsAsync(activeOnly: true, cancellationToken);
        return instructions.Where(i => PlatformMatches(i.Platform, host.ClientAgentOS)).ToArray();
    }

    public async Task<IReadOnlyList<DeploymentJobView>> GetHostJobsAsync(string clientAgentId, CancellationToken cancellationToken = default)
    {
        var hostId = await _db.ClientHosts.AsNoTracking().Where(h => h.ClientAgentId == clientAgentId)
            .Select(h => (long?)h.Id).FirstOrDefaultAsync(cancellationToken);
        if (hostId is null) return Array.Empty<DeploymentJobView>();

        var jobs = await (from job in _db.DeploymentJobs.AsNoTracking()
                          join instruction in _db.DeploymentInstructions.AsNoTracking() on job.DeploymentInstructionId equals instruction.Id
                          where job.ClientHostId == hostId.Value
                          orderby job.RequestedAt descending
                          select new { job, instruction.PackageCode, instruction.PackageName, instruction.Version })
            .Take(50).ToListAsync(cancellationToken);

        var jobIds = jobs.Select(x => x.job.Id).ToArray();
        var logsUnavailable = false;
        List<DeploymentJobLog> logs;
        if (jobIds.Length == 0)
        {
            logs = [];
        }
        else
        {
            try
            {
                logs = await _db.DeploymentJobLogs.AsNoTracking()
                    .Where(l => jobIds.Contains(l.DeploymentJobId)).OrderBy(l => l.LoggedAt).ToListAsync(cancellationToken);
            }
            catch (Exception ex) when (ExceptionMentionsLogTable(ex))
            {
                logsUnavailable = true;
                logs = [];
            }
        }

        return jobs.Select(x => new DeploymentJobView
        {
            Id = x.job.Id,
            PackageCode = x.PackageCode,
            PackageName = x.PackageName,
            Version = x.Version,
            Status = x.job.Status,
            CurrentStep = x.job.CurrentStep,
            RequestedAt = x.job.RequestedAt,
            StartedAt = x.job.StartedAt,
            CompletedAt = x.job.CompletedAt,
            Success = x.job.Success,
            ErrorMessage = x.job.ErrorMessage,
            LogsUnavailable = logsUnavailable,
            Logs = logs.Where(l => l.DeploymentJobId == x.job.Id).Select(ToView).ToArray()
        }).ToArray();
    }

    public async Task<long> StartJobAsync(string clientAgentId, long instructionId, string? configuration, string? requestedBy, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientAgentId) || clientAgentId.Trim().Length > 64)
            throw new DeploymentValidationException("ClientAgentId is required and cannot exceed 64 characters.");
        if (instructionId <= 0) throw new DeploymentValidationException("A valid deployment instruction must be selected.");
        clientAgentId = clientAgentId.Trim();
        if (string.IsNullOrWhiteSpace(configuration)) configuration = null;
        if (configuration is not null) ValidateJson(configuration, "Configuration");
        if (requestedBy?.Length > 100) throw new DeploymentValidationException("RequestedBy cannot exceed 100 characters.");

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var host = await _db.ClientHosts.FirstOrDefaultAsync(h => h.ClientAgentId == clientAgentId, cancellationToken)
            ?? throw new DeploymentValidationException("The selected client host was not found.");
        await AcquireHostLockAsync(host.Id, cancellationToken);
        if (host.IsDeleted) throw new DeploymentValidationException("A deactivated host cannot receive deployments.");

        var instruction = await _db.DeploymentInstructions.FirstOrDefaultAsync(i => i.Id == instructionId, cancellationToken)
            ?? throw new DeploymentValidationException("The selected deployment instruction was not found.");
        if (!instruction.IsActive) throw new DeploymentValidationException("The selected deployment instruction is inactive.");
        if (!PlatformMatches(instruction.Platform, host.ClientAgentOS))
            throw new DeploymentValidationException("The selected package platform is incompatible with this host.");

        var activeJob = await _db.DeploymentJobs.AnyAsync(j => j.ClientHostId == host.Id && ActiveStatuses.Contains(j.Status), cancellationToken);
        if (activeJob) throw new DeploymentConflictException("This host already has a deployment in progress.");

        var job = new DeploymentJob
        {
            DeploymentInstructionId = instruction.Id,
            ClientHostId = host.Id,
            RequestedBy = requestedBy,
            Configuration = string.IsNullOrWhiteSpace(configuration) ? instruction.DefaultConfiguration : configuration,
            Status = DeploymentJobStatuses.Pending,
            CurrentStep = "Waiting for agent pickup",
            RequestedAt = DateTime.Now
        };
        _db.DeploymentJobs.Add(job);
        await _db.SaveChangesAsync(cancellationToken);
        AddLog(job.Id, "Requested", "Deployment requested from dashboard.");
        await SaveChangesWithLogsAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        _logger.LogInformation("Deployment job {JobId} requested for host {ClientAgentId}.", job.Id, clientAgentId);
        return job.Id;
    }

    public async Task<DeploymentClaimResult> ClaimNextJobAsync(string clientAgentId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var host = await _db.ClientHosts.FirstOrDefaultAsync(h => h.ClientAgentId == clientAgentId, cancellationToken);
        if (host is null) return new("NotFound");
        if (host.IsDeleted)
        {
            var unstartedJobs = await _db.DeploymentJobs.Where(j => j.ClientHostId == host.Id &&
                (j.Status == DeploymentJobStatuses.Pending || j.Status == DeploymentJobStatuses.Assigned)).ToListAsync(cancellationToken);
            foreach (var pending in unstartedJobs)
            {
                pending.Status = DeploymentJobStatuses.Cancelled;
                pending.CurrentStep = "Cancelled because the host was deactivated";
                pending.CompletedAt = DateTime.Now;
                pending.Success = false;
                pending.ErrorMessage = "Host was deactivated before mock execution started.";
                AddLog(pending.Id, "Cancelled", pending.ErrorMessage, "Warning");
            }
            if (unstartedJobs.Count > 0)
            {
                await SaveChangesWithLogsAsync(cancellationToken);
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            }
            return new("Deactivated");
        }
        await AcquireHostLockAsync(host.Id, cancellationToken);
        var job = await _db.DeploymentJobs.Where(j => j.ClientHostId == host.Id && j.Status == DeploymentJobStatuses.Pending)
            .OrderBy(j => j.RequestedAt).FirstOrDefaultAsync(cancellationToken);
        if (job is null) return new("Empty");

        var instruction = await _db.DeploymentInstructions.FirstAsync(i => i.Id == job.DeploymentInstructionId, cancellationToken);
        if (!instruction.IsActive)
        {
            job.Status = DeploymentJobStatuses.Failed;
            job.Success = false;
            job.CompletedAt = DateTime.Now;
            job.ErrorMessage = "Deployment instruction was deactivated before agent pickup.";
            job.CurrentStep = "Failed before pickup";
            AddLog(job.Id, "Failed", job.ErrorMessage, "Error");
            await SaveChangesWithLogsAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new("Empty");
        }

        job.Status = DeploymentJobStatuses.Assigned;
        job.CurrentStep = "Assigned to client agent";
        AddLog(job.Id, "Assigned", "Job claimed by the target client agent.");
        await SaveChangesWithLogsAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);

        return new("Claimed", new DeploymentClaimView
        {
            JobId = job.Id,
            PackageCode = instruction.PackageCode,
            PackageName = instruction.PackageName,
            Version = instruction.Version,
            InstallPlaybookYaml = instruction.InstallPlaybookYaml,
            VerificationPlaybookYaml = instruction.VerificationPlaybookYaml,
            RollbackPlaybookYaml = instruction.RollbackPlaybookYaml,
            Configuration = job.Configuration
        });
    }

    public async Task<DeploymentOperationResult> ReportProgressAsync(long jobId, DeploymentProgressRequest request, CancellationToken cancellationToken = default)
    {
        if (jobId <= 0 || string.IsNullOrWhiteSpace(request.ClientAgentId) || request.ClientAgentId.Trim().Length > 64)
            return new(false, "A valid job ID and ClientAgentId are required.");
        if (request.CurrentStep?.Length > 50) return new(false, "CurrentStep cannot exceed 50 characters.");
        if (request.Message?.Length > 4000) return new(false, "Progress message cannot exceed 4000 characters.");
        if (request.Status is not (DeploymentJobStatuses.Running or DeploymentJobStatuses.Verifying or DeploymentJobStatuses.RollingBack))
            return new(false, "Unsupported progress status.");

        var found = await FindOwnedJobAsync(jobId, request.ClientAgentId, cancellationToken);
        if (found is null) return new(false, "Deployment job was not found for this client agent.");
        var (job, host) = found.Value;
        if (DeploymentJobStatuses.Terminal.Contains(job.Status)) return new(false, "The deployment job is already complete.");
        if (job.Status == request.Status && job.CurrentStep == request.CurrentStep) return new(true, JobId: job.Id);
        if (!CanTransition(job.Status, request.Status)) return new(false, $"Invalid deployment transition from {job.Status} to {request.Status}.");

        job.Status = request.Status;
        job.CurrentStep = request.CurrentStep;
        job.StartedAt ??= request.Status == DeploymentJobStatuses.Running ? DateTime.Now : null;
        AddLog(job.Id, request.Status, request.Message ?? request.CurrentStep ?? request.Status);
        await SaveChangesWithLogsAsync(cancellationToken);
        _logger.LogInformation("Job {JobId} progressed to {Status} for host {HostId}.", job.Id, job.Status, host.Id);
        return new(true, JobId: job.Id);
    }

    public async Task<DeploymentOperationResult> CompleteJobAsync(long jobId, DeploymentCompletionRequest request, CancellationToken cancellationToken = default)
    {
        if (jobId <= 0 || string.IsNullOrWhiteSpace(request.ClientAgentId) || request.ClientAgentId.Trim().Length > 64)
            return new(false, "A valid job ID and ClientAgentId are required.");
        if (request.ErrorMessage?.Length > 4000) return new(false, "ErrorMessage cannot exceed 4000 characters.");
        if (request.Status is not (DeploymentJobStatuses.Succeeded or DeploymentJobStatuses.Failed or DeploymentJobStatuses.RolledBack or DeploymentJobStatuses.RollbackFailed))
            return new(false, "Unsupported terminal status.");

        var found = await FindOwnedJobAsync(jobId, request.ClientAgentId, cancellationToken);
        if (found is null) return new(false, "Deployment job was not found for this client agent.");
        var (job, host) = found.Value;
        if (job.Status == request.Status) return new(true, JobId: job.Id);
        if (DeploymentJobStatuses.Terminal.Contains(job.Status)) return new(false, "The deployment job is already complete.");
        if (!CanTransition(job.Status, request.Status)) return new(false, $"Invalid deployment transition from {job.Status} to {request.Status}.");

        job.Status = request.Status;
        job.CurrentStep = request.Status;
        job.CompletedAt = DateTime.Now;
        job.Success = request.Status == DeploymentJobStatuses.Succeeded;
        job.ErrorMessage = request.ErrorMessage;
        AddLog(job.Id, request.Status, request.ErrorMessage ?? $"Mock deployment finished with status {request.Status}.", job.Success.Value ? "Information" : "Error");
        await SaveChangesWithLogsAsync(cancellationToken);
        _logger.LogInformation("Job {JobId} completed as {Status} for host {HostId} (deactivated={IsDeleted}).", job.Id, job.Status, host.Id, host.IsDeleted);
        return new(true, JobId: job.Id);
    }

    private async Task<(DeploymentJob Job, ClientHost Host)?> FindOwnedJobAsync(long jobId, string agentId, CancellationToken cancellationToken)
    {
        var host = await _db.ClientHosts.FirstOrDefaultAsync(h => h.ClientAgentId == agentId, cancellationToken);
        if (host is null) return null;
        var job = await _db.DeploymentJobs.FirstOrDefaultAsync(j => j.Id == jobId && j.ClientHostId == host.Id, cancellationToken);
        return job is null ? null : (job, host);
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        return _db.Database.ProviderName == "Microsoft.EntityFrameworkCore.SqlServer"
            ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
    }

    private async Task AcquireHostLockAsync(long hostId, CancellationToken cancellationToken)
    {
        if (_db.Database.ProviderName != "Microsoft.EntityFrameworkCore.SqlServer") return;
        await using var command = _db.Database.GetDbConnection().CreateCommand();
        command.Transaction = _db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000; SELECT @result;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@resource";
        parameter.Value = $"DeploymentHost:{hostId}";
        command.Parameters.Add(parameter);
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        if (result < 0) throw new DeploymentConflictException("Could not obtain the host deployment lock. Please retry.");
    }

    private void AddLog(long jobId, string eventName, string message, string severity = "Information")
    {
        _db.DeploymentJobLogs.Add(new DeploymentJobLog
        {
            DeploymentJobId = jobId,
            LoggedAt = DateTime.Now,
            EventName = eventName.Length > 50 ? eventName[..50] : eventName,
            Severity = severity,
            Message = message
        });
    }

    private async Task SaveChangesWithLogsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ExceptionMentionsLogTable(ex))
        {
            throw new DeploymentLogStorageUnavailableException(
                "Deployment logging is not available. Apply database/DeploymentJobLogs.sql, then retry.", ex);
        }
    }

    private static bool ExceptionMentionsLogTable(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("DeploymentJobLogs", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool CanTransition(string current, string next) => current switch
    {
        DeploymentJobStatuses.Assigned => next is DeploymentJobStatuses.Running or DeploymentJobStatuses.Failed,
        DeploymentJobStatuses.Running => next is DeploymentJobStatuses.Verifying or DeploymentJobStatuses.RollingBack or DeploymentJobStatuses.Succeeded or DeploymentJobStatuses.Failed,
        DeploymentJobStatuses.Verifying => next is DeploymentJobStatuses.RollingBack or DeploymentJobStatuses.Succeeded or DeploymentJobStatuses.Failed,
        DeploymentJobStatuses.RollingBack => next is DeploymentJobStatuses.RolledBack or DeploymentJobStatuses.RollbackFailed,
        _ => false
    };

    private static bool PlatformMatches(string platform, string os)
    {
        if (platform.Equals("Any", StringComparison.OrdinalIgnoreCase)) return true;
        if (platform.Equals("Mac", StringComparison.OrdinalIgnoreCase) || platform.Equals("macOS", StringComparison.OrdinalIgnoreCase))
            return os.Contains("mac", StringComparison.OrdinalIgnoreCase) || os.Contains("darwin", StringComparison.OrdinalIgnoreCase);
        return os.Contains(platform, StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateInstruction(DeploymentInstructionInput input)
    {
        CheckLength(input.PackageCode, 100, nameof(input.PackageCode));
        CheckLength(input.PackageName, 200, nameof(input.PackageName));
        CheckLength(input.Version, 30, nameof(input.Version));
        CheckLength(input.Platform, 30, nameof(input.Platform));
        if (string.IsNullOrWhiteSpace(input.InstallPlaybookYaml)) throw new DeploymentValidationException("Install playbook is required.");
        if (string.IsNullOrWhiteSpace(input.VerificationPlaybookYaml)) throw new DeploymentValidationException("Verification playbook is required.");
        if (!string.IsNullOrWhiteSpace(input.DefaultConfiguration)) ValidateJson(input.DefaultConfiguration, "DefaultConfiguration");
    }

    private static void CheckLength(string value, int max, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > max)
            throw new DeploymentValidationException($"{name} is required and must not exceed {max} characters.");
    }

    private static void ValidateJson(string value, string name)
    {
        try { using var _ = JsonDocument.Parse(value); }
        catch (JsonException) { throw new DeploymentValidationException($"{name} must contain valid JSON."); }
    }

    private static DeploymentInstruction FromInput(DeploymentInstructionInput input) => new() { PackageCode = input.PackageCode.Trim(), PackageName = input.PackageName.Trim(), Version = input.Version.Trim(), Platform = input.Platform.Trim(), InstallPlaybookYaml = input.InstallPlaybookYaml, VerificationPlaybookYaml = input.VerificationPlaybookYaml, RollbackPlaybookYaml = input.RollbackPlaybookYaml, DefaultConfiguration = string.IsNullOrWhiteSpace(input.DefaultConfiguration) ? null : input.DefaultConfiguration };
    private static void CopyInput(DeploymentInstructionInput input, DeploymentInstruction item) { item.PackageCode = input.PackageCode.Trim(); item.PackageName = input.PackageName.Trim(); item.Version = input.Version.Trim(); item.Platform = input.Platform.Trim(); item.InstallPlaybookYaml = input.InstallPlaybookYaml; item.VerificationPlaybookYaml = input.VerificationPlaybookYaml; item.RollbackPlaybookYaml = input.RollbackPlaybookYaml; item.DefaultConfiguration = string.IsNullOrWhiteSpace(input.DefaultConfiguration) ? null : input.DefaultConfiguration; }
    private static DeploymentInstructionView ToView(DeploymentInstruction i) => new() { Id = i.Id, PackageCode = i.PackageCode, PackageName = i.PackageName, Version = i.Version, Platform = i.Platform, InstallPlaybookYaml = i.InstallPlaybookYaml, VerificationPlaybookYaml = i.VerificationPlaybookYaml, RollbackPlaybookYaml = i.RollbackPlaybookYaml, DefaultConfiguration = i.DefaultConfiguration, IsActive = i.IsActive, CreatedAt = i.CreatedAt };
    private static DeploymentJobLogView ToView(DeploymentJobLog l) => new() { LoggedAt = l.LoggedAt, EventName = l.EventName, Severity = l.Severity, Message = l.Message };
}
