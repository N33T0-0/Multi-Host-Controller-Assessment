namespace Multi_Host_Controller_Assessment.Configuration;

/// <summary>
/// Configuration settings for JWT access token generation and validation.
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "MasterHost";

    public string Audience { get; set; } = "ClientAgent";

    public int TokenLifetimeSeconds { get; set; } = 600;

    public string? KeyFilePath { get; set; }

    public void Validate(bool requireKeyFile = true)
    {
        if (string.IsNullOrWhiteSpace(Issuer))
        {
            throw new InvalidOperationException("Configuration 'Jwt:Issuer' is required.");
        }

        if (string.IsNullOrWhiteSpace(Audience))
        {
            throw new InvalidOperationException("Configuration 'Jwt:Audience' is required.");
        }

        if (TokenLifetimeSeconds < 60 || TokenLifetimeSeconds > 3600)
        {
            throw new InvalidOperationException(
                $"Configuration 'Jwt:TokenLifetimeSeconds' must be between 60 and 3600 seconds (got: {TokenLifetimeSeconds}).");
        }

        if (string.IsNullOrWhiteSpace(KeyFilePath))
        {
            if (!requireKeyFile) return;
            throw new InvalidOperationException(
                "Configuration 'Jwt:KeyFilePath' must point to an existing protected PEM file containing the Master Host RSA private key.");
        }

        if (!File.Exists(KeyFilePath))
            throw new InvalidOperationException($"Configured JWT private key file was not found at '{KeyFilePath}'.");
    }
}

