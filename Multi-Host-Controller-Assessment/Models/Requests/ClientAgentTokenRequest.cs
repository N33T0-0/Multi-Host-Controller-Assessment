using System.ComponentModel.DataAnnotations;

namespace Multi_Host_Controller_Assessment.Models.Requests;

/// <summary>
/// Request to acquire an access token for a registered client agent.
/// </summary>
public class ClientAgentTokenRequest
{
    [Required(AllowEmptyStrings = false, ErrorMessage = "ClientAgentId is required.")]
    [StringLength(64, MinimumLength = 1)]
    public string ClientAgentId { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false, ErrorMessage = "ClientSecret is required.")]
    [StringLength(128, MinimumLength = 1)]
    public string ClientSecret { get; set; } = string.Empty;
}

