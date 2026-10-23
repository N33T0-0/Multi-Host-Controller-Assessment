namespace Client_Agent.Models;

/// <summary>
/// JSON request payload sent to POST /api/master-host/heartbeat/{id}.
/// Compatible with Master Host's HeartbeatRequest DTO.
/// </summary>
public class HeartbeatAgentRequest
{
    public object? ResourceUsageMetrics { get; set; }
}

