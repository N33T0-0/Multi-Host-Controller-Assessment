using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Client_Agent.Models;

namespace Client_Agent.Services;

/// <summary>
/// HTTP client implementation for communicating with the Master Host API with JWT Bearer authentication.
/// </summary>
public class MasterHostClient : IMasterHostClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly IAgentCredentialProvider _credentialProvider;
    private readonly IAgentTokenProvider _tokenProvider;
    private readonly ILogger<MasterHostClient> _logger;

    [ActivatorUtilitiesConstructor]
    public MasterHostClient(
        HttpClient httpClient,
        IAgentCredentialProvider credentialProvider,
        IAgentTokenProvider tokenProvider,
        ILogger<MasterHostClient> logger)
    {
        _httpClient = httpClient;
        _credentialProvider = credentialProvider;
        _tokenProvider = tokenProvider;
        _logger = logger;
    }

    /// <summary>
    /// Backwards-compatible constructor for testing without token acquisition infrastructure.
    /// </summary>
    public MasterHostClient(HttpClient httpClient, ILogger<MasterHostClient> logger)
        : this(httpClient, new NullAgentCredentialProvider(), new NullAgentTokenProvider(), logger)
    {
    }

    /// <inheritdoc />
    public async Task<RegistrationResultStatus> RegisterAsync(RegisterAgentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.ClientSecret))
            {
                try
                {
                    request.ClientSecret = _credentialProvider.GetSecret();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not load client secret for registration: {Message}", ex.Message);
                }
            }

            using var response = await _httpClient.PostAsJsonAsync(
                "api/master-host/register",
                request,
                JsonOptions,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "Registration accepted by Master Host with status code {StatusCode}.",
                    (int)response.StatusCode);
                return RegistrationResultStatus.Success;
            }

            if (response.StatusCode == HttpStatusCode.Gone)
            {
                _logger.LogError(
                    "Master Host has deactivated client agent '{AgentId}'. Registration will stop for this process.",
                    request.ClientAgentId);
                return RegistrationResultStatus.Deactivated;
            }
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                _logger.LogError("Master Host rejected the Client Agent credential during enrollment (HTTP {StatusCode}).", (int)response.StatusCode);
                return RegistrationResultStatus.AuthenticationFailed;
            }

            var responseBody = await SafeReadContentAsync(response.Content, cancellationToken);
            _logger.LogWarning(
                "Master Host registration returned non-success status {StatusCode}. Details: {Details}",
                (int)response.StatusCode,
                responseBody);

            return RegistrationResultStatus.Failed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Registration request canceled due to application shutdown.");
            throw;
        }
        catch (AgentAuthenticationException ex)
        {
            _logger.LogError("Client Agent authentication failed during enrollment: {Message}", ex.Message);
            return RegistrationResultStatus.AuthenticationFailed;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Transient error occurred while sending registration to Master Host: {Message}",
                ex.Message);
            return RegistrationResultStatus.Failed;
        }
    }

    /// <inheritdoc />
    public async Task<HeartbeatResultStatus> SendHeartbeatAsync(
        string clientAgentId,
        object? metrics,
        CancellationToken cancellationToken)
    {
        var escapedId = Uri.EscapeDataString(clientAgentId.Trim());
        var relativeUrl = $"api/master-host/heartbeat/{escapedId}";

        var payload = new HeartbeatAgentRequest
        {
            ResourceUsageMetrics = metrics
        };

        try
        {
            using var response = await SendWithAuthAsync(() =>
            {
                var req = new HttpRequestMessage(HttpMethod.Post, relativeUrl);
                req.Content = JsonContent.Create(payload, options: JsonOptions);
                return req;
            }, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "Heartbeat successfully recorded by Master Host for agent '{AgentId}' with status code {StatusCode}.",
                    clientAgentId,
                    (int)response.StatusCode);
                return HeartbeatResultStatus.Success;
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogWarning(
                    "Master Host returned 404 Not Found for heartbeat on agent '{AgentId}'. Re-registration is required.",
                    clientAgentId);
                return HeartbeatResultStatus.NotFound;
            }

            if (response.StatusCode == HttpStatusCode.Gone)
            {
                _logger.LogError(
                    "Master Host has deactivated client agent '{AgentId}'. Heartbeat transmission will stop for this process.",
                    clientAgentId);
                return HeartbeatResultStatus.Deactivated;
            }
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                _logger.LogError("Master Host rejected Client Agent authentication for heartbeat (HTTP {StatusCode}).", (int)response.StatusCode);
                return HeartbeatResultStatus.AuthenticationFailed;
            }

            var responseBody = await SafeReadContentAsync(response.Content, cancellationToken);
            _logger.LogWarning(
                "Heartbeat submission returned non-success status {StatusCode} for agent '{AgentId}'. Details: {Details}",
                (int)response.StatusCode,
                clientAgentId,
                responseBody);

            return HeartbeatResultStatus.Failed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Heartbeat request canceled due to application shutdown.");
            throw;
        }
        catch (AgentAuthenticationException ex)
        {
            _logger.LogError("Client Agent authentication failed: {Message}", ex.Message);
            return HeartbeatResultStatus.AuthenticationFailed;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Transient error occurred while sending heartbeat for agent '{AgentId}': {Message}",
                clientAgentId,
                ex.Message);
            return HeartbeatResultStatus.Failed;
        }
    }

    public async Task<DeploymentPollResult> ClaimDeploymentAsync(string clientAgentId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendWithAuthAsync(() =>
            {
                var req = new HttpRequestMessage(HttpMethod.Post, "api/master-host/deployments/claim");
                req.Content = JsonContent.Create(new DeploymentClaimRequest { ClientAgentId = clientAgentId }, options: JsonOptions);
                return req;
            }, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NoContent) return new(DeploymentPollStatus.Empty);
            if (response.StatusCode == HttpStatusCode.Gone) return new(DeploymentPollStatus.Deactivated);
            if (response.StatusCode == HttpStatusCode.NotFound) return new(DeploymentPollStatus.Failed);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return new(DeploymentPollStatus.AuthenticationFailed);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Deployment poll returned HTTP {StatusCode} for agent {AgentId}.", (int)response.StatusCode, clientAgentId);
                return new(DeploymentPollStatus.Failed);
            }

            var assignment = await response.Content.ReadFromJsonAsync<DeploymentAssignment>(JsonOptions, cancellationToken);
            return assignment is null ? new(DeploymentPollStatus.Failed) : new(DeploymentPollStatus.Claimed, assignment);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (AgentAuthenticationException ex)
        {
            _logger.LogError("Client Agent authentication failed during deployment polling: {Message}", ex.Message);
            return new(DeploymentPollStatus.AuthenticationFailed);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Deployment poll failed for agent {AgentId}.", clientAgentId);
            return new(DeploymentPollStatus.Failed);
        }
    }

    public Task<bool> ReportDeploymentProgressAsync(long jobId, DeploymentProgressReport report, CancellationToken cancellationToken) =>
        SendDeploymentReportAsync($"api/master-host/deployments/{jobId}/progress", report, cancellationToken);

    public Task<bool> CompleteDeploymentAsync(long jobId, DeploymentCompletionReport report, CancellationToken cancellationToken) =>
        SendDeploymentReportAsync($"api/master-host/deployments/{jobId}/complete", report, cancellationToken);

    private async Task<bool> SendDeploymentReportAsync<T>(string route, T report, CancellationToken cancellationToken)
    {
        const int maxAttempts = 5;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var response = await SendWithAuthAsync(() =>
                {
                    var req = new HttpRequestMessage(HttpMethod.Post, route);
                    req.Content = JsonContent.Create(report, options: JsonOptions);
                    return req;
                }, cancellationToken);

                if (response.IsSuccessStatusCode) return true;
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    _logger.LogError("Master Host rejected authentication for deployment report to {Route}.", route);
                    return false;
                }
                var transient = (int)response.StatusCode >= 500 || response.StatusCode is HttpStatusCode.RequestTimeout or (HttpStatusCode)429;
                _logger.LogWarning("Deployment report to {Route} returned HTTP {StatusCode} on attempt {Attempt}/{MaxAttempts}.", route, (int)response.StatusCode, attempt, maxAttempts);
                if (!transient || attempt == maxAttempts) return false;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (AgentAuthenticationException ex)
            {
                _logger.LogError("Client Agent authentication failed while reporting deployment to {Route}: {Message}", route, ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Deployment report to {Route} failed on attempt {Attempt}/{MaxAttempts}.", route, attempt, maxAttempts);
                if (attempt == maxAttempts) return false;
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Min(1 << (attempt - 1), 8)), cancellationToken);
        }
        return false;
    }

    private async Task<HttpResponseMessage> SendWithAuthAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        var token = await _tokenProvider.GetTokenAsync(forceRefresh: false, cancellationToken);
        using var request = requestFactory();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("Master Host returned 401 Unauthorized. Invalidating cached token and retrying once.");
            response.Dispose();
            _tokenProvider.InvalidateToken();

            token = await _tokenProvider.GetTokenAsync(forceRefresh: true, cancellationToken);
            using var retryRequest = requestFactory();
            retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var retryResponse = await _httpClient.SendAsync(retryRequest, cancellationToken);
            if (retryResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                retryResponse.Dispose();
                throw new AgentAuthenticationException("Master Host rejected the refreshed Client Agent token.");
            }
            return retryResponse;
        }

        return response;
    }

    private static async Task<string> SafeReadContentAsync(HttpContent content, CancellationToken cancellationToken)
    {
        try
        {
            var text = await content.ReadAsStringAsync(cancellationToken);
            if (text.Length > 500)
            {
                return text.Substring(0, 500) + "... (truncated)";
            }
            return text;
        }
        catch
        {
            return "(unavailable)";
        }
    }

    private sealed class NullAgentCredentialProvider : IAgentCredentialProvider
    {
        public string GetSecret() => string.Empty;
    }

    private sealed class NullAgentTokenProvider : IAgentTokenProvider
    {
        public Task<string> GetTokenAsync(bool forceRefresh = false, CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);

        public void InvalidateToken() { }
    }
}
