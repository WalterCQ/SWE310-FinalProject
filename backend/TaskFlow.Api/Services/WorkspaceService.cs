using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.DTOs.Workspaces;
using TaskFlow.Api.Helpers;
using TaskFlow.Api.Models;
using TaskFlow.Api.Services.Interfaces;

namespace TaskFlow.Api.Services;

public class WorkspaceService(
    AppDbContext dbContext,
    ICurrentUserService currentUser,
    IPermissionService permissionService) : IWorkspaceService
{
    public async Task<ApiResponse<IEnumerable<WorkspaceResponse>>> GetWorkspacesAsync()
    {
        if (!currentUser.IsAuthenticated())
        {
            return ApiResponse.Fail<IEnumerable<WorkspaceResponse>>("Authentication is required.", StatusCodes.Status401Unauthorized);
        }

        var userId = currentUser.GetUserId();
        var query = dbContext.Workspaces
            .Include(workspace => workspace.Members)
            .Include(workspace => workspace.Channels)
            .Include(workspace => workspace.Projects)
            .AsSplitQuery()
            .AsQueryable();

        if (currentUser.GetGlobalRole() != GlobalRole.Admin)
        {
            query = query.Where(workspace => workspace.Members.Any(member => member.UserId == userId));
        }

        var workspaces = await query
            .OrderBy(workspace => workspace.Name)
            .ToListAsync();

        return ApiResponse.Ok(workspaces.Select(workspace => workspace.ToResponse()));
    }

    public async Task<ApiResponse<WorkspaceResponse>> CreateWorkspaceAsync(CreateWorkspaceRequest request)
    {
        if (!currentUser.IsAuthenticated())
        {
            return ApiResponse.Fail<WorkspaceResponse>("Authentication is required.", StatusCodes.Status401Unauthorized);
        }

        var userId = currentUser.GetUserId();
        await EnsureCurrentUserExists(userId);

        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            CreatedByUserId = userId
        };

        workspace.Members.Add(new WorkspaceMember
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            UserId = userId,
            Role = WorkspaceRole.Owner
        });

        dbContext.Workspaces.Add(workspace);
        await dbContext.SaveChangesAsync();

        return ApiResponse.Created(workspace.ToResponse(), "Workspace created.");
    }

    public async Task<ApiResponse<WorkspaceResponse>> GetWorkspaceAsync(Guid workspaceId)
    {
        var userId = currentUser.GetUserId();
        if (!await permissionService.CanAccessWorkspace(userId, workspaceId))
        {
            return ApiResponse.Fail<WorkspaceResponse>("Workspace not found or access denied.", StatusCodes.Status404NotFound);
        }

        var workspace = await GetWorkspaceQuery().FirstOrDefaultAsync(item => item.Id == workspaceId);
        return workspace is null
            ? ApiResponse.Fail<WorkspaceResponse>("Workspace not found.", StatusCodes.Status404NotFound)
            : ApiResponse.Ok(workspace.ToResponse());
    }

    public async Task<ApiResponse<WorkspaceResponse>> UpdateWorkspaceAsync(Guid workspaceId, UpdateWorkspaceRequest request)
    {
        var userId = currentUser.GetUserId();
        if (!await permissionService.CanManageWorkspace(userId, workspaceId))
        {
            return ApiResponse.Fail<WorkspaceResponse>("You do not have permission to update this workspace.", StatusCodes.Status403Forbidden);
        }

        var workspace = await GetWorkspaceQuery().FirstOrDefaultAsync(item => item.Id == workspaceId);
        if (workspace is null)
        {
            return ApiResponse.Fail<WorkspaceResponse>("Workspace not found.", StatusCodes.Status404NotFound);
        }

        workspace.Name = request.Name.Trim();
        workspace.Description = request.Description?.Trim();
        workspace.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();

        return ApiResponse.Ok(workspace.ToResponse(), "Workspace updated.");
    }

    private IQueryable<Workspace> GetWorkspaceQuery()
    {
        return dbContext.Workspaces
            .Include(workspace => workspace.Members)
            .Include(workspace => workspace.Channels)
            .Include(workspace => workspace.Projects)
            .AsSplitQuery();
    }

    private async Task EnsureCurrentUserExists(Guid userId)
    {
        if (await dbContext.Users.AnyAsync(user => user.Id == userId))
        {
            return;
        }

        var email = currentUser.GetUserEmail() ?? $"user-{userId:N}@taskflow.local";
        dbContext.Users.Add(new User
        {
            Id = userId,
            Email = email,
            Name = email.Split('@')[0],
            GlobalRole = currentUser.GetGlobalRole()
        });
    }
}
