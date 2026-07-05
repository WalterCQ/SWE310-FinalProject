using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.DTOs.Admin;
using TaskFlow.Api.Helpers;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = nameof(GlobalRole.Admin))]
public class AdminController(AppDbContext dbContext) : ControllerBase
{
    [HttpGet("overview")]
    public async Task<ActionResult> GetOverview(CancellationToken cancellationToken)
    {
        var userCount = await dbContext.Users.AsNoTracking().CountAsync(cancellationToken);
        var adminCount = await dbContext.Users.AsNoTracking()
            .CountAsync(user => user.GlobalRole == GlobalRole.Admin, cancellationToken);
        var workspaceCount = await dbContext.Workspaces.AsNoTracking().CountAsync(cancellationToken);
        var projectCount = await dbContext.Projects.AsNoTracking().CountAsync(cancellationToken);
        var taskCount = await dbContext.TaskItems.AsNoTracking().CountAsync(cancellationToken);
        var channelCount = await dbContext.Channels.AsNoTracking().CountAsync(cancellationToken);
        var aiProviderCount = await dbContext.WorkspaceAiProviderCredentials.AsNoTracking().CountAsync(cancellationToken);

        var response = new AdminOverviewResponse
        {
            Metrics =
            [
                new AdminMetricResponse { Label = "Users", Value = userCount, HelpText = $"{adminCount} global admins" },
                new AdminMetricResponse { Label = "Workspaces", Value = workspaceCount, HelpText = "Relational workspace records" },
                new AdminMetricResponse { Label = "Projects", Value = projectCount, HelpText = "Workspace-linked projects" },
                new AdminMetricResponse { Label = "Tasks", Value = taskCount, HelpText = "Project task records" },
                new AdminMetricResponse { Label = "Channels", Value = channelCount, HelpText = "Team communication channels" },
                new AdminMetricResponse { Label = "AI providers", Value = aiProviderCount, HelpText = "Workspace-scoped LLM credentials" }
            ],
            SecurityEvidence =
            [
                new AdminSecurityEvidenceResponse
                {
                    Area = "API role restriction",
                    Evidence = "This endpoint requires a Bearer JWT with the Admin role claim."
                },
                new AdminSecurityEvidenceResponse
                {
                    Area = "Frontend route restriction",
                    Evidence = "The /admin route is hidden from standard users and blocked by ProtectedShell."
                },
                new AdminSecurityEvidenceResponse
                {
                    Area = "Permission model",
                    Evidence = "Workspace, project, channel, task, and AI operations also check relationship-based permissions per request."
                }
            ]
        };

        return Ok(ApiResponse.Ok(response));
    }
}
