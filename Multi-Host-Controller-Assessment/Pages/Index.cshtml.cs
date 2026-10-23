using Microsoft.AspNetCore.Mvc.RazorPages;
using Multi_Host_Controller_Assessment.Dashboard;

namespace Multi_Host_Controller_Assessment.Pages;

/// <summary>
/// Hosts list page at route "/".
/// </summary>
public class IndexModel : PageModel
{
    private readonly IDashboardQueryService _queryService;

    public IndexModel(IDashboardQueryService queryService)
    {
        _queryService = queryService;
    }

    public IReadOnlyList<HostSummaryViewModel> Hosts { get; private set; } =
        Array.Empty<HostSummaryViewModel>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Hosts = await _queryService.GetHostSummariesAsync(cancellationToken);
    }
}

