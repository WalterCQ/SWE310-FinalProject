using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.DTOs.Projects;
using TaskFlow.Api.Helpers;
using TaskFlow.Api.Services.Interfaces;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api")]
// TODO: Add [Authorize] when the teammate's JWT module is connected.
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
}
