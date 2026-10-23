namespace Client_Agent.Services;

public interface IAgentEnrollmentState
{
    Task<bool> WaitUntilCompleteAsync(CancellationToken cancellationToken);
    void MarkReady();
    void MarkRejected();
}

public sealed class AgentEnrollmentState : IAgentEnrollmentState
{
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<bool> WaitUntilCompleteAsync(CancellationToken cancellationToken) => _completion.Task.WaitAsync(cancellationToken);
    public void MarkReady() => _completion.TrySetResult(true);
    public void MarkRejected() => _completion.TrySetResult(false);
}
