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
    IPermissionService permissionService,
    IPineconeVectorStore pineconeVectorStore) : IWorkspaceService
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

        if (currentUser.GetGlobalRole() != GlobalRole.Administrator)
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
        if (!await CanCurrentUserCreateWorkspace(userId))
        {
            return ApiResponse.Fail<WorkspaceResponse>("Only workspace managers can create new workspaces.", StatusCodes.Status403Forbidden);
        }

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
            Role = WorkspaceRole.Administrator
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

    public async Task<ApiResponse<bool>> DeleteWorkspaceAsync(Guid workspaceId)
    {
        var userId = currentUser.GetUserId();
        if (!await IsWorkspaceManager(userId, workspaceId))
        {
            return ApiResponse.Fail<bool>("Only workspace managers can delete this workspace.", StatusCodes.Status403Forbidden);
        }

        var workspace = await dbContext.Workspaces.FirstOrDefaultAsync(item => item.Id == workspaceId);
        if (workspace is null)
        {
            return ApiResponse.Fail<bool>("Workspace not found.", StatusCodes.Status404NotFound);
        }

        var knowledgeChunks = await dbContext.ChannelKnowledgeChunks
            .Where(chunk => chunk.WorkspaceId == workspaceId)
            .ToListAsync();
        var attachments = await dbContext.ChannelAttachments
            .Where(attachment => attachment.WorkspaceId == workspaceId)
            .ToListAsync();

        if (knowledgeChunks.Count > 0)
        {
            try
            {
                await pineconeVectorStore.DeleteByWorkspaceAsync(workspaceId);
            }
            catch (PineconeVectorStoreException ex)
            {
                return ApiResponse.Fail<bool>(ex.Message, StatusCodes.Status502BadGateway);
            }
        }

        dbContext.ChannelKnowledgeChunks.RemoveRange(knowledgeChunks);
        dbContext.ChannelAttachments.RemoveRange(attachments);
        dbContext.Workspaces.Remove(workspace);
        await dbContext.SaveChangesAsync();

        return ApiResponse.NoData("Workspace deleted.");
    }

    public async Task<ApiResponse<IEnumerable<WorkspaceMemberResponse>>> GetWorkspaceMembersAsync(Guid workspaceId)
    {
        var userId = currentUser.GetUserId();
        if (!await permissionService.CanAccessWorkspace(userId, workspaceId))
        {
            return ApiResponse.Fail<IEnumerable<WorkspaceMemberResponse>>("Workspace not found or access denied.", StatusCodes.Status404NotFound);
        }

        var members = await dbContext.WorkspaceMembers
            .Include(member => member.User)
            .Where(member => member.WorkspaceId == workspaceId)
            .OrderBy(member => member.User!.Name)
            .ToListAsync();

        return ApiResponse.Ok(members.Select(ToMemberResponse));
    }

    public async Task<ApiResponse<WorkspaceMemberResponse>> AddWorkspaceMemberAsync(Guid workspaceId, AddWorkspaceMemberRequest request)
    {
        var currentUserId = currentUser.GetUserId();
        if (!await permissionService.CanManageWorkspace(currentUserId, workspaceId))
        {
            return ApiResponse.Fail<WorkspaceMemberResponse>("You do not have permission to add workspace members.", StatusCodes.Status403Forbidden);
        }

        if (!await dbContext.Workspaces.AnyAsync(workspace => workspace.Id == workspaceId))
        {
            return ApiResponse.Fail<WorkspaceMemberResponse>("Workspace not found.", StatusCodes.Status404NotFound);
        }

        var user = await FindUserByEmail(request.Email);
        if (user is null)
        {
            return ApiResponse.Fail<WorkspaceMemberResponse>("Registered user not found.", StatusCodes.Status404NotFound);
        }

        var existingMember = await dbContext.WorkspaceMembers
            .Include(member => member.User)
            .FirstOrDefaultAsync(member => member.WorkspaceId == workspaceId && member.UserId == user.Id);
        if (existingMember is not null)
        {
            return ApiResponse.Fail<WorkspaceMemberResponse>("User is already a workspace member.");
        }

        var member = new WorkspaceMember
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            UserId = user.Id,
            Role = request.Role,
            User = user
        };

        dbContext.WorkspaceMembers.Add(member);
        await dbContext.SaveChangesAsync();

        return ApiResponse.Created(ToMemberResponse(member), "Workspace member added.");
    }

    public async Task<ApiResponse<WorkspaceMemberResponse>> UpdateWorkspaceMemberRoleAsync(Guid workspaceId, Guid userId, UpdateWorkspaceMemberRoleRequest request)
    {
        var currentUserId = currentUser.GetUserId();
        if (!await permissionService.CanManageWorkspace(currentUserId, workspaceId))
        {
            return ApiResponse.Fail<WorkspaceMemberResponse>("You do not have permission to update workspace members.", StatusCodes.Status403Forbidden);
        }

        var member = await dbContext.WorkspaceMembers
            .Include(item => item.User)
            .FirstOrDefaultAsync(item => item.WorkspaceId == workspaceId && item.UserId == userId);
        if (member is null)
        {
            return ApiResponse.Fail<WorkspaceMemberResponse>("Workspace member not found.", StatusCodes.Status404NotFound);
        }

        if (member.Role == WorkspaceRole.Administrator && request.Role != WorkspaceRole.Administrator && await CountWorkspaceAdministrators(workspaceId) <= 1)
        {
            return ApiResponse.Fail<WorkspaceMemberResponse>("A workspace must keep at least one administrator.");
        }

        member.Role = request.Role;
        await dbContext.SaveChangesAsync();

        return ApiResponse.Ok(ToMemberResponse(member), "Workspace member role updated.");
    }

    public async Task<ApiResponse<bool>> RemoveWorkspaceMemberAsync(Guid workspaceId, Guid userId)
    {
        var currentUserId = currentUser.GetUserId();
        if (!await permissionService.CanManageWorkspace(currentUserId, workspaceId))
        {
            return ApiResponse.Fail<bool>("You do not have permission to remove workspace members.", StatusCodes.Status403Forbidden);
        }

        var member = await dbContext.WorkspaceMembers
            .FirstOrDefaultAsync(item => item.WorkspaceId == workspaceId && item.UserId == userId);
        if (member is null)
        {
            return ApiResponse.Fail<bool>("Workspace member not found.", StatusCodes.Status404NotFound);
        }

        if (member.Role == WorkspaceRole.Administrator && await CountWorkspaceAdministrators(workspaceId) <= 1)
        {
            return ApiResponse.Fail<bool>("A workspace must keep at least one administrator.");
        }

        var projectIds = await dbContext.Projects
            .Where(project => project.WorkspaceId == workspaceId)
            .Select(project => project.Id)
            .ToListAsync();
        var channelIds = await dbContext.Channels
            .Where(channel => channel.WorkspaceId == workspaceId)
            .Select(channel => channel.Id)
            .ToListAsync();

        var managedProjectIds = await dbContext.ProjectMembers
            .Where(projectMember =>
                projectIds.Contains(projectMember.ProjectId)
                && projectMember.UserId == userId
                && projectMember.RoleInProject == ProjectRole.Administrator)
            .Select(projectMember => projectMember.ProjectId)
            .ToListAsync();
        foreach (var managedProjectId in managedProjectIds)
        {
            var managerCount = await dbContext.ProjectMembers.CountAsync(projectMember =>
                projectMember.ProjectId == managedProjectId
                && projectMember.RoleInProject == ProjectRole.Administrator);
            if (managerCount <= 1)
            {
                return ApiResponse.Fail<bool>("Cannot remove this workspace member because they are the only project administrator on at least one project.");
            }
        }

        var projectMembers = await dbContext.ProjectMembers
            .Where(projectMember => projectIds.Contains(projectMember.ProjectId) && projectMember.UserId == userId)
            .ToListAsync();
        var channelMembers = await dbContext.ChannelMembers
            .Where(channelMember => channelIds.Contains(channelMember.ChannelId) && channelMember.UserId == userId)
            .ToListAsync();
        var assignedTasks = await dbContext.TaskItems
            .Where(task => projectIds.Contains(task.ProjectId) && task.AssigneeId == userId)
            .ToListAsync();

        foreach (var task in assignedTasks)
        {
            task.AssigneeId = null;
            task.UpdatedAtUtc = DateTime.UtcNow;
        }

        dbContext.ProjectMembers.RemoveRange(projectMembers);
        dbContext.ChannelMembers.RemoveRange(channelMembers);
        dbContext.WorkspaceMembers.Remove(member);
        await dbContext.SaveChangesAsync();

        return ApiResponse.NoData("Workspace member removed.");
    }

    private IQueryable<Workspace> GetWorkspaceQuery()
    {
        return dbContext.Workspaces
            .Include(workspace => workspace.Members)
            .Include(workspace => workspace.Channels)
            .Include(workspace => workspace.Projects)
            .AsSplitQuery();
    }

    private async Task<User?> FindUserByEmail(string email)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        return await dbContext.Users.FirstOrDefaultAsync(user => user.Email == normalizedEmail);
    }

    private async Task<int> CountWorkspaceAdministrators(Guid workspaceId)
    {
        return await dbContext.WorkspaceMembers.CountAsync(member =>
            member.WorkspaceId == workspaceId && member.Role == WorkspaceRole.Administrator);
    }

    private async Task<bool> CanCurrentUserCreateWorkspace(Guid userId)
    {
        if (currentUser.GetGlobalRole() == GlobalRole.Administrator)
        {
            return true;
        }

        return await dbContext.WorkspaceMembers.AnyAsync(member =>
            member.UserId == userId && member.Role == WorkspaceRole.Manager);
    }

    private async Task<bool> IsWorkspaceManager(Guid userId, Guid workspaceId)
    {
        return await dbContext.WorkspaceMembers.AnyAsync(member =>
            member.UserId == userId
            && member.WorkspaceId == workspaceId
            && member.Role == WorkspaceRole.Manager);
    }

    private static WorkspaceMemberResponse ToMemberResponse(WorkspaceMember member)
    {
        return new WorkspaceMemberResponse
        {
            UserId = member.UserId,
            Name = member.User?.Name ?? string.Empty,
            Email = member.User?.Email ?? string.Empty,
            Role = member.Role,
            JoinedAtUtc = member.JoinedAtUtc
        };
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
