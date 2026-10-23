using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Client_Agent.Models;

namespace Client_Agent.Services;

/// <summary>
/// HTTP client implementation for communicating with the Master Host API.
/// </summary>
public class MasterHostClient : IMasterHostClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ILogger<MasterHostClient> _logger;

    public MasterHostClient(HttpClient httpClient, ILogger<MasterHostClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<RegistrationResultStatus> RegisterAsync(RegisterAgentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
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
            var response = await _httpClient.PostAsJsonAsync(
                relativeUrl,
                payload,
                JsonOptions,
                cancellationToken);

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
            using var response = await _httpClient.PostAsJsonAsync("api/master-host/deployments/claim",
                new DeploymentClaimRequest { ClientAgentId = clientAgentId }, JsonOptions, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NoContent) return new(DeploymentPollStatus.Empty);
            if (response.StatusCode == HttpStatusCode.Gone) return new(DeploymentPollStatus.Deactivated);
            if (response.StatusCode == HttpStatusCode.NotFound) return new(DeploymentPollStatus.Failed);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Deployment poll returned HTTP {StatusCode} for agent {AgentId}.", (int)response.StatusCode, clientAgentId);
                return new(DeploymentPollStatus.Failed);
            }

            var assignment = await response.Content.ReadFromJsonAsync<DeploymentAssignment>(JsonOptions, cancellationToken);
            return assignment is null ? new(DeploymentPollStatus.Failed) : new(DeploymentPollStatus.Claimed, assignment);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
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
                using var response = await _httpClient.PostAsJsonAsync(route, report, JsonOptions, cancellationToken);
                if (response.IsSuccessStatusCode) return true;
                var transient = (int)response.StatusCode >= 500 || response.StatusCode is HttpStatusCode.RequestTimeout or (HttpStatusCode)429;
                _logger.LogWarning("Deployment report to {Route} returned HTTP {StatusCode} on attempt {Attempt}/{MaxAttempts}.", route, (int)response.StatusCode, attempt, maxAttempts);
                if (!transient || attempt == maxAttempts) return false;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Deployment report to {Route} failed on attempt {Attempt}/{MaxAttempts}.", route, attempt, maxAttempts);
                if (attempt == maxAttempts) return false;
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Min(1 << (attempt - 1), 8)), cancellationToken);
        }
        return false;
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
}

