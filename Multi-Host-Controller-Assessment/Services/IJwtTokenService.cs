using Microsoft.IdentityModel.Tokens;

namespace Multi_Host_Controller_Assessment.Services;

/// <summary>
/// Service contract for issuing and validating JWT access tokens with asymmetric RSA signing.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>
    /// Gets the public or symmetric security key used for validating signatures.
    /// </summary>
    SecurityKey GetValidationKey();

    /// <summary>
    /// Generates a signed RS256 JWT access token for the specified client agent.
    /// </summary>
    string GenerateToken(string clientAgentId, TimeSpan? lifetime = null);
}

