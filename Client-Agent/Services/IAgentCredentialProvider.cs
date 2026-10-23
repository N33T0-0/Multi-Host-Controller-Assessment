namespace Client_Agent.Services;

/// <summary>
/// Abstraction for loading the local pre-provisioned secret for Client Agent authentication.
/// </summary>
public interface IAgentCredentialProvider
{
    /// <summary>
    /// Gets the pre-provisioned secret for this agent instance.
    /// </summary>
    string GetSecret();
}

