namespace Client_Agent.Services;

public sealed class AgentAuthenticationException : Exception
{
    public AgentAuthenticationException(string message) : base(message) { }
}

/// <summary>
/// Service contract for acquiring and caching short-lived JWT access tokens from the Master Host.
/// </summary>
public interface IAgentTokenProvider
{
    /// <summary>
    /// Gets a valid JWT access token, acquiring or refreshing it as needed.
    /// </summary>
    Task<string> GetTokenAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates the currently cached JWT token.
    /// </summary>
    void InvalidateToken();
}

