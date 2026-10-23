using System.Runtime.InteropServices;
using Client_Agent.Configuration;
using Client_Agent.Models;
using Client_Agent.Services;
using Microsoft.Extensions.Options;

namespace Client_Agent;

/// <summary>
/// Hosted background service managing client agent registration and periodic heartbeat telemetry.
/// </summary>
public class Worker : BackgroundService
{
    private readonly ClientAgentOptions _options;
    private readonly IAgentIdProvider _agentIdProvider;
    private readonly IMasterHostClient _masterHostClient;
    private readonly IResourceMetricsProvider _metricsProvider;
    private readonly ILogger<Worker> _logger;

    public Worker(
        IOptions<ClientAgentOptions> options,
        IAgentIdProvider agentIdProvider,
        IMasterHostClient masterHostClient,
        IResourceMetricsProvider metricsProvider,
        ILogger<Worker> logger)
    {
        _options = options.Value;
        _agentIdProvider = agentIdProvider;
        _masterHostClient = masterHostClient;
        _metricsProvider = metricsProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Client Agent worker service starting...");

        // 1. Validate configuration at startup
        _options.Validate();

        // 2. Retrieve or atomically generate persistent UUID
        var agentId = _agentIdProvider.GetOrCreateAgentId();

        // 3. Resolve and constrain platform OS description (API limit: 50 characters)
        var osDescription = RuntimeInformation.OSDescription;
        if (osDescription.Length > 50)
        {
            osDescription = osDescription.Substring(0, 50);
        }

        var registrationRequest = new RegisterAgentRequest
        {
            ClientAgentId = agentId,
            ClientAgentName = _options.Name.Trim(),
            ClientAgentOS = osDescription,
            ClientAgentVersion = _options.Version.Trim()
        };

        _logger.LogInformation(
            "Agent identity configured: ID='{AgentId}', Name='{Name}', OS='{OS}', Version='{Version}'. Target Master Host='{Url}'.",
            agentId,
            registrationRequest.ClientAgentName,
            registrationRequest.ClientAgentOS,
            registrationRequest.ClientAgentVersion,
            _options.MasterHostBaseUrl);

        var isRegistered = false;
        var retryDelay = TimeSpan.FromSeconds(5);
        var maxRetryDelay = TimeSpan.FromSeconds(30);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Registration phase
                if (!isRegistered)
                {
                    _logger.LogInformation("Attempting registration with Master Host for agent '{AgentId}'...", agentId);
                    var registrationStatus = await _masterHostClient.RegisterAsync(registrationRequest, stoppingToken);

                    if (registrationStatus == RegistrationResultStatus.Deactivated)
                    {
                        _logger.LogError(
                            "Client Agent '{AgentId}' is deactivated by the Master Host. No further requests will be sent; operator intervention is required.",
                            agentId);
                        return;
                    }

                    if (registrationStatus == RegistrationResultStatus.Success)
                    {
                        isRegistered = true;
                        retryDelay = TimeSpan.FromSeconds(5); // Reset backoff on success
                        _logger.LogInformation("Agent '{AgentId}' registration accepted by Master Host.", agentId);

                        // Immediate heartbeat after successful registration
                        var initialMetrics = _metricsProvider.GetMetrics();
                        var initialHbStatus = await _masterHostClient.SendHeartbeatAsync(agentId, initialMetrics, stoppingToken);

                        if (initialHbStatus == HeartbeatResultStatus.NotFound)
                        {
                            _logger.LogWarning("Master Host reported 404 immediately after registration. Retrying registration...");
                            isRegistered = false;
                            continue;
                        }

                        if (initialHbStatus == HeartbeatResultStatus.Deactivated)
                        {
                            _logger.LogError(
                                "Client Agent '{AgentId}' was deactivated while starting. No further requests will be sent; operator intervention is required.",
                                agentId);
                            return;
                        }
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Registration failed for agent '{AgentId}'. Retrying in {Seconds}s with bounded backoff...",
                            agentId,
                            retryDelay.TotalSeconds);

                        await Task.Delay(retryDelay, stoppingToken);
                        retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, maxRetryDelay.TotalSeconds));
                        continue;
                    }
                }

                // Wait interval measured from previous attempt (no overlapping sends)
                var interval = TimeSpan.FromSeconds(_options.HeartbeatIntervalSeconds);
                _logger.LogDebug("Waiting {Seconds}s until next heartbeat cycle...", interval.TotalSeconds);
                await Task.Delay(interval, stoppingToken);

                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                // Sample metrics and send periodic heartbeat
                var metrics = _metricsProvider.GetMetrics();
                var hbStatus = await _masterHostClient.SendHeartbeatAsync(agentId, metrics, stoppingToken);

                if (hbStatus == HeartbeatResultStatus.NotFound)
                {
                    _logger.LogWarning(
                        "Master Host returned 404 Not Found for agent '{AgentId}'. Registration may have been lost. Initiating re-registration.",
                        agentId);
                    isRegistered = false;
                }
                else if (hbStatus == HeartbeatResultStatus.Deactivated)
                {
                    _logger.LogError(
                        "Client Agent '{AgentId}' was deactivated by the Master Host. No further requests will be sent; operator intervention is required.",
                        agentId);
                    return;
                }
                else if (hbStatus == HeartbeatResultStatus.Failed)
                {
                    _logger.LogWarning(
                        "Periodic heartbeat failed for agent '{AgentId}'. Will retry on subsequent cycle.",
                        agentId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Client Agent worker received shutdown signal and stopped gracefully.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fatal error in Client Agent worker: {Message}", ex.Message);
            throw;
        }
    }
}
