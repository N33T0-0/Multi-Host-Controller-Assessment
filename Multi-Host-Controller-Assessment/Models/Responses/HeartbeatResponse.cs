namespace Multi_Host_Controller_Assessment.Models.Responses;

/// <summary>
/// Response payload for a recorded heartbeat.
/// </summary>
public class HeartbeatResponse
{
    /// <summary>
    /// Stable external identifier of the client agent.
    /// </summary>
    public string ClientAgentId { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when the heartbeat log entry was recorded.
    /// </summary>
    public DateTime LoggedDateTime { get; set; }

    /// <summary>
    /// Updated active status of the client host.
    /// </summary>
    public bool Status { get; set; }

    /// <summary>
    /// Informational message regarding the heartbeat outcome.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}

