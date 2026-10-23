using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Multi_Host_Controller_Assessment.Dashboard;
using Multi_Host_Controller_Assessment.Services;
using Multi_Host_Controller_Assessment.Deployments;
using Multi_Host_Controller_Assessment.Data.Entities;

namespace Multi_Host_Controller_Assessment.Pages.Hosts;

/// <summary>
/// Host detail page at route "/Hosts/{clientAgentId}".
/// </summary>
public class DetailModel : PageModel
{
    private readonly IDashboardQueryService _queryService;
    private readonly IClientHostService _clientHostService;
    private readonly IDeploymentService _deploymentService;

    public DetailModel(
        IDashboardQueryService queryService,
        IClientHostService clientHostService,
        IDeploymentService deploymentService)
    {
        _queryService = queryService;
        _clientHostService = clientHostService;
        _deploymentService = deploymentService;
    }

    public HostDetailViewModel? HostDetail { get; private set; }

    public IReadOnlyList<DeploymentInstructionView> CompatibleInstructions { get; private set; } = Array.Empty<DeploymentInstructionView>();

    public IReadOnlyList<DeploymentJobView> DeploymentJobs { get; private set; } = Array.Empty<DeploymentJobView>();

    public bool HasOngoingDeployment => DeploymentJobs.Any(j => DeploymentJobStatuses.Ongoing.Contains(j.Status));

    [BindProperty]
    public long SelectedInstructionId { get; set; }

    [BindProperty]
    public string? Configuration { get; set; }

    [TempData]
    public string? DeploymentNotice { get; set; }

    public async Task<IActionResult> OnGetAsync(string clientAgentId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(clientAgentId))
        {
            return BadRequest();
        }

        await LoadPageAsync(clientAgentId, cancellationToken);

        if (HostDetail is null)
        {
            return NotFound();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostDeployAsync(string clientAgentId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(clientAgentId)) return BadRequest();
        try
        {
            var jobId = await _deploymentService.StartJobAsync(clientAgentId, SelectedInstructionId, Configuration, User.Identity?.Name, cancellationToken);
            DeploymentNotice = $"Deployment job {jobId} was started and is waiting for the agent.";
            return RedirectToPage(new { clientAgentId });
        }
        catch (DeploymentConflictException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
        }
        catch (DeploymentValidationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
        }
        catch (DeploymentLogStorageUnavailableException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
        }

        await LoadPageAsync(clientAgentId, cancellationToken);
        if (HostDetail is null) return NotFound();
        return Page();
    }

    public async Task<IActionResult> OnPostDeactivateAsync(string clientAgentId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(clientAgentId))
        {
            return BadRequest();
        }

        var success = await _clientHostService.DeactivateHostAsync(clientAgentId, cancellationToken);

        if (!success)
        {
            return NotFound();
        }

        return RedirectToPage(new { clientAgentId });
    }

    private async Task LoadPageAsync(string clientAgentId, CancellationToken cancellationToken)
    {
        HostDetail = await _queryService.GetHostDetailAsync(clientAgentId, cancellationToken);
        CompatibleInstructions = await _deploymentService.GetCompatibleInstructionsAsync(clientAgentId, cancellationToken);
        DeploymentJobs = await _deploymentService.GetHostJobsAsync(clientAgentId, cancellationToken);
    }
}
