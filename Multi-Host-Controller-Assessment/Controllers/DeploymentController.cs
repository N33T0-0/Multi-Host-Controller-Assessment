using Microsoft.AspNetCore.Mvc;
using Multi_Host_Controller_Assessment.Deployments;
using Multi_Host_Controller_Assessment.Data.Entities;

namespace Multi_Host_Controller_Assessment.Controllers;

[ApiController]
[Route("api/master-host/deployments")]
[Produces("application/json")]
public sealed class DeploymentController : ControllerBase
{
    private readonly IDeploymentService _deployments;

    public DeploymentController(IDeploymentService deployments) => _deployments = deployments;

    [HttpPost("claim")]
    [ProducesResponseType(typeof(DeploymentClaimView), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone)]
    public async Task<IActionResult> Claim([FromBody] ClaimDeploymentRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ClientAgentId) || request.ClientAgentId.Trim().Length > 64)
        {
            ModelState.AddModelError(nameof(request.ClientAgentId), "ClientAgentId is required and cannot exceed 64 characters.");
            return ValidationProblem();
        }

        DeploymentClaimResult result;
        try { result = await _deployments.ClaimNextJobAsync(request.ClientAgentId.Trim(), cancellationToken); }
        catch (DeploymentLogStorageUnavailableException ex) { return StatusCode(503, new ProblemDetails { Status = 503, Title = "Deployment logging unavailable", Detail = ex.Message }); }
        return result.State switch
        {
            "NotFound" => NotFound(new ProblemDetails { Status = 404, Title = "Client Agent Not Found" }),
            "Deactivated" => StatusCode(StatusCodes.Status410Gone, new ProblemDetails { Status = 410, Title = "Client Agent Deactivated" }),
            "Empty" => NoContent(),
            _ => Ok(result.Job)
        };
    }

    [HttpPost("{jobId:long}/progress")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Progress(long jobId, [FromBody] DeploymentProgressRequest request, CancellationToken cancellationToken)
    {
        DeploymentOperationResult result;
        try { result = await _deployments.ReportProgressAsync(jobId, request, cancellationToken); }
        catch (DeploymentLogStorageUnavailableException ex) { return StatusCode(503, new ProblemDetails { Status = 503, Title = "Deployment logging unavailable", Detail = ex.Message }); }
        if (result.Succeeded) return NoContent();
        if (result.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true) return NotFound(Problem(result.Error, 404));
        if (result.Error?.Contains("Invalid deployment transition", StringComparison.OrdinalIgnoreCase) == true ||
            result.Error?.Contains("already complete", StringComparison.OrdinalIgnoreCase) == true) return Conflict(Problem(result.Error, 409));
        return BadRequest(Problem(result.Error ?? "Invalid deployment progress.", 400));
    }

    [HttpPost("{jobId:long}/complete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Complete(long jobId, [FromBody] DeploymentCompletionRequest request, CancellationToken cancellationToken)
    {
        DeploymentOperationResult result;
        try { result = await _deployments.CompleteJobAsync(jobId, request, cancellationToken); }
        catch (DeploymentLogStorageUnavailableException ex) { return StatusCode(503, new ProblemDetails { Status = 503, Title = "Deployment logging unavailable", Detail = ex.Message }); }
        if (result.Succeeded) return NoContent();
        if (result.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true) return NotFound(Problem(result.Error, 404));
        if (result.Error?.Contains("Invalid deployment transition", StringComparison.OrdinalIgnoreCase) == true ||
            result.Error?.Contains("already complete", StringComparison.OrdinalIgnoreCase) == true) return Conflict(Problem(result.Error, 409));
        return BadRequest(Problem(result.Error ?? "Invalid deployment completion.", 400));
    }

    private static ProblemDetails Problem(string detail, int status) => new() { Status = status, Title = "Deployment request rejected", Detail = detail };
}
