using Client_Agent.Configuration;
using Microsoft.Extensions.Options;

namespace Client_Agent.Services;

/// <summary>
/// Manages loading and atomic first-run generation of the local ClientAgentID UUID file.
/// </summary>
public class AgentIdProvider : IAgentIdProvider
{
    private static readonly object ProcessLock = new();

    private readonly ClientAgentOptions _options;
    private readonly ILogger<AgentIdProvider> _logger;
    private string? _cachedAgentId;

    public AgentIdProvider(IOptions<ClientAgentOptions> options, ILogger<AgentIdProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public string GetOrCreateAgentId()
    {
        if (_cachedAgentId != null)
        {
            return _cachedAgentId;
        }

        lock (ProcessLock)
        {
            if (_cachedAgentId != null)
            {
                return _cachedAgentId;
            }

            var filePath = ResolveFilePath();

            if (File.Exists(filePath))
            {
                var content = SafeReadAllTextWithRetry(filePath);

                if (Guid.TryParseExact(content, "D", out var parsedGuid))
                {
                    _cachedAgentId = parsedGuid.ToString("D");
                    _logger.LogInformation("Loaded persisted client agent ID '{AgentId}' from '{Path}'.", _cachedAgentId, filePath);
                    return _cachedAgentId;
                }

                throw new InvalidOperationException(
                    $"Persisted client agent ID in '{filePath}' is invalid or malformed: '{content}'. " +
                    "The file must contain a valid 36-character canonical UUID. " +
                    "Please repair or restore the file without silently changing the agent identity.");
            }

            // Atomic generation for first-run
            var newGuid = Guid.NewGuid().ToString("D");
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempFilePath = Path.Combine(
                directory ?? AppContext.BaseDirectory,
                $".tmp-client-agent-id-{Guid.NewGuid():N}");

            try
            {
                File.WriteAllText(tempFilePath, newGuid);
                File.Move(tempFilePath, filePath, overwrite: false);

                _cachedAgentId = newGuid;
                _logger.LogInformation("Generated and persisted new client agent ID '{AgentId}' to '{Path}'.", newGuid, filePath);
                return _cachedAgentId;
            }
            catch (IOException ex) when (File.Exists(filePath))
            {
                // Simultaneous process created the file; read and reuse that file with retry
                try
                {
                    if (File.Exists(tempFilePath))
                    {
                        File.Delete(tempFilePath);
                    }
                }
                catch
                {
                    // Ignore temp cleanup errors
                }

                var content = SafeReadAllTextWithRetry(filePath);
                if (Guid.TryParseExact(content, "D", out var existingGuid))
                {
                    _cachedAgentId = existingGuid.ToString("D");
                    _logger.LogInformation("Reused concurrently created client agent ID '{AgentId}' from '{Path}'.", _cachedAgentId, filePath);
                    return _cachedAgentId;
                }

                throw new InvalidOperationException(
                    $"Concurrent creation conflict at '{filePath}', but existing content is invalid: '{content}'.",
                    ex);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempFilePath))
                    {
                        File.Delete(tempFilePath);
                    }
                }
                catch
                {
                    // Ignore temp cleanup errors
                }
            }
        }
    }

    private string ResolveFilePath()
    {
        if (!string.IsNullOrWhiteSpace(_options.IdFilePath))
        {
            return Path.GetFullPath(_options.IdFilePath);
        }

        return Path.Combine(AppContext.BaseDirectory, "client-agent-id");
    }

    private static string SafeReadAllTextWithRetry(string filePath)
    {
        for (var attempt = 1; attempt <= 10; attempt++)
        {
            try
            {
                using var stream = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd().Trim();
            }
            catch (IOException) when (attempt < 10)
            {
                Thread.Sleep(25);
            }
        }

        return File.ReadAllText(filePath).Trim();
    }
}

