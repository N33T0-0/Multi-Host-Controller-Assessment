using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Multi_Host_Controller_Assessment.Services;

/// <summary>
/// Helper for generating, decoding, and computing SHA-256 hashes of agent secrets.
/// </summary>
public static class ClientAgentCredentialHelper
{
    /// <summary>
    /// Computes the SHA-256 hash for an agent secret string.
    /// The provisioned secret must be a Base64URL encoding of exactly 32 random bytes.
    /// </summary>
    public static byte[] ComputeSecretHash(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new ArgumentException("Secret cannot be null or whitespace.", nameof(secret));
        }

        var rawBytes = Base64UrlEncoder.DecodeBytes(secret.Trim());
        if (rawBytes.Length != 32)
            throw new ArgumentException("Client secret must decode to exactly 32 bytes.", nameof(secret));

        return SHA256.HashData(rawBytes);
    }

    /// <summary>
    /// Generates a new 32-byte cryptographically secure secret (Base64URL) and its SHA-256 hash formatted as SQL binary literal.
    /// </summary>
    public static (string AgentSecret, byte[] HashBytes, string SqlHexLiteral) GenerateProvisioningCredentials()
    {
        var rawBytes = RandomNumberGenerator.GetBytes(32);
        var agentSecret = Base64UrlEncoder.Encode(rawBytes);
        var hashBytes = SHA256.HashData(rawBytes);
        var sqlHexLiteral = "0x" + Convert.ToHexString(hashBytes).ToLowerInvariant();
        return (agentSecret, hashBytes, sqlHexLiteral);
    }

    /// <summary>
    /// Constant-time comparison of two byte sequences.
    /// </summary>
    public static bool FixedTimeEquals(byte[]? a, byte[]? b)
    {
        if (a == null || b == null || a.Length != b.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}

