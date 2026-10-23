using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Client_Agent.Services;

/// <summary>
/// Acquires, in-memory caches, and automatically refreshes JWT access tokens.
/// </summary>
public class AgentTokenProvider : IAgentTokenProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly IAgentIdProvider _agentIdProvider;
    private readonly IAgentCredentialProvider _credentialProvider;
    private readonly ILogger<AgentTokenProvider> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;
    private DateTimeOffset _refreshAt = DateTimeOffset.MinValue;

    public AgentTokenProvider(
        HttpClient httpClient,
        IAgentIdProvider agentIdProvider,
        IAgentCredentialProvider credentialProvider,
        ILogger<AgentTokenProvider> logger)
    {
        _httpClient = httpClient;
        _agentIdProvider = agentIdProvider;
        _credentialProvider = credentialProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> GetTokenAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        if (!forceRefresh && !string.IsNullOrEmpty(_cachedToken) && now < _refreshAt)
        {
            return _cachedToken;
        }

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            now = DateTimeOffset.UtcNow;
            if (!forceRefresh && !string.IsNullOrEmpty(_cachedToken) && now < _refreshAt)
            {
                return _cachedToken;
            }

            var agentId = _agentIdProvider.GetOrCreateAgentId();
            var secret = _credentialProvider.GetSecret();

            var payload = new
            {
                clientAgentId = agentId,
                clientSecret = secret
            };

            const int maxAttempts = 3;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    using var response = await _httpClient.PostAsJsonAsync(
                        "api/master-host/auth/token",
                        payload,
                        JsonOptions,
                        cancellationToken);

                    if (response.IsSuccessStatusCode)
                    {
                        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                        var accessToken = doc.RootElement.GetProperty("access_token").GetString();
                        var expiresIn = doc.RootElement.GetProperty("expires_in").GetInt32();

                        if (string.IsNullOrWhiteSpace(accessToken))
                        {
                            throw new InvalidOperationException("Master Host token response missing 'access_token'.");
                        }
                        if (expiresIn <= 0 || expiresIn > 3600)
                        {
                            throw new InvalidOperationException("Master Host token response has an invalid lifetime.");
                        }

                        _cachedToken = accessToken;
                        _expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
                        _refreshAt = _expiresAt.AddSeconds(-Math.Min(60, Math.Max(5, expiresIn / 10)));
                        _logger.LogInformation("Successfully acquired JWT access token (expires in {ExpiresIn}s).", expiresIn);
                        return accessToken;
                    }

                    if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    {
                        throw new AgentAuthenticationException("Master Host rejected the configured client agent credential.");
                    }

                    var isTransient = (int)response.StatusCode >= 500 ||
                                      response.StatusCode is HttpStatusCode.RequestTimeout or (HttpStatusCode)429;

                    _logger.LogWarning("Token request returned HTTP {StatusCode} on attempt {Attempt}/{MaxAttempts}.",
                        (int)response.StatusCode, attempt, maxAttempts);

                    if (!isTransient || attempt == maxAttempts)
                    {
                        throw new InvalidOperationException($"Failed to acquire access token: HTTP {(int)response.StatusCode}.");
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (AgentAuthenticationException)
                {
                    throw;
                }
                catch (Exception ex) when (attempt < maxAttempts && ex is not InvalidOperationException)
                {
                    _logger.LogWarning(ex, "Transient error acquiring token on attempt {Attempt}/{MaxAttempts}.", attempt, maxAttempts);
                }

                await Task.Delay(TimeSpan.FromSeconds(Math.Min(1 << (attempt - 1), 4)), cancellationToken);
            }

            throw new InvalidOperationException("Token acquisition failed after multiple attempts.");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public void InvalidateToken()
    {
        _cachedToken = null;
        _expiresAt = DateTimeOffset.MinValue;
        _refreshAt = DateTimeOffset.MinValue;
    }
}

