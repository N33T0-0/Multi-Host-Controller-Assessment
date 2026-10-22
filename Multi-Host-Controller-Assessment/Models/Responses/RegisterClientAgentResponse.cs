namespace Multi_Host_Controller_Assessment.Models.Responses;

/// <summary>
/// Response payload for client agent registration.
/// </summary>
public class RegisterClientAgentResponse
{
    /// <summary>
    /// Stable external identifier of the client agent.
    /// </summary>
    public string ClientAgentId { get; set; } = string.Empty;

    /// <summary>
    /// Friendly name or hostname of the client agent.
    /// </summary>
    public string ClientAgentName { get; set; } = string.Empty;

    /// <summary>
    /// Operating system description of the client agent.
    /// </summary>
    public string ClientAgentOS { get; set; } = string.Empty;

    /// <summary>
    /// Running version of the client agent software.
    /// </summary>
    public string ClientAgentVersion { get; set; } = string.Empty;

    /// <summary>
    /// Current active status of the client host.
    /// </summary>
    public bool Status { get; set; }

    /// <summary>
    /// Original registration timestamp recorded in the database.
    /// </summary>
    public DateTime ClientRegistrationDateTime { get; set; }

    /// <summary>
    /// Indicates whether this registration created a new record or refreshed an existing one.
    /// </summary>
    public bool IsNewRegistration { get; set; }

    /// <summary>
    /// Informational message regarding the registration outcome.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}

