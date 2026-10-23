using System.Reflection;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Multi_Host_Controller_Assessment.Configuration;
using Multi_Host_Controller_Assessment.Dashboard;
using Multi_Host_Controller_Assessment.Data;
using Multi_Host_Controller_Assessment.Deployments;
using Multi_Host_Controller_Assessment.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddProblemDetails();

// Configure Razor Pages for the dashboard
builder.Services.AddRazorPages(options =>
    options.Conventions.ConfigureFilter(new AutoValidateAntiforgeryTokenAttribute()));

// Bind and validate dashboard configuration
builder.Services.Configure<DashboardOptions>(
    builder.Configuration.GetSection(DashboardOptions.SectionName));

var dashboardOptions = builder.Configuration
    .GetSection(DashboardOptions.SectionName)
    .Get<DashboardOptions>() ?? new DashboardOptions();
dashboardOptions.Validate();

// Bind and validate JWT configuration
builder.Services.Configure<JwtOptions>(
    builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddOptions<JwtOptions>()
    .Validate(options =>
    {
        try { options.Validate(requireKeyFile: !builder.Environment.IsDevelopment()); return true; }
        catch { return false; }
    }, "JWT configuration is invalid. Configure issuer, audience, lifetime, and a protected RSA private key file.")
    .ValidateOnStart();

// Configure persistence
var connectionString = builder.Configuration.GetConnectionString("LocalDatabase");
builder.Services.AddDbContext<MasterHostDbContext>(options =>
{
    if (!string.IsNullOrEmpty(connectionString))
    {
        options.UseSqlServer(connectionString);
    }
});

// Configure application services
builder.Services.AddScoped<IClientHostService, ClientHostService>();
builder.Services.AddScoped<IDashboardQueryService, DashboardQueryService>();
builder.Services.AddScoped<IDeploymentService, DeploymentService>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IAuthorizationHandler, ClientAgentActiveHandler>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("agent-auth", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

// Configure JWT Authentication and Authorization
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer();

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IJwtTokenService, IOptions<JwtOptions>>((options, jwtService, jwtOpt) =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOpt.Value.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOpt.Value.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = jwtService.GetValidationKey(),
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 },
            ClockSkew = TimeSpan.FromSeconds(30),
            RoleClaimType = ClaimTypes.Role
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ClientAgentPolicy", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole("ClientAgent");
        policy.AddRequirements(new ClientAgentActiveRequirement());
    });
    options.AddPolicy("AdminPolicy", policy =>
    {
        policy.RequireRole("Admin");
    });
});

// Configure Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFilename);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter a valid JWT Bearer token."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Force key loading during startup so a missing or invalid signing key fails fast.
_ = app.Services.GetRequiredService<IJwtTokenService>();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRateLimiter();

// Order middleware: UseAuthentication before UseAuthorization
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapRazorPages();

app.Run();

// Partial declaration for WebApplicationFactory in integration tests
public partial class Program { }
