using System.Security.Cryptography;
using System.Text;
using Client_Agent.Configuration;
using Microsoft.Extensions.Options;

namespace Client_Agent.Services;

/// <summary>Persists the provisioned agent secret using Windows DPAPI or owner-only Unix file permissions.</summary>
public sealed class AgentCredentialProvider : IAgentCredentialProvider
{
    public const string SecretEnvironmentVariable = "CLIENT_AGENT_SECRET";
    public const string DefaultSecretFileName = "client-agent-secret";
    private const string DpapiPrefix = "DPAPI1:";
    private const string UnixPrefix = "FILE1:";

    private readonly ClientAgentOptions _options;
    private readonly object _lock = new();
    private string? _cachedSecret;

    public AgentCredentialProvider(IOptions<ClientAgentOptions> options, ILogger<AgentCredentialProvider> logger)
    {
        _options = options.Value;
    }

    public string GetSecret()
    {
        lock (_lock)
        {
            if (_cachedSecret is not null) return _cachedSecret;

            var path = string.IsNullOrWhiteSpace(_options.SecretFilePath)
                ? Path.Combine(AppContext.BaseDirectory, DefaultSecretFileName)
                : _options.SecretFilePath;
            if (File.Exists(path))
            {
                _cachedSecret = ReadProtectedSecret(path);
                return _cachedSecret;
            }

            var secret = !string.IsNullOrWhiteSpace(_options.Secret)
                ? _options.Secret.Trim()
                : Environment.GetEnvironmentVariable(SecretEnvironmentVariable)?.Trim();
            if (string.IsNullOrWhiteSpace(secret))
            {
                throw new InvalidOperationException(
                    "Client agent secret is not provisioned. Supply ClientAgent:Secret or CLIENT_AGENT_SECRET on first startup.");
            }

            PersistProtectedSecret(path, secret);
            _cachedSecret = secret;
            return secret;
        }
    }

    private static void PersistProtectedSecret(string path, string secret)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var bytes = Encoding.UTF8.GetBytes(secret);
        byte[] stored;
        if (OperatingSystem.IsWindows())
        {
            stored = Encoding.ASCII.GetBytes(DpapiPrefix)
                .Concat(ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser))
                .ToArray();
        }
        else
        {
            stored = Encoding.ASCII.GetBytes(UnixPrefix).Concat(bytes).ToArray();
        }

        var fileOptions = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None
        };
        if (!OperatingSystem.IsWindows()) fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var stream = new FileStream(path, fileOptions))
        {
            stream.Write(stored);
            stream.Flush(flushToDisk: true);
        }
        CryptographicOperations.ZeroMemory(bytes);
    }

    private static string ReadProtectedSecret(string path)
    {
        var stored = File.ReadAllBytes(path);
        var prefixLength = Encoding.ASCII.GetByteCount(DpapiPrefix);
        if (stored.AsSpan().StartsWith(Encoding.ASCII.GetBytes(DpapiPrefix)))
        {
            if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("This agent secret was protected for Windows DPAPI and cannot be read on this OS.");
            var clear = ProtectedData.Unprotect(stored.AsSpan(prefixLength).ToArray(), null, DataProtectionScope.CurrentUser);
            var result = Encoding.UTF8.GetString(clear);
            CryptographicOperations.ZeroMemory(clear);
            CryptographicOperations.ZeroMemory(stored);
            return result;
        }

        var unixPrefix = Encoding.ASCII.GetBytes(UnixPrefix);
        if (stored.AsSpan().StartsWith(unixPrefix))
        {
            if (OperatingSystem.IsWindows()) throw new InvalidOperationException("The local agent secret file is not protected for Windows DPAPI.");
            var mode = File.GetUnixFileMode(path);
            if (mode != (UnixFileMode.UserRead | UnixFileMode.UserWrite))
                throw new InvalidOperationException("Client agent secret file permissions must be owner-only (mode 600).");
            var result = Encoding.UTF8.GetString(stored, unixPrefix.Length, stored.Length - unixPrefix.Length);
            CryptographicOperations.ZeroMemory(stored);
            return result;
        }

        // Upgrade the plaintext file written by the interrupted implementation.
        var legacySecret = Encoding.UTF8.GetString(stored).Trim();
        CryptographicOperations.ZeroMemory(stored);
        try
        {
            var base64 = legacySecret.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
            var raw = Convert.FromBase64String(base64);
            if (raw.Length != 32) throw new FormatException();
            CryptographicOperations.ZeroMemory(raw);
        }
        catch
        {
            throw new InvalidOperationException("Client agent secret file is unrecognized or invalid. Reprovision the secret securely.");
        }

        var temporaryPath = path + ".protected-" + Guid.NewGuid().ToString("N");
        try
        {
            PersistProtectedSecret(temporaryPath, legacySecret);
            File.Move(temporaryPath, path, overwrite: true);
            return legacySecret;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
