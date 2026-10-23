using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Multi_Host_Controller_Assessment.Data;

namespace Multi_Host_Controller_Assessment.Services;

public sealed class ClientAgentActiveRequirement : IAuthorizationRequirement;

/// <summary>Checks current database state on every agent API request so revocation takes effect immediately.</summary>
public sealed class ClientAgentActiveHandler : AuthorizationHandler<ClientAgentActiveRequirement>
{
    private readonly MasterHostDbContext _db;

    public ClientAgentActiveHandler(MasterHostDbContext db) => _db = db;

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ClientAgentActiveRequirement requirement)
    {
        var agentId = context.User.FindFirst("client_agent_id")?.Value
            ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.User.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(agentId)) return;

        var now = DateTime.UtcNow;
        var active = await (from host in _db.ClientHosts.AsNoTracking()
                            join credential in _db.ClientAgentCredentials.AsNoTracking()
                                on host.Id equals credential.ClientHostId
                            where host.ClientAgentId == agentId
                                && !host.IsDeleted
                                && credential.RevokedAt == null
                                && (credential.ExpiresAt == null || credential.ExpiresAt > now)
                            select credential.Id).AnyAsync();
        if (active) context.Succeed(requirement);
    }
}
