namespace Client_Agent.Models;

/// <summary>
/// JSON request payload sent to POST /api/master-host/register.
/// Compatible with Master Host's RegisterClientAgentRequest DTO.
/// </summary>
public class RegisterAgentRequest
{
    public string ClientAgentId { get; set; } = string.Empty;

    public string ClientAgentName { get; set; } = string.Empty;

    public string ClientAgentOS { get; set; } = string.Empty;

    public string ClientAgentVersion { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;
}

