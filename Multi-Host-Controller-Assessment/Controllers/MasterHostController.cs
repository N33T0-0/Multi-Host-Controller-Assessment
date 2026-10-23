using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Multi_Host_Controller_Assessment.Models.Requests;
using Multi_Host_Controller_Assessment.Models.Responses;
using Multi_Host_Controller_Assessment.Services;

namespace Multi_Host_Controller_Assessment.Controllers;

/// <summary>
/// Controller for client host agent registration, heartbeat management, and deactivation.
/// </summary>
[ApiController]
[Route("api/master-host")]
[Produces("application/json")]
public class MasterHostController : ControllerBase
{
    private readonly IClientHostService _clientHostService;
    private readonly ILogger<MasterHostController> _logger;
    private readonly IHostEnvironment _environment;

    public MasterHostController(IClientHostService clientHostService, ILogger<MasterHostController> logger, IHostEnvironment environment)
    {
        _clientHostService = clientHostService;
        _logger = logger;
        _environment = environment;
    }

    /// <summary>
    /// Registers a client host agent or returns the existing active record without modifications.
    /// Returns 410 Gone if the agent has been soft-deactivated.
    /// </summary>
    /// <param name="request">Agent registration details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Registration confirmation response.</returns>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("agent-auth")]
    [ProducesResponseType(typeof(RegisterClientAgentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(RegisterClientAgentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterClientAgentRequest request,
        CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment() && !Request.IsHttps)
        {
            return StatusCode(StatusCodes.Status400BadRequest, new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "HTTPS required",
                Detail = "Client agent enrollment must use HTTPS outside Development."
            });
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = await _clientHostService.RegisterHostAsync(request, cancellationToken);

        if (result.Status == RegisterStatus.CredentialRejected)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Registration Rejected",
                Detail = "Client credentials are invalid or cannot be enrolled."
            });
        }

        if (result.Status == RegisterStatus.Deactivated)
        {
            return StatusCode(StatusCodes.Status410Gone, new ProblemDetails
            {
                Status = StatusCodes.Status410Gone,
                Title = "Client Agent Deactivated",
                Detail = $"Client agent '{request.ClientAgentId}' has been deactivated and cannot register."
            });
        }

        if (result.Host == null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Status = 500,
                Title = "Registration Failed",
                Detail = "An unexpected error occurred during registration."
            });
        }

        var response = new RegisterClientAgentResponse
        {
            ClientAgentId = result.Host.ClientAgentId,
            ClientAgentName = result.Host.ClientAgentName,
            ClientAgentOS = result.Host.ClientAgentOS,
            ClientAgentVersion = result.Host.ClientAgentVersion,
            Status = result.Host.Status,
            ClientRegistrationDateTime = result.Host.ClientRegistrationDateTime,
            IsNewRegistration = result.IsNewRegistration,
            Message = result.IsNewRegistration
                ? "Client host registered successfully."
                : "Client host is already registered (existing record unchanged)."
        };

        if (result.IsNewRegistration)
        {
            return CreatedAtAction(
                nameof(Heartbeat),
                new { id = response.ClientAgentId },
                response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Submits a heartbeat and optional resource metrics for a registered client host agent.
    /// Returns 410 Gone without writing logs if the agent has been deactivated.
    /// </summary>
    /// <param name="id">ClientAgentID external identifier.</param>
    /// <param name="request">Optional heartbeat request containing metrics.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Heartbeat confirmation response.</returns>
    [Authorize(Policy = "ClientAgentPolicy")]
    [HttpPost("heartbeat/{id}")]
    [ProducesResponseType(typeof(HeartbeatResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Heartbeat(
        [FromRoute] string id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] HeartbeatRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Trim().Length > 64)
        {
            ModelState.AddModelError(nameof(id), "Route identifier 'id' must be non-empty and not exceed 64 characters.");
            return ValidationProblem(ModelState);
        }

        var callerAgentId = User.FindFirst("client_agent_id")?.Value
                            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                            ?? User.FindFirst("sub")?.Value;

        if (!string.Equals(callerAgentId, id.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Forbidden",
                Detail = $"Token agent identity '{callerAgentId}' does not match route '{id}'."
            });
        }

        try
        {
            var result = await _clientHostService.RecordHeartbeatAsync(
                id,
                request?.ResourceUsageMetrics,
                cancellationToken);

            if (result.Status == HeartbeatStatus.NotFound)
            {
                return NotFound(new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = "Client Agent Not Found",
                    Detail = $"Client agent with ID '{id}' is not registered."
                });
            }

            if (result.Status == HeartbeatStatus.Deactivated)
            {
                return StatusCode(StatusCodes.Status410Gone, new ProblemDetails
                {
                    Status = StatusCodes.Status410Gone,
                    Title = "Client Agent Deactivated",
                    Detail = $"Client agent with ID '{id}' has been deactivated and cannot submit heartbeats."
                });
            }

            return Ok(new HeartbeatResponse
            {
                ClientAgentId = result.ClientAgentId,
                LoggedDateTime = result.LoggedDateTime,
                Status = result.HostStatus,
                Message = "Heartbeat recorded successfully."
            });
        }
        catch (ArgumentException ex) when (ex.ParamName == "resourceUsageMetrics")
        {
            ModelState.AddModelError("ResourceUsageMetrics", ex.Message);
            return ValidationProblem(ModelState);
        }
    }

    /// <summary>
    /// Soft-deactivates a registered client host agent by its external ClientAgentID.
    /// </summary>
    /// <param name="id">ClientAgentID external identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>204 No Content on success; 404 Not Found if unknown ID.</returns>
    [Authorize(Policy = "AdminPolicy")]
    [HttpPost("deactivate/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Deactivate(
        [FromRoute] string id,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Trim().Length > 64)
        {
            ModelState.AddModelError(nameof(id), "Route identifier 'id' must be non-empty and not exceed 64 characters.");
            return ValidationProblem(ModelState);
        }

        var success = await _clientHostService.DeactivateHostAsync(id, cancellationToken);

        if (!success)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Client Agent Not Found",
                Detail = $"Client agent with ID '{id}' is not registered."
            });
        }

        return NoContent();
    }
}
