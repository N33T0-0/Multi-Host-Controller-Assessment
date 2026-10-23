namespace Client_Agent.Services;

/// <summary>
/// Provides access to the stable local ClientAgentId UUID.
/// </summary>
public interface IAgentIdProvider
{
    /// <summary>
    /// Retrieves the existing persisted UUID or atomically generates and persists a new one on first run.
    /// Fails clearly if the persisted file is corrupt or invalid.
    /// </summary>
    string GetOrCreateAgentId();
}

