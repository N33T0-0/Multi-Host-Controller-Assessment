using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Multi_Host_Controller_Assessment.Configuration;

namespace Multi_Host_Controller_Assessment.Services;

/// <summary>
/// Issues RS256 JWT access tokens using an asymmetric RSA key.
/// </summary>
public class JwtTokenService : IJwtTokenService, IDisposable
{
    private readonly JwtOptions _options;
    private readonly RSA _rsa;
    private readonly RsaSecurityKey _securityKey;
    private readonly JwtSecurityTokenHandler _tokenHandler = new();

    public JwtTokenService(IOptions<JwtOptions> options, ILogger<JwtTokenService> logger, IHostEnvironment environment)
    {
        _options = options.Value;
        _options.Validate(requireKeyFile: !environment.IsDevelopment());

        _rsa = RSA.Create();
        var keyFilePath = string.IsNullOrWhiteSpace(_options.KeyFilePath)
            ? CreateOrLoadDevelopmentKey()
            : _options.KeyFilePath;
        var pem = File.ReadAllText(keyFilePath);
        _rsa.ImportFromPem(pem);
        if (_rsa.KeySize < 2048)
        {
            _rsa.Dispose();
            throw new InvalidOperationException("JWT RSA signing key must be at least 2048 bits.");
        }
        try { _ = _rsa.ExportParameters(includePrivateParameters: true); }
        catch (CryptographicException ex)
        {
            _rsa.Dispose();
            throw new InvalidOperationException("Configured JWT PEM file must contain the RSA private key.", ex);
        }
        logger.LogInformation("Loaded persistent RSA signing key for the Master Host.");

        _securityKey = new RsaSecurityKey(_rsa);
    }

    private static string CreateOrLoadDevelopmentKey()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
            localData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

        var directory = Path.Combine(localData, "MultiHostController", "Development");
        Directory.CreateDirectory(directory);
        var keyPath = Path.Combine(directory, "master-host-private.pem");
        if (File.Exists(keyPath)) return keyPath;

        using var generatedKey = RSA.Create(3072);
        var temporaryPath = keyPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, generatedKey.ExportPkcs8PrivateKeyPem());
            try
            {
                File.Move(temporaryPath, keyPath, overwrite: false);
            }
            catch (IOException) when (File.Exists(keyPath))
            {
                // Another local development process generated the persistent key first.
            }
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }

        return keyPath;
    }

    /// <inheritdoc />
    public SecurityKey GetValidationKey() => _securityKey;

    /// <inheritdoc />
    public string GenerateToken(string clientAgentId, TimeSpan? lifetime = null)
    {
        if (string.IsNullOrWhiteSpace(clientAgentId))
        {
            throw new ArgumentException("ClientAgentId cannot be null or whitespace.", nameof(clientAgentId));
        }

        var now = DateTime.UtcNow;
        var tokenLifetime = lifetime ?? TimeSpan.FromSeconds(_options.TokenLifetimeSeconds);
        var expires = now.Add(tokenLifetime);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, clientAgentId),
            new("client_agent_id", clientAgentId),
            new(ClaimTypes.Role, "ClientAgent"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        var signingCredentials = new SigningCredentials(_securityKey, SecurityAlgorithms.RsaSha256);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            NotBefore = now,
            IssuedAt = now,
            Expires = expires,
            SigningCredentials = signingCredentials
        };

        var token = _tokenHandler.CreateToken(tokenDescriptor);
        return _tokenHandler.WriteToken(token);
    }

    public void Dispose()
    {
        _rsa.Dispose();
        GC.SuppressFinalize(this);
    }
}

