using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Multi_Host_Controller_Assessment.Configuration;
using Multi_Host_Controller_Assessment.Data;
using Multi_Host_Controller_Assessment.Models.Requests;
using Multi_Host_Controller_Assessment.Models.Responses;
using Multi_Host_Controller_Assessment.Services;

namespace Multi_Host_Controller_Assessment.Controllers;

/// <summary>
/// Handles authentication and token acquisition for registered client agents.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/master-host/auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly MasterHostDbContext _dbContext;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly JwtOptions _jwtOptions;
    private readonly ILogger<AuthController> _logger;
    private readonly IHostEnvironment _environment;

    public AuthController(
        MasterHostDbContext dbContext,
        IJwtTokenService jwtTokenService,
        IOptions<JwtOptions> jwtOptions,
        ILogger<AuthController> logger,
        IHostEnvironment environment)
    {
        _dbContext = dbContext;
        _jwtTokenService = jwtTokenService;
        _jwtOptions = jwtOptions.Value;
        _logger = logger;
        _environment = environment;
    }

    /// <summary>
    /// Issues a short-lived JWT access token for an active registered client agent using its pre-provisioned secret.
    /// </summary>
    /// <param name="request">Agent credentials containing ClientAgentId and ClientSecret.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>JWT access token response.</returns>
    [HttpPost("token")]
    [EnableRateLimiting("agent-auth")]
    [ProducesResponseType(typeof(ClientAgentTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> AcquireToken(
        [FromBody] ClientAgentTokenRequest request,
        CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment() && !Request.IsHttps)
        {
            return StatusCode(StatusCodes.Status400BadRequest, new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "HTTPS required",
                Detail = "Client agent token requests must use HTTPS outside Development."
            });
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var trimmedId = request.ClientAgentId.Trim();
        byte[] secretHash;
        try
        {
            secretHash = ClientAgentCredentialHelper.ComputeSecretHash(request.ClientSecret);
        }
        catch
        {
            _logger.LogWarning("Token request rejected: Secret processing failed.");
            return GenericUnauthorized();
        }

        var host = await _dbContext.ClientHosts
            .FirstOrDefaultAsync(h => h.ClientAgentId == trimmedId, cancellationToken);

        if (host == null || host.IsDeleted)
        {
            _logger.LogWarning("Token request rejected for agent '{AgentId}': Host not found or deactivated.", trimmedId);
            return GenericUnauthorized();
        }

        var credentials = await _dbContext.ClientAgentCredentials
            .Where(c => c.ClientHostId == host.Id)
            .Take(2)
            .ToListAsync(cancellationToken);

        if (credentials.Count != 1)
        {
            _logger.LogWarning("Token request rejected for agent '{AgentId}': Credential binding is missing or ambiguous.", trimmedId);
            return GenericUnauthorized();
        }

        var credential = credentials[0];

        if (credential.RevokedAt != null || (credential.ExpiresAt != null && credential.ExpiresAt <= DateTime.UtcNow))
        {
            _logger.LogWarning("Token request rejected for agent '{AgentId}': Credential revoked or expired.", trimmedId);
            return GenericUnauthorized();
        }

        if (!ClientAgentCredentialHelper.FixedTimeEquals(credential.SecretHash, secretHash))
        {
            _logger.LogWarning("Token request rejected for agent '{AgentId}': Secret mismatch.", trimmedId);
            return GenericUnauthorized();
        }

        // Update LastUsedAt timestamp
        credential.LastUsedAt = DateTime.UtcNow;
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update LastUsedAt timestamp for agent '{AgentId}'.", trimmedId);
        }

        var token = _jwtTokenService.GenerateToken(host.ClientAgentId);

        _logger.LogInformation("Issued JWT access token for client agent '{AgentId}'.", trimmedId);

        return Ok(new ClientAgentTokenResponse
        {
            AccessToken = token,
            TokenType = "Bearer",
            ExpiresIn = _jwtOptions.TokenLifetimeSeconds
        });
    }

    private UnauthorizedObjectResult GenericUnauthorized() =>
        Unauthorized(new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "Unauthorized",
            Detail = "Invalid client credentials."
        });
}

