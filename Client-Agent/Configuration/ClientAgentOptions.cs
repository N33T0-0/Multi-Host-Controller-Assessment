using System.Text.RegularExpressions;

namespace Client_Agent.Configuration;

/// <summary>
/// Configuration options for the Client Agent worker service.
/// </summary>
public class ClientAgentOptions
{
    public const string SectionName = "ClientAgent";

    private static readonly Regex DistributionNameRegex = new(@"\A[a-zA-Z0-9._-]+\z", RegexOptions.Compiled);
    private static readonly char[] DisallowedShellChars = [';', '&', '|', '`', '$', '\r', '\n'];

    /// <summary>
    /// Friendly name or hostname for this client agent (max 300 chars, required).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Agent software version (max 20 chars, required). Intended to be updated by CI/CD.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Base URL of the Master Host API (e.g., "https://localhost:7123" or "http://localhost:5000").
    /// </summary>
    public string MasterHostBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Heartbeat transmission interval in seconds. Defaults to 60 seconds (1 minute).
    /// </summary>
    public int HeartbeatIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Polling interval for claimed deployment jobs in seconds.
    /// </summary>
    public int DeploymentPollIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// WSL distribution name containing the target environment (e.g., "Ubuntu").
    /// </summary>
    public string WslDistribution { get; set; } = "Ubuntu";

    /// <summary>
    /// Ansible playbook executable name or path inside the WSL distribution (e.g., "ansible-playbook").
    /// </summary>
    public string AnsiblePlaybookCommand { get; set; } = "ansible-playbook";

    /// <summary>
    /// Linux working directory root where per-job directories are staged inside WSL (e.g., "~/.multi-host-controller/jobs").
    /// </summary>
    public string LinuxWorkDirectory { get; set; } = "~/.multi-host-controller/jobs";

    /// <summary>
    /// Maximum execution time in seconds allowed for an individual playbook step before timing out.
    /// </summary>
    public int PlaybookTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Grace period in seconds to wait for a child process to exit gracefully before forceful termination.
    /// </summary>
    public int ProcessGracePeriodSeconds { get; set; } = 10;

    /// <summary>
    /// Optional override file path where clientAgentId UUID is persisted.
    /// When null or empty, defaults to Path.Combine(AppContext.BaseDirectory, "client-agent-id").
    /// </summary>
    public string? IdFilePath { get; set; }

    /// <summary>
    /// Validates the configuration values according to contracts and security constraints.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidOperationException("Configuration 'ClientAgent:Name' is required and cannot be empty or whitespace.");
        }
        if (Name.Trim().Length > 300)
        {
            throw new InvalidOperationException($"Configuration 'ClientAgent:Name' must not exceed 300 characters (length: {Name.Length}).");
        }

        if (string.IsNullOrWhiteSpace(Version))
        {
            throw new InvalidOperationException("Configuration 'ClientAgent:Version' is required and cannot be empty or whitespace.");
        }
        if (Version.Trim().Length > 20)
        {
            throw new InvalidOperationException($"Configuration 'ClientAgent:Version' must not exceed 20 characters (length: {Version.Length}).");
        }

        if (string.IsNullOrWhiteSpace(MasterHostBaseUrl))
        {
            throw new InvalidOperationException("Configuration 'ClientAgent:MasterHostBaseUrl' is required.");
        }
        if (!Uri.TryCreate(MasterHostBaseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException($"Configuration 'ClientAgent:MasterHostBaseUrl' must be a valid absolute HTTP or HTTPS URL (got: '{MasterHostBaseUrl}').");
        }

        if (HeartbeatIntervalSeconds <= 0)
        {
            throw new InvalidOperationException($"Configuration 'ClientAgent:HeartbeatIntervalSeconds' must be greater than zero (got: {HeartbeatIntervalSeconds}).");
        }

        if (DeploymentPollIntervalSeconds <= 0 || DeploymentPollIntervalSeconds > 300)
        {
            throw new InvalidOperationException("ClientAgent:DeploymentPollIntervalSeconds must be between 1 and 300.");
        }

        if (string.IsNullOrWhiteSpace(WslDistribution))
        {
            throw new InvalidOperationException("Configuration 'ClientAgent:WslDistribution' is required and cannot be empty or whitespace.");
        }
        if (WslDistribution.Length > 100 || !DistributionNameRegex.IsMatch(WslDistribution))
        {
            throw new InvalidOperationException($"Configuration 'ClientAgent:WslDistribution' contains invalid characters or exceeds 100 characters (got: '{WslDistribution}').");
        }

        if (string.IsNullOrWhiteSpace(AnsiblePlaybookCommand))
        {
            throw new InvalidOperationException("Configuration 'ClientAgent:AnsiblePlaybookCommand' is required and cannot be empty or whitespace.");
        }
        if (AnsiblePlaybookCommand.Length > 260 || AnsiblePlaybookCommand.IndexOfAny(DisallowedShellChars) >= 0)
        {
            throw new InvalidOperationException($"Configuration 'ClientAgent:AnsiblePlaybookCommand' contains invalid characters or exceeds 260 characters (got: '{AnsiblePlaybookCommand}').");
        }

        if (string.IsNullOrWhiteSpace(LinuxWorkDirectory))
        {
            throw new InvalidOperationException("Configuration 'ClientAgent:LinuxWorkDirectory' is required and cannot be empty or whitespace.");
        }
        if (LinuxWorkDirectory.Length > 500 || (!LinuxWorkDirectory.StartsWith("/") && !LinuxWorkDirectory.StartsWith("~")))
        {
            throw new InvalidOperationException($"Configuration 'ClientAgent:LinuxWorkDirectory' must be an absolute Linux path or start with '~' (got: '{LinuxWorkDirectory}').");
        }
        if (LinuxWorkDirectory.IndexOfAny(DisallowedShellChars) >= 0)
        {
            throw new InvalidOperationException($"Configuration 'ClientAgent:LinuxWorkDirectory' contains invalid characters (got: '{LinuxWorkDirectory}').");
        }

        if (PlaybookTimeoutSeconds < 5 || PlaybookTimeoutSeconds > 3600)
        {
            throw new InvalidOperationException($"Configuration 'ClientAgent:PlaybookTimeoutSeconds' must be between 5 and 3600 seconds (got: {PlaybookTimeoutSeconds}).");
        }

        if (ProcessGracePeriodSeconds < 1 || ProcessGracePeriodSeconds > 60)
        {
            throw new InvalidOperationException($"Configuration 'ClientAgent:ProcessGracePeriodSeconds' must be between 1 and 60 seconds (got: {ProcessGracePeriodSeconds}).");
        }
    }
}
