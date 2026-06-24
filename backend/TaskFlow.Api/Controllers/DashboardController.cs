using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.Helpers;
using TaskFlow.Api.Services.Interfaces;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api")]
// TODO: Add [Authorize] when the teammate's JWT module is connected.
public class DashboardController(IDashboardService dashboardService) : ControllerBase
{
    [HttpGet("workspaces/{workspaceId:guid}/dashboard")]
    public async Task<ActionResult> GetWorkspaceDashboard(Guid workspaceId)
    {
        return this.ToActionResult(await dashboardService.GetWorkspaceDashboardAsync(workspaceId));
    }

    [HttpGet("projects/{projectId:guid}/dashboard")]
    public async Task<ActionResult> GetProjectDashboard(Guid projectId)
    {
        return this.ToActionResult(await dashboardService.GetProjectDashboardAsync(projectId));
    }
}
