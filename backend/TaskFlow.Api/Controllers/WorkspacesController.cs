using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using TaskFlow.Api.DTOs.Workspaces;
using TaskFlow.Api.Helpers;
using TaskFlow.Api.Services.Interfaces;


namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api/workspaces")]
[Authorize]
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

    [HttpGet("{workspaceId:guid}/members")]
    public async Task<ActionResult> GetWorkspaceMembers(Guid workspaceId)
    {
        return this.ToActionResult(await workspaceService.GetWorkspaceMembersAsync(workspaceId));
    }

    [HttpPost("{workspaceId:guid}/members")]
    public async Task<ActionResult> AddWorkspaceMember(Guid workspaceId, AddWorkspaceMemberRequest request)
    {
        return this.ToActionResult(await workspaceService.AddWorkspaceMemberAsync(workspaceId, request));
    }

    [HttpPut("{workspaceId:guid}/members/{userId:guid}")]
    public async Task<ActionResult> UpdateWorkspaceMemberRole(Guid workspaceId, Guid userId, UpdateWorkspaceMemberRoleRequest request)
    {
        return this.ToActionResult(await workspaceService.UpdateWorkspaceMemberRoleAsync(workspaceId, userId, request));
    }

    [HttpDelete("{workspaceId:guid}/members/{userId:guid}")]
    public async Task<ActionResult> RemoveWorkspaceMember(Guid workspaceId, Guid userId)
    {
        return this.ToActionResult(await workspaceService.RemoveWorkspaceMemberAsync(workspaceId, userId));
    }
}
