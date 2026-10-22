using System.Text.Json;

namespace Multi_Host_Controller_Assessment.Models.Requests;

/// <summary>
/// Optional request payload for client agent heartbeat submission.
/// </summary>
public class HeartbeatRequest
{
    /// <summary>
    /// Optional resource usage metrics. Can be submitted as:
    /// 1. A JSON object/array/value (e.g., {"cpuUsage": 15.2, "memoryUsageMb": 1024}), OR
    /// 2. A serialized JSON string containing valid JSON text.
    /// It is stored as JSON text in NVARCHAR(MAX). If null or omitted, null is stored.
    /// </summary>
    public JsonElement? ResourceUsageMetrics { get; set; }
}

