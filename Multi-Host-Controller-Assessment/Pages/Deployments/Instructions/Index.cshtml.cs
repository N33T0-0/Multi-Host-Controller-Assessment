using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Multi_Host_Controller_Assessment.Deployments;

namespace Multi_Host_Controller_Assessment.Pages.Deployments.Instructions;

public sealed class IndexModel : PageModel
{
    private readonly IDeploymentService _service;
    public IndexModel(IDeploymentService service) => _service = service;

    public IReadOnlyList<DeploymentInstructionView> Instructions { get; private set; } = Array.Empty<DeploymentInstructionView>();
    [BindProperty] public InstructionForm Form { get; set; } = new();
    [TempData] public string? Notice { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken) => await LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) { await LoadAsync(cancellationToken); return Page(); }
        try
        {
            await _service.CreateInstructionAsync(Form.ToInput(), cancellationToken);
            Notice = "Deployment instruction created.";
            return RedirectToPage();
        }
        catch (DeploymentConflictException ex) { ModelState.AddModelError(string.Empty, ex.Message); }
        catch (DeploymentValidationException ex) { ModelState.AddModelError(string.Empty, ex.Message); }
        catch (DbUpdateException) { ModelState.AddModelError(string.Empty, "Could not save the instruction. Check for a duplicate package code and version."); }
        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostUpdateAsync(CancellationToken cancellationToken)
    {
        if (Form.Id <= 0) ModelState.AddModelError(nameof(Form.Id), "Instruction ID is required.");
        if (!ModelState.IsValid) { await LoadAsync(cancellationToken); return Page(); }
        try
        {
            var result = await _service.UpdateInstructionAsync(Form.Id, Form.ToInput(), cancellationToken);
            if (!result.Succeeded) ModelState.AddModelError(string.Empty, result.Error!);
            else { Notice = "Deployment instruction updated."; return RedirectToPage(); }
        }
        catch (DeploymentConflictException ex) { ModelState.AddModelError(string.Empty, ex.Message); }
        catch (DeploymentValidationException ex) { ModelState.AddModelError(string.Empty, ex.Message); }
        catch (DbUpdateException) { ModelState.AddModelError(string.Empty, "Could not save the instruction. Check for a duplicate package code and version."); }
        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostToggleAsync(long id, bool active, CancellationToken cancellationToken)
    {
        var result = await _service.SetInstructionActiveAsync(id, active, cancellationToken);
        Notice = result.Succeeded ? (active ? "Instruction activated." : "Instruction deactivated.") : result.Error;
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken) => Instructions = await _service.GetInstructionsAsync(cancellationToken: cancellationToken);

    public sealed class InstructionForm
    {
        public long Id { get; set; }
        [Required, StringLength(100)] public string PackageCode { get; set; } = string.Empty;
        [Required, StringLength(200)] public string PackageName { get; set; } = string.Empty;
        [Required, StringLength(30)] public string Version { get; set; } = string.Empty;
        [Required, StringLength(30)] public string Platform { get; set; } = string.Empty;
        [Required] public string InstallPlaybookYaml { get; set; } = string.Empty;
        [Required] public string VerificationPlaybookYaml { get; set; } = string.Empty;
        public string? RollbackPlaybookYaml { get; set; }
        public string? DefaultConfiguration { get; set; }

        public DeploymentInstructionInput ToInput() => new(PackageCode, PackageName, Version, Platform, InstallPlaybookYaml, VerificationPlaybookYaml, RollbackPlaybookYaml, DefaultConfiguration);
    }
}
