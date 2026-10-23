namespace Multi_Host_Controller_Assessment.Deployments;

public interface IDeploymentService
{
    Task<IReadOnlyList<DeploymentInstructionView>> GetInstructionsAsync(bool activeOnly = false, CancellationToken cancellationToken = default);
    Task<DeploymentInstructionView?> GetInstructionAsync(long id, CancellationToken cancellationToken = default);
    Task<DeploymentInstructionView> CreateInstructionAsync(DeploymentInstructionInput input, CancellationToken cancellationToken = default);
    Task<DeploymentOperationResult> UpdateInstructionAsync(long id, DeploymentInstructionInput input, CancellationToken cancellationToken = default);
    Task<DeploymentOperationResult> SetInstructionActiveAsync(long id, bool isActive, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeploymentJobView>> GetHostJobsAsync(string clientAgentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeploymentInstructionView>> GetCompatibleInstructionsAsync(string clientAgentId, CancellationToken cancellationToken = default);
    Task<long> StartJobAsync(string clientAgentId, long instructionId, string? configuration, string? requestedBy, CancellationToken cancellationToken = default);
    Task<DeploymentClaimResult> ClaimNextJobAsync(string clientAgentId, CancellationToken cancellationToken = default);
    Task<DeploymentOperationResult> ReportProgressAsync(long jobId, DeploymentProgressRequest request, CancellationToken cancellationToken = default);
    Task<DeploymentOperationResult> CompleteJobAsync(long jobId, DeploymentCompletionRequest request, CancellationToken cancellationToken = default);
}
