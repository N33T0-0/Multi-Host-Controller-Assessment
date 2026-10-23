namespace Multi_Host_Controller_Assessment.Configuration;

/// <summary>
/// Dashboard display and behaviour configuration options.
/// </summary>
public class DashboardOptions
{
    public const string SectionName = "Dashboard";

    /// <summary>
    /// Number of seconds since the latest heartbeat within which a host is
    /// considered online. Defaults to 180 seconds (three one-minute intervals).
    /// Must be a positive integer.
    /// </summary>
    public int OnlineThresholdSeconds { get; set; } = 180;

    /// <summary>
    /// Validates that OnlineThresholdSeconds is a positive value.
    /// </summary>
    public void Validate()
    {
        if (OnlineThresholdSeconds <= 0)
        {
            throw new InvalidOperationException(
                $"Configuration 'Dashboard:OnlineThresholdSeconds' must be a positive integer (got: {OnlineThresholdSeconds}).");
        }
    }
}

