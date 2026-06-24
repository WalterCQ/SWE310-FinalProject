using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.DTOs.Workspaces;
using TaskFlow.Api.Helpers;
using TaskFlow.Api.Services.Interfaces;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api/workspaces")]
// TODO: Add [Authorize] when the teammate's JWT module is connected.
public class WorkspacesController(IWorkspaceService workspaceService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> GetWorkspaces()
    {
        return this.ToActionResult(await workspaceService.GetWorkspacesAsync());
    }

    [HttpPost]
    public async Task<ActionResult> CreateWorkspace(CreateWorkspaceRequest request)
    {
        return this.ToActionResult(await workspaceService.CreateWorkspaceAsync(request));
    }

    [HttpGet("{workspaceId:guid}")]
    public async Task<ActionResult> GetWorkspace(Guid workspaceId)
    {
        return this.ToActionResult(await workspaceService.GetWorkspaceAsync(workspaceId));
    }

    [HttpPut("{workspaceId:guid}")]
    public async Task<ActionResult> UpdateWorkspace(Guid workspaceId, UpdateWorkspaceRequest request)
    {
        return this.ToActionResult(await workspaceService.UpdateWorkspaceAsync(workspaceId, request));
    }
}
