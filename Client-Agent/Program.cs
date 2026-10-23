using Client_Agent;
using Client_Agent.Configuration;
using Client_Agent.Services;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

// Configure options
builder.Services.Configure<ClientAgentOptions>(
    builder.Configuration.GetSection(ClientAgentOptions.SectionName));
builder.Services.PostConfigure<ClientAgentOptions>(options =>
{
    if (!builder.Environment.IsDevelopment()) options.RequireHttps = true;
});

// Configure services
builder.Services.AddSingleton<IAgentIdProvider, AgentIdProvider>();
builder.Services.AddSingleton<IAgentCredentialProvider, AgentCredentialProvider>();
builder.Services.AddSingleton<IAgentEnrollmentState, AgentEnrollmentState>();
builder.Services.AddSingleton<IResourceMetricsProvider, ResourceMetricsProvider>();
builder.Services.AddSingleton<IWslProcessRunner, WslProcessRunner>();
builder.Services.AddSingleton<IWslPrerequisiteChecker, WslPrerequisiteChecker>();
builder.Services.AddSingleton<IDeploymentStager, DeploymentStager>();

builder.Services.AddHttpClient<IAgentTokenProvider, AgentTokenProvider>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<ClientAgentOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(options.MasterHostBaseUrl))
    {
        client.BaseAddress = new Uri(options.MasterHostBaseUrl.TrimEnd('/') + "/");
    }
    client.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddHttpClient<IMasterHostClient, MasterHostClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<ClientAgentOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(options.MasterHostBaseUrl))
    {
        client.BaseAddress = new Uri(options.MasterHostBaseUrl.TrimEnd('/') + "/");
    }
    client.Timeout = TimeSpan.FromSeconds(15);
});

// Register worker hosted service
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<DeploymentWorker>();

var host = builder.Build();
host.Run();
