using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using TaskFlow.Api.DTOs.Projects;
using TaskFlow.Api.Helpers;
using TaskFlow.Api.Services.Interfaces;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class ProjectsController(IProjectService projectService) : ControllerBase
{
    [HttpGet("workspaces/{workspaceId:guid}/projects")]
    public async Task<ActionResult> GetWorkspaceProjects(Guid workspaceId)
    {
        return this.ToActionResult(await projectService.GetWorkspaceProjectsAsync(workspaceId));
    }

    [HttpPost("workspaces/{workspaceId:guid}/projects")]
    public async Task<ActionResult> CreateProject(Guid workspaceId, CreateProjectRequest request)
    {
        return this.ToActionResult(await projectService.CreateProjectAsync(workspaceId, request));
    }

    [HttpGet("projects/{projectId:guid}")]
    public async Task<ActionResult> GetProject(Guid projectId)
    {
        return this.ToActionResult(await projectService.GetProjectAsync(projectId));
    }

    [HttpPut("projects/{projectId:guid}")]
    public async Task<ActionResult> UpdateProject(Guid projectId, UpdateProjectRequest request)
    {
        return this.ToActionResult(await projectService.UpdateProjectAsync(projectId, request));
    }

    [HttpDelete("projects/{projectId:guid}")]
    public async Task<ActionResult> DeleteProject(Guid projectId)
    {
        return this.ToActionResult(await projectService.DeleteProjectAsync(projectId));
    }

    [HttpGet("projects/{projectId:guid}/members")]
    public async Task<ActionResult> GetProjectMembers(Guid projectId)
    {
        return this.ToActionResult(await projectService.GetProjectMembersAsync(projectId));
    }

    [HttpPost("projects/{projectId:guid}/members")]
    public async Task<ActionResult> AddProjectMember(Guid projectId, AddProjectMemberRequest request)
    {
        return this.ToActionResult(await projectService.AddProjectMemberAsync(projectId, request));
    }

    [HttpPut("projects/{projectId:guid}/members/{userId:guid}")]
    public async Task<ActionResult> UpdateProjectMemberRole(Guid projectId, Guid userId, UpdateProjectMemberRoleRequest request)
    {
        return this.ToActionResult(await projectService.UpdateProjectMemberRoleAsync(projectId, userId, request));
    }

    [HttpDelete("projects/{projectId:guid}/members/{userId:guid}")]
    public async Task<ActionResult> RemoveProjectMember(Guid projectId, Guid userId)
    {
        return this.ToActionResult(await projectService.RemoveProjectMemberAsync(projectId, userId));
    }
}
