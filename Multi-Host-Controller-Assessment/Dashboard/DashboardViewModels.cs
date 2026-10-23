namespace Multi_Host_Controller_Assessment.Dashboard;

/// <summary>
/// Summary row for the hosts list page.
/// </summary>
public class HostSummaryViewModel
{
    public string ClientAgentId { get; init; } = string.Empty;
    public string ClientAgentName { get; init; } = string.Empty;
    public string ClientAgentOS { get; init; } = string.Empty;
    public string ClientAgentVersion { get; init; } = string.Empty;

    /// <summary>
    /// The latest heartbeat timestamp from dbo.ClientHostHeartbeatLogs,
    /// or null if this host has never sent a heartbeat.
    /// Note: stored as SQL Server DATETIME (local server time, no timezone).
    /// </summary>
    public DateTime? LatestHeartbeatAt { get; init; }

    /// <summary>
    /// Derived from LatestHeartbeatAt versus the configured threshold.
    /// Hosts that have never sent a heartbeat are always offline.
    /// </summary>
    public bool IsOnline { get; init; }

    /// <summary>
    /// Indicates whether the host has been soft-deactivated.
    /// Deactivated takes precedence over IsOnline in presentation.
    /// </summary>
    public bool IsDeactivated { get; init; }

    /// <summary>
    /// Timestamp when the host was soft-deactivated, or null if active.
    /// </summary>
    public DateTime? DeletedAt { get; init; }
}

/// <summary>
/// Parsed process-scope CPU and memory metrics.
/// </summary>
public class ProcessMetricsViewModel
{
    /// <summary>CPU usage as a percentage (0.0–100.0), normalized across all logical processors.</summary>
    public double? CpuUsagePercentage { get; init; }
    public string CpuUsageUnit { get; init; } = "percent_normalized_to_total_processors";

    /// <summary>Process working-set physical memory, in bytes.</summary>
    public long? WorkingSetBytes { get; init; }

    /// <summary>Process committed private memory, in bytes.</summary>
    public long? PrivateMemoryBytes { get; init; }
}

/// <summary>
/// Parsed host-environment metrics.
/// </summary>
public class HostEnvironmentMetricsViewModel
{
    public int? ProcessorCount { get; init; }
    public long? TotalAvailableMemoryBytes { get; init; }
    public string? OperatingSystem { get; init; }
}

/// <summary>
/// Detail page view model for a single registered host.
/// </summary>
public class HostDetailViewModel
{
    public string ClientAgentId { get; init; } = string.Empty;
    public string ClientAgentName { get; init; } = string.Empty;
    public string ClientAgentOS { get; init; } = string.Empty;
    public string ClientAgentVersion { get; init; } = string.Empty;
    public DateTime RegistrationDateTime { get; init; }

    /// <summary>
    /// The latest heartbeat timestamp, or null if none has been received.
    /// Note: stored as SQL Server DATETIME (local server time, no timezone).
    /// </summary>
    public DateTime? LatestHeartbeatAt { get; init; }

    public bool IsOnline { get; init; }

    /// <summary>
    /// Indicates whether the host has been soft-deactivated.
    /// </summary>
    public bool IsDeactivated { get; init; }

    /// <summary>
    /// Timestamp when the host was soft-deactivated, or null if active.
    /// </summary>
    public DateTime? DeletedAt { get; init; }

    /// <summary>Latest parsed process metrics, or null if metrics were absent or unparseable.</summary>
    public ProcessMetricsViewModel? ProcessMetrics { get; init; }

    /// <summary>Latest parsed host-environment metrics, or null if absent or unparseable.</summary>
    public HostEnvironmentMetricsViewModel? HostEnvironmentMetrics { get; init; }

    /// <summary>
    /// Indicates whether metrics were present but could not be fully parsed.
    /// Used to show a warning rather than silently hiding data.
    /// </summary>
    public bool MetricsParseWarning { get; init; }
}
