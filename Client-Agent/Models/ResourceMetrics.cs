namespace Client_Agent.Models;

/// <summary>
/// Metric snapshot sent to Master Host via heartbeat.
/// </summary>
public class ResourceMetrics
{
    /// <summary>
    /// Explicit scope indicator differentiating process metrics from host-level environment data.
    /// </summary>
    public string Scope { get; set; } = "process_and_host_environment";

    /// <summary>
    /// UTC timestamp when metrics were sampled.
    /// </summary>
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// CPU and memory utilization specific to the running Client Agent worker process.
    /// </summary>
    public ProcessResourceMetrics Process { get; set; } = new();

    /// <summary>
    /// Host-level hardware and operating system environment information.
    /// </summary>
    public HostEnvironmentMetrics Host { get; set; } = new();
}

/// <summary>
/// Worker process-specific resource utilization metrics.
/// </summary>
public class ProcessResourceMetrics
{
    /// <summary>
    /// Estimated CPU utilization percentage for the current process normalized across all logical processors (0.0 to 100.0).
    /// </summary>
    public double CpuUsagePercentage { get; set; }

    public string CpuUsagePercentageUnit { get; set; } = "percent_normalized_to_total_processors";

    /// <summary>
    /// Physical memory currently allocated to the process (Working Set).
    /// </summary>
    public long WorkingSetBytes { get; set; }

    public string WorkingSetUnit { get; set; } = "bytes";

    /// <summary>
    /// Committed private memory size for the process.
    /// </summary>
    public long PrivateMemoryBytes { get; set; }

    public string PrivateMemoryUnit { get; set; } = "bytes";
}

/// <summary>
/// Host hardware and OS environment details available cross-platform in .NET 8.
/// </summary>
public class HostEnvironmentMetrics
{
    /// <summary>
    /// Number of logical processors available on the host machine.
    /// </summary>
    public int ProcessorCount { get; set; }

    /// <summary>
    /// Total physical memory available to the operating system or container environment.
    /// </summary>
    public long TotalAvailableMemoryBytes { get; set; }

    public string TotalAvailableMemoryUnit { get; set; } = "bytes";

    /// <summary>
    /// Operating system platform and version description.
    /// </summary>
    public string OperatingSystem { get; set; } = string.Empty;
}

