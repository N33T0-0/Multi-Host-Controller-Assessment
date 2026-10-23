namespace Multi_Host_Controller_Assessment.Data.Entities;

/// <summary>
/// Pre-provisioned and bound credentials for client agent authentication, mapped to dbo.ClientAgentCredentials.
/// </summary>
public class ClientAgentCredential
{
    public long Id { get; set; }

    public long? ClientHostId { get; set; }

    public byte[] SecretHash { get; set; } = Array.Empty<byte>();

    public DateTime CreatedAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public DateTime? LastUsedAt { get; set; }

    public ClientHost? ClientHost { get; set; }
}

