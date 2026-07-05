using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.Models;
using TaskFlow.Api.Services.Interfaces;

namespace TaskFlow.Api.Services;

public class PermissionService(AppDbContext dbContext) : IPermissionService
{
    public async Task<bool> CanAccessWorkspace(Guid userId, Guid workspaceId)
    {
        return await IsGlobalAdmin(userId)
            || await dbContext.WorkspaceMembers.AnyAsync(member =>
                member.UserId == userId && member.WorkspaceId == workspaceId);
    }

    public async Task<bool> CanManageWorkspace(Guid userId, Guid workspaceId)
    {
        return await IsGlobalAdmin(userId)
            || await dbContext.WorkspaceMembers.AnyAsync(member =>
                member.UserId == userId
                && member.WorkspaceId == workspaceId
                && (member.Role == WorkspaceRole.Owner || member.Role == WorkspaceRole.Admin));
    }

    public async Task<bool> CanAccessChannel(Guid userId, Guid channelId)
    {
        if (await IsGlobalAdmin(userId))
        {
            return true;
        }

        var channel = await dbContext.Channels
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == channelId);

        if (channel is null)
        {
            return false;
        }

        if (!await CanAccessWorkspace(userId, channel.WorkspaceId))
        {
            return false;
        }

        return !channel.IsPrivate
            || await dbContext.ChannelMembers.AnyAsync(member =>
                member.ChannelId == channelId && member.UserId == userId);
    }

    public Task<bool> CanSendMessage(Guid userId, Guid channelId)
    {
        return CanAccessChannel(userId, channelId);
    }

    public async Task<bool> CanAccessProject(Guid userId, Guid projectId)
    {
        if (await IsGlobalAdmin(userId))
        {
            return true;
        }

        var project = await dbContext.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == projectId);

        return project is not null && await CanAccessWorkspace(userId, project.WorkspaceId);
    }

    public async Task<bool> CanManageProject(Guid userId, Guid projectId)
    {
        if (await IsGlobalAdmin(userId))
        {
            return true;
        }

        var project = await dbContext.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == projectId);

        if (project is null)
        {
            return false;
        }

        return await CanManageWorkspace(userId, project.WorkspaceId)
            || await dbContext.ProjectMembers.AnyAsync(member =>
                member.UserId == userId
                && member.ProjectId == projectId
                && member.RoleInProject == ProjectRole.ProjectManager);
    }

    public async Task<bool> CanCreateTask(Guid userId, Guid projectId)
    {
        return await CanManageProject(userId, projectId);
    }

    public async Task<bool> CanUpdateTask(Guid userId, Guid taskId)
    {
        var task = await dbContext.TaskItems
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == taskId);

        if (task is null)
        {
            return false;
        }

        return await CanManageProject(userId, task.ProjectId);
    }

    public Task<bool> CanUseAiCommand(Guid userId, Guid workspaceId)
    {
        return CanAccessWorkspace(userId, workspaceId);
    }

    private async Task<bool> IsGlobalAdmin(Guid userId)
    {
        return await dbContext.Users.AnyAsync(user => user.Id == userId && user.GlobalRole == GlobalRole.Admin);
    }
}
