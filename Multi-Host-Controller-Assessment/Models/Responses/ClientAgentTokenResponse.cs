using System.Text.Json.Serialization;

namespace Multi_Host_Controller_Assessment.Models.Responses;

/// <summary>
/// Response payload containing the issued JWT access token.
/// </summary>
public class ClientAgentTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = "Bearer";

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

