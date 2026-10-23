namespace Multi_Host_Controller_Assessment.Data.Entities;

/// <summary>
/// Represents a registered client host agent mapped to dbo.ClientHosts.
/// </summary>
public class ClientHost
{
    public long Id { get; set; }

    public string ClientAgentId { get; set; } = string.Empty;

    public string ClientAgentName { get; set; } = string.Empty;

    public string ClientAgentOS { get; set; } = string.Empty;

    public string ClientAgentVersion { get; set; } = string.Empty;

    public DateTime ClientRegistrationDateTime { get; set; }

    public bool Status { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }
}

