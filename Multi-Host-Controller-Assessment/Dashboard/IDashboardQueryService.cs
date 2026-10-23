namespace Multi_Host_Controller_Assessment.Dashboard;

/// <summary>
/// Read-only query service for the dashboard, returning view models without
/// duplicating EF query logic in page models.
/// </summary>
public interface IDashboardQueryService
{
    /// <summary>
    /// Returns all registered hosts with latest heartbeat info and derived
    /// online/offline status. Uses a single efficient join; no N+1 queries.
    /// </summary>
    Task<IReadOnlyList<HostSummaryViewModel>> GetHostSummariesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the full detail view model for a single host identified by
    /// its ClientAgentID, or null if not found.
    /// </summary>
    Task<HostDetailViewModel?> GetHostDetailAsync(string clientAgentId, CancellationToken cancellationToken = default);
}

