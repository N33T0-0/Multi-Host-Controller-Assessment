namespace Multi_Host_Controller_Assessment.Data.Entities;

/// <summary>
/// Represents a logged heartbeat and resource metrics record mapped to dbo.ClientHostHeartbeatLogs.
/// </summary>
public class ClientHostHeartbeatLog
{
    public long Id { get; set; }

    public string ClientAgentId { get; set; } = string.Empty;

    public DateTime LoggedDateTime { get; set; }

    public string? ResourceUsageMetrics { get; set; }
}

