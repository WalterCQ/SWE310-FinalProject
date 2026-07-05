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

    public async Task<ApiResponse<IEnumerable<ProjectMemberResponse>>> GetProjectMembersAsync(Guid projectId)
    {
        var userId = currentUser.GetUserId();
        if (!await permissionService.CanAccessProject(userId, projectId))
        {
            return ApiResponse.Fail<IEnumerable<ProjectMemberResponse>>("Project not found or access denied.", StatusCodes.Status404NotFound);
        }

        var members = await dbContext.ProjectMembers
            .Include(member => member.User)
            .Where(member => member.ProjectId == projectId)
            .OrderBy(member => member.User!.Name)
            .ToListAsync();

        return ApiResponse.Ok(members.Select(ToMemberResponse));
    }

    public async Task<ApiResponse<ProjectMemberResponse>> AddProjectMemberAsync(Guid projectId, AddProjectMemberRequest request)
    {
        var currentUserId = currentUser.GetUserId();
        if (!await permissionService.CanManageProject(currentUserId, projectId))
        {
            return ApiResponse.Fail<ProjectMemberResponse>("You do not have permission to add project members.", StatusCodes.Status403Forbidden);
        }

        var project = await dbContext.Projects.AsNoTracking().FirstOrDefaultAsync(item => item.Id == projectId);
        if (project is null)
        {
            return ApiResponse.Fail<ProjectMemberResponse>("Project not found.", StatusCodes.Status404NotFound);
        }

        var user = await FindUserByEmail(request.Email);
        if (user is null)
        {
            return ApiResponse.Fail<ProjectMemberResponse>("Registered user not found.", StatusCodes.Status404NotFound);
        }

        var isWorkspaceMember = await dbContext.WorkspaceMembers.AnyAsync(member =>
            member.WorkspaceId == project.WorkspaceId && member.UserId == user.Id);
        if (!isWorkspaceMember)
        {
            return ApiResponse.Fail<ProjectMemberResponse>("User must be a workspace member before joining this project.");
        }

        var existingMember = await dbContext.ProjectMembers
            .Include(member => member.User)
            .FirstOrDefaultAsync(member => member.ProjectId == projectId && member.UserId == user.Id);
        if (existingMember is not null)
        {
            return ApiResponse.Fail<ProjectMemberResponse>("User is already a project member.");
        }

        var member = new ProjectMember
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            UserId = user.Id,
            RoleInProject = request.RoleInProject,
            User = user
        };

        dbContext.ProjectMembers.Add(member);
        await dbContext.SaveChangesAsync();

        return ApiResponse.Created(ToMemberResponse(member), "Project member added.");
    }

    public async Task<ApiResponse<ProjectMemberResponse>> UpdateProjectMemberRoleAsync(Guid projectId, Guid userId, UpdateProjectMemberRoleRequest request)
    {
        var currentUserId = currentUser.GetUserId();
        if (!await permissionService.CanManageProject(currentUserId, projectId))
        {
            return ApiResponse.Fail<ProjectMemberResponse>("You do not have permission to update project members.", StatusCodes.Status403Forbidden);
        }

        var member = await dbContext.ProjectMembers
            .Include(item => item.User)
            .FirstOrDefaultAsync(item => item.ProjectId == projectId && item.UserId == userId);
        if (member is null)
        {
            return ApiResponse.Fail<ProjectMemberResponse>("Project member not found.", StatusCodes.Status404NotFound);
        }

        if (member.RoleInProject == ProjectRole.ProjectManager
            && request.RoleInProject != ProjectRole.ProjectManager
            && await CountProjectManagers(projectId) <= 1)
        {
            return ApiResponse.Fail<ProjectMemberResponse>("A project must keep at least one project manager.");
        }

        member.RoleInProject = request.RoleInProject;
        await dbContext.SaveChangesAsync();

        return ApiResponse.Ok(ToMemberResponse(member), "Project member role updated.");
    }

    public async Task<ApiResponse<bool>> RemoveProjectMemberAsync(Guid projectId, Guid userId)
    {
        var currentUserId = currentUser.GetUserId();
        if (!await permissionService.CanManageProject(currentUserId, projectId))
        {
            return ApiResponse.Fail<bool>("You do not have permission to remove project members.", StatusCodes.Status403Forbidden);
        }

        var member = await dbContext.ProjectMembers
            .FirstOrDefaultAsync(item => item.ProjectId == projectId && item.UserId == userId);
        if (member is null)
        {
            return ApiResponse.Fail<bool>("Project member not found.", StatusCodes.Status404NotFound);
        }

        if (member.RoleInProject == ProjectRole.ProjectManager && await CountProjectManagers(projectId) <= 1)
        {
            return ApiResponse.Fail<bool>("A project must keep at least one project manager.");
        }

        var assignedTasks = await dbContext.TaskItems
            .Where(task => task.ProjectId == projectId && task.AssigneeId == userId)
            .ToListAsync();
        foreach (var task in assignedTasks)
        {
            task.AssigneeId = null;
            task.UpdatedAtUtc = DateTime.UtcNow;
        }

        dbContext.ProjectMembers.Remove(member);
        await dbContext.SaveChangesAsync();

        return ApiResponse.NoData("Project member removed.");
    }

    private IQueryable<Project> ProjectQuery()
    {
        return dbContext.Projects
            .Include(project => project.Members)
            .Include(project => project.Tasks)
            .AsSplitQuery();
    }

    private async Task<User?> FindUserByEmail(string email)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        return await dbContext.Users.FirstOrDefaultAsync(user => user.Email == normalizedEmail);
    }

    private async Task<int> CountProjectManagers(Guid projectId)
    {
        return await dbContext.ProjectMembers.CountAsync(member =>
            member.ProjectId == projectId && member.RoleInProject == ProjectRole.ProjectManager);
    }

    private static ProjectMemberResponse ToMemberResponse(ProjectMember member)
    {
        return new ProjectMemberResponse
        {
            UserId = member.UserId,
            Name = member.User?.Name ?? string.Empty,
            Email = member.User?.Email ?? string.Empty,
            RoleInProject = member.RoleInProject,
            JoinedAtUtc = member.JoinedAtUtc
        };
    }
}
