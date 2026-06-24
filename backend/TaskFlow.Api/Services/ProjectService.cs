using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.DTOs.Projects;
using TaskFlow.Api.Helpers;
using TaskFlow.Api.Models;
using TaskFlow.Api.Services.Interfaces;

namespace TaskFlow.Api.Services;

public class ProjectService(
    AppDbContext dbContext,
    ICurrentUserService currentUser,
    IPermissionService permissionService) : IProjectService
{
    public async Task<ApiResponse<IEnumerable<ProjectResponse>>> GetWorkspaceProjectsAsync(Guid workspaceId)
    {
        var userId = currentUser.GetUserId();
        if (!await permissionService.CanAccessWorkspace(userId, workspaceId))
        {
            return ApiResponse.Fail<IEnumerable<ProjectResponse>>("Workspace not found or access denied.", StatusCodes.Status404NotFound);
        }

        var projects = await ProjectQuery()
            .Where(project => project.WorkspaceId == workspaceId)
            .OrderBy(project => project.Name)
            .ToListAsync();

        return ApiResponse.Ok(projects.Select(project => project.ToResponse()));
    }

    public async Task<ApiResponse<ProjectResponse>> CreateProjectAsync(Guid workspaceId, CreateProjectRequest request)
    {
        var userId = currentUser.GetUserId();
        if (!await permissionService.CanManageWorkspace(userId, workspaceId))
        {
            return ApiResponse.Fail<ProjectResponse>("You do not have permission to create projects in this workspace.", StatusCodes.Status403Forbidden);
        }

        var project = new Project
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            DeadlineUtc = request.DeadlineUtc,
            Status = ProjectStatus.Active,
            CreatedByUserId = userId
        };

        project.Members.Add(new ProjectMember
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            UserId = userId,
            RoleInProject = ProjectRole.ProjectManager
        });

        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync();

        return ApiResponse.Created(project.ToResponse(), "Project created.");
    }

    public async Task<ApiResponse<ProjectResponse>> GetProjectAsync(Guid projectId)
    {
        var userId = currentUser.GetUserId();
        if (!await permissionService.CanAccessProject(userId, projectId))
        {
            return ApiResponse.Fail<ProjectResponse>("Project not found or access denied.", StatusCodes.Status404NotFound);
        }

        var project = await ProjectQuery().FirstOrDefaultAsync(item => item.Id == projectId);
        return project is null
            ? ApiResponse.Fail<ProjectResponse>("Project not found.", StatusCodes.Status404NotFound)
            : ApiResponse.Ok(project.ToResponse());
    }

    public async Task<ApiResponse<ProjectResponse>> UpdateProjectAsync(Guid projectId, UpdateProjectRequest request)
    {
        var userId = currentUser.GetUserId();
        if (!await permissionService.CanManageProject(userId, projectId))
        {
            return ApiResponse.Fail<ProjectResponse>("You do not have permission to update this project.", StatusCodes.Status403Forbidden);
        }

        var project = await ProjectQuery().FirstOrDefaultAsync(item => item.Id == projectId);
        if (project is null)
        {
            return ApiResponse.Fail<ProjectResponse>("Project not found.", StatusCodes.Status404NotFound);
        }

        project.Name = request.Name.Trim();
        project.Description = request.Description?.Trim();
        project.Status = request.Status;
        project.DeadlineUtc = request.DeadlineUtc;
        project.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();

        return ApiResponse.Ok(project.ToResponse(), "Project updated.");
    }

    public async Task<ApiResponse<bool>> DeleteProjectAsync(Guid projectId)
    {
        var userId = currentUser.GetUserId();
        if (!await permissionService.CanManageProject(userId, projectId))
        {
            return ApiResponse.Fail<bool>("You do not have permission to delete this project.", StatusCodes.Status403Forbidden);
        }

        var project = await dbContext.Projects.FirstOrDefaultAsync(item => item.Id == projectId);
        if (project is null)
        {
            return ApiResponse.Fail<bool>("Project not found.", StatusCodes.Status404NotFound);
        }

        dbContext.Projects.Remove(project);
        await dbContext.SaveChangesAsync();

        return ApiResponse.NoData("Project deleted.");
    }

    private IQueryable<Project> ProjectQuery()
    {
        return dbContext.Projects
            .Include(project => project.Members)
            .Include(project => project.Tasks)
            .AsSplitQuery();
    }
}
