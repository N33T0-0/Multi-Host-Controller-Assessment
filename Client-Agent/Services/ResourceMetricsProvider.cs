using System.Diagnostics;
using System.Runtime.InteropServices;
using Client_Agent.Models;

namespace Client_Agent.Services;

/// <summary>
/// Cross-platform implementation of resource metrics collection using standard .NET 8 runtime diagnostics.
/// Valid across Windows, Linux, and macOS.
/// </summary>
public class ResourceMetricsProvider : IResourceMetricsProvider
{
    private readonly object _sync = new();
    private TimeSpan _lastCpuTime;
    private DateTimeOffset _lastSampleTime;
    private bool _hasInitialSample;

    public ResourceMetricsProvider()
    {
        try
        {
            using var currentProcess = Process.GetCurrentProcess();
            _lastCpuTime = currentProcess.TotalProcessorTime;
            _lastSampleTime = DateTimeOffset.UtcNow;
            _hasInitialSample = true;
        }
        catch
        {
            _hasInitialSample = false;
        }
    }

    public ResourceMetrics GetMetrics()
    {
        var now = DateTimeOffset.UtcNow;
        var metrics = new ResourceMetrics
        {
            TimestampUtc = now
        };

        // Host environment metrics
        var processorCount = Environment.ProcessorCount;
        metrics.Host.ProcessorCount = processorCount;
        metrics.Host.OperatingSystem = RuntimeInformation.OSDescription;

        // Native .NET 8 GC info provides cross-platform host / container memory limit
        try
        {
            var gcInfo = GC.GetGCMemoryInfo();
            metrics.Host.TotalAvailableMemoryBytes = gcInfo.TotalAvailableMemoryBytes;
        }
        catch
        {
            metrics.Host.TotalAvailableMemoryBytes = 0;
        }

        // Process-specific metrics
        try
        {
            using var currentProcess = Process.GetCurrentProcess();
            metrics.Process.WorkingSetBytes = currentProcess.WorkingSet64;
            metrics.Process.PrivateMemoryBytes = currentProcess.PrivateMemorySize64;

            lock (_sync)
            {
                var currentCpuTime = currentProcess.TotalProcessorTime;
                if (_hasInitialSample && processorCount > 0)
                {
                    var timeElapsedMs = (now - _lastSampleTime).TotalMilliseconds;
                    var cpuElapsedMs = (currentCpuTime - _lastCpuTime).TotalMilliseconds;

                    if (timeElapsedMs > 0)
                    {
                        var totalCpuCapacityMs = timeElapsedMs * processorCount;
                        var rawPercentage = (cpuElapsedMs / totalCpuCapacityMs) * 100.0;
                        metrics.Process.CpuUsagePercentage = Math.Clamp(Math.Round(rawPercentage, 2), 0.0, 100.0);
                    }
                }
                else
                {
                    metrics.Process.CpuUsagePercentage = 0.0;
                    _hasInitialSample = true;
                }

                _lastCpuTime = currentCpuTime;
                _lastSampleTime = now;
            }
        }
        catch
        {
            // If process inspection fails on restricted environments
            metrics.Process.WorkingSetBytes = 0;
            metrics.Process.PrivateMemoryBytes = 0;
            metrics.Process.CpuUsagePercentage = 0.0;
        }

        return metrics;
    }
}

