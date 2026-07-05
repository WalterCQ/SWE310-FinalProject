using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Data.DemoData;

public static class DemoDataAccess
{
    public static async Task<bool> GrantUserDemoDataAccessAsync(
        AppDbContext dbContext,
        Guid userId,
        WorkspaceRole workspaceRole = WorkspaceRole.Member,
        ProjectRole projectRole = ProjectRole.Manager,
        CancellationToken cancellationToken = default)
    {
        var workspaceExists = await dbContext.Workspaces
            .AnyAsync(workspace => workspace.Id == DemoDataIds.WorkspaceId, cancellationToken);

        if (!workspaceExists)
        {
            return false;
        }

        await EnsureWorkspaceMembershipAsync(dbContext, userId, workspaceRole, cancellationToken);
        await EnsureChannelMembershipsAsync(dbContext, userId, cancellationToken);
        await EnsureProjectMembershipsAsync(dbContext, userId, projectRole, cancellationToken);
        await EnsureDemoNotificationsAsync(dbContext, userId, cancellationToken);

        return true;
    }

    private static async Task EnsureWorkspaceMembershipAsync(
        AppDbContext dbContext,
        Guid userId,
        WorkspaceRole role,
        CancellationToken cancellationToken)
    {
        var membership = await dbContext.WorkspaceMembers.FirstOrDefaultAsync(
            item => item.WorkspaceId == DemoDataIds.WorkspaceId && item.UserId == userId,
            cancellationToken);

        if (membership is null)
        {
            dbContext.WorkspaceMembers.Add(new WorkspaceMember
            {
                Id = Guid.NewGuid(),
                WorkspaceId = DemoDataIds.WorkspaceId,
                UserId = userId,
                Role = role
            });
            return;
        }

        if (role != WorkspaceRole.Member && membership.Role != role)
        {
            membership.Role = role;
        }
    }

    private static async Task EnsureChannelMembershipsAsync(
        AppDbContext dbContext,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var existingChannelIds = await dbContext.Channels
            .Where(channel => DemoDataIds.ChannelIds.Contains(channel.Id))
            .Select(channel => channel.Id)
            .ToListAsync(cancellationToken);
        var joinedChannelIds = await dbContext.ChannelMembers
            .Where(member => member.UserId == userId && existingChannelIds.Contains(member.ChannelId))
            .Select(member => member.ChannelId)
            .ToListAsync(cancellationToken);
        var joinedLookup = joinedChannelIds.ToHashSet();

        foreach (var channelId in existingChannelIds.Where(channelId => !joinedLookup.Contains(channelId)))
        {
            dbContext.ChannelMembers.Add(new ChannelMember
            {
                Id = Guid.NewGuid(),
                ChannelId = channelId,
                UserId = userId
            });
        }
    }

    private static async Task EnsureProjectMembershipsAsync(
        AppDbContext dbContext,
        Guid userId,
        ProjectRole role,
        CancellationToken cancellationToken)
    {
        var existingProjectIds = await dbContext.Projects
            .Where(project => DemoDataIds.ProjectIds.Contains(project.Id))
            .Select(project => project.Id)
            .ToListAsync(cancellationToken);
        var memberships = await dbContext.ProjectMembers
            .Where(member => member.UserId == userId && existingProjectIds.Contains(member.ProjectId))
            .ToListAsync(cancellationToken);
        var membershipLookup = memberships.ToDictionary(member => member.ProjectId);

        foreach (var projectId in existingProjectIds)
        {
            if (membershipLookup.TryGetValue(projectId, out var membership))
            {
                if (role == ProjectRole.Administrator && membership.RoleInProject != role)
                {
                    membership.RoleInProject = role;
                }

                continue;
            }

            dbContext.ProjectMembers.Add(new ProjectMember
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                UserId = userId,
                RoleInProject = role
            });
        }
    }

    private static async Task EnsureDemoNotificationsAsync(
        AppDbContext dbContext,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var templates = new[]
        {
            new NotificationTemplate(
                "Demo workspace is ready",
                "Open Dashboard, Projects, Tasks, Channels, AI Assistant, and Notifications to show live Azure data.",
                NotificationType.General,
                false,
                now.AddMinutes(-12)),
            new NotificationTemplate(
                "Presentation checklist due soon",
                "Record the dashboard, task creation, chat, notification read state, and AI project summary.",
                NotificationType.Reminder,
                false,
                now.AddHours(-2)),
            new NotificationTemplate(
                "AI project summary has new context",
                "The AI demo project now includes backend, task, and channel records for grounded summaries.",
                NotificationType.Ai,
                true,
                now.AddHours(-5))
        };

        var titles = templates.Select(template => template.Title).ToArray();
        var existingNotifications = await dbContext.Notifications
            .Where(notification =>
                notification.UserId == userId
                && notification.WorkspaceId == DemoDataIds.WorkspaceId
                && titles.Contains(notification.Title))
            .ToListAsync(cancellationToken);
        var notificationLookup = existingNotifications.ToDictionary(notification => notification.Title);

        foreach (var template in templates)
        {
            if (notificationLookup.TryGetValue(template.Title, out var notification))
            {
                notification.Message = template.Message;
                notification.Type = template.Type;
                notification.IsRead = template.IsRead;
                continue;
            }

            dbContext.Notifications.Add(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                WorkspaceId = DemoDataIds.WorkspaceId,
                Title = template.Title,
                Message = template.Message,
                Type = template.Type,
                IsRead = template.IsRead,
                CreatedAtUtc = template.CreatedAtUtc
            });
        }
    }

    private sealed record NotificationTemplate(
        string Title,
        string Message,
        NotificationType Type,
        bool IsRead,
        DateTime CreatedAtUtc);
}
