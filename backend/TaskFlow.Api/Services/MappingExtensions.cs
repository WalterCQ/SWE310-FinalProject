using TaskFlow.Api.DTOs.Channels;
using TaskFlow.Api.DTOs.Messages;
using TaskFlow.Api.DTOs.Notifications;
using TaskFlow.Api.DTOs.Projects;
using TaskFlow.Api.DTOs.Tasks;
using TaskFlow.Api.DTOs.Workspaces;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Services;

internal static class MappingExtensions
{
    public static WorkspaceResponse ToResponse(this Workspace workspace)
    {
        return new WorkspaceResponse
        {
            Id = workspace.Id,
            Name = workspace.Name,
            Description = workspace.Description,
            CreatedByUserId = workspace.CreatedByUserId,
            MemberCount = workspace.Members.Count,
            ChannelCount = workspace.Channels.Count,
            ProjectCount = workspace.Projects.Count,
            CreatedAtUtc = workspace.CreatedAtUtc,
            UpdatedAtUtc = workspace.UpdatedAtUtc
        };
    }

    public static ChannelResponse ToResponse(this Channel channel)
    {
        return new ChannelResponse
        {
            Id = channel.Id,
            WorkspaceId = channel.WorkspaceId,
            Name = channel.Name,
            Description = channel.Description,
            IsPrivate = channel.IsPrivate,
            CreatedByUserId = channel.CreatedByUserId,
            MemberCount = channel.Members.Count,
            CreatedAtUtc = channel.CreatedAtUtc
        };
    }

    public static MessageResponse ToResponse(this Message message)
    {
        return new MessageResponse
        {
            Id = message.Id,
            ChannelId = message.ChannelId,
            SenderId = message.SenderId,
            SenderName = message.Sender?.Name ?? string.Empty,
            Content = message.Content,
            IsDeleted = message.IsDeleted,
            CreatedAtUtc = message.CreatedAtUtc,
            EditedAtUtc = message.EditedAtUtc
        };
    }

    public static ProjectResponse ToResponse(this Project project)
    {
        return new ProjectResponse
        {
            Id = project.Id,
            WorkspaceId = project.WorkspaceId,
            Name = project.Name,
            Description = project.Description,
            Status = project.Status,
            CreatedByUserId = project.CreatedByUserId,
            DeadlineUtc = project.DeadlineUtc,
            MemberCount = project.Members.Count,
            TaskCount = project.Tasks.Count,
            CompletedTaskCount = project.Tasks.Count(task => task.Status == TaskItemStatus.Done),
            CreatedAtUtc = project.CreatedAtUtc,
            UpdatedAtUtc = project.UpdatedAtUtc
        };
    }

    public static TaskResponse ToResponse(this TaskItem task)
    {
        return new TaskResponse
        {
            Id = task.Id,
            ProjectId = task.ProjectId,
            Title = task.Title,
            Description = task.Description,
            Status = task.Status,
            Priority = task.Priority,
            CreatedByUserId = task.CreatedByUserId,
            AssigneeId = task.AssigneeId,
            AssigneeName = task.Assignee?.Name,
            DeadlineUtc = task.DeadlineUtc,
            CreatedAtUtc = task.CreatedAtUtc,
            UpdatedAtUtc = task.UpdatedAtUtc,
            CompletedAtUtc = task.CompletedAtUtc
        };
    }

    public static NotificationResponse ToResponse(this Notification notification)
    {
        return new NotificationResponse
        {
            Id = notification.Id,
            UserId = notification.UserId,
            WorkspaceId = notification.WorkspaceId,
            Title = notification.Title,
            Message = notification.Message,
            Type = notification.Type,
            IsRead = notification.IsRead,
            CreatedAtUtc = notification.CreatedAtUtc
        };
    }
}
