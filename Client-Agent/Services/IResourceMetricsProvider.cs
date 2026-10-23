using Client_Agent.Models;

namespace Client_Agent.Services;

/// <summary>
/// Provides cross-platform resource utilization metrics for the Client Agent and its hosting environment.
/// </summary>
public interface IResourceMetricsProvider
{
    /// <summary>
    /// Samples current process and host resource usage metrics.
    /// </summary>
    ResourceMetrics GetMetrics();
}

