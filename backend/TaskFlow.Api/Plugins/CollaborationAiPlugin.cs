using System.ComponentModel;
using TaskFlow.Api.DTOs.Notifications;
using TaskFlow.Api.DTOs.Projects;
using TaskFlow.Api.DTOs.Tasks;
using TaskFlow.Api.Models;
using TaskFlow.Api.Services.Interfaces;
using Microsoft.SemanticKernel;

namespace TaskFlow.Api.Plugins;

public class CollaborationAiPlugin(
    ICurrentUserService currentUser,
    IProjectService projectService,
    ITaskService taskService,
    IChannelService channelService,
    IDashboardService dashboardService,
    INotificationService notificationService)
{
    [KernelFunction("create_project")]
    [Description("Creates a project in a workspace through the backend project service.")]
    public async Task<string> CreateProject(string workspaceId, string name, string? deadlineUtc = null)
    {
        if (!Guid.TryParse(workspaceId, out var parsedWorkspaceId))
        {
            return "Invalid workspace id.";
        }

        var request = new CreateProjectRequest
        {
            Name = name,
            DeadlineUtc = DateTime.TryParse(deadlineUtc, out var parsedDeadline) ? parsedDeadline : null
        };

        var result = await projectService.CreateProjectAsync(parsedWorkspaceId, request);
        return result.Success ? $"Project created: {result.Data?.Name}" : result.Message;
    }

    [KernelFunction("update_project")]
    [Description("Updates a project through the backend project service.")]
    public async Task<string> UpdateProject(string projectId, string name, string? description = null, string status = "Active")
    {
        if (!Guid.TryParse(projectId, out var parsedProjectId))
        {
            return "Invalid project id.";
        }

        var request = new UpdateProjectRequest
        {
            Name = name,
            Description = description,
            Status = Enum.TryParse<ProjectStatus>(status, true, out var parsedStatus) ? parsedStatus : ProjectStatus.Active
        };

        var result = await projectService.UpdateProjectAsync(parsedProjectId, request);
        return result.Success ? $"Project updated: {result.Data?.Name}" : result.Message;
    }

    [KernelFunction("create_task")]
    [Description("Creates a task in a project through the backend task service.")]
    public async Task<string> CreateTask(string projectId, string title, string? description = null, string priority = "Medium")
    {
        if (!Guid.TryParse(projectId, out var parsedProjectId))
        {
            return "Invalid project id.";
        }

        var request = new CreateTaskRequest
        {
            Title = title,
            Description = description,
            Priority = Enum.TryParse<TaskPriority>(priority, true, out var parsedPriority) ? parsedPriority : TaskPriority.Medium
        };

        var result = await taskService.CreateTaskAsync(parsedProjectId, request);
        return result.Success ? $"Task created: {result.Data?.Title}" : result.Message;
    }

    [KernelFunction("create_task_from_message")]
    [Description("Creates a task from a chat message through the backend task service.")]
    public Task<string> CreateTaskFromMessage(string projectId, string messageContent)
    {
        var title = messageContent.Length > 80 ? messageContent[..80] : messageContent;
        return CreateTask(projectId, title, messageContent, "Medium");
    }

    [KernelFunction("assign_task")]
    [Description("Assigns a task through the backend task service.")]
    public async Task<string> AssignTask(string taskId, string assigneeId)
    {
        if (!Guid.TryParse(taskId, out var parsedTaskId) || !Guid.TryParse(assigneeId, out var parsedAssigneeId))
        {
            return "Invalid task id or assignee id.";
        }

        var result = await taskService.AssignTaskAsync(parsedTaskId, new AssignTaskRequest { AssigneeId = parsedAssigneeId });
        return result.Success ? $"Task assigned: {result.Data?.Title}" : result.Message;
    }

    [KernelFunction("update_task_status")]
    [Description("Updates task status through the backend task service.")]
    public async Task<string> UpdateTaskStatus(string taskId, string status)
    {
        if (!Guid.TryParse(taskId, out var parsedTaskId))
        {
            return "Invalid task id.";
        }

        var request = new UpdateTaskStatusRequest
        {
            Status = Enum.TryParse<TaskItemStatus>(status, true, out var parsedStatus) ? parsedStatus : TaskItemStatus.Todo
        };

        var result = await taskService.UpdateTaskStatusAsync(parsedTaskId, request);
        return result.Success ? $"Task status updated: {result.Data?.Status}" : result.Message;
    }

    [KernelFunction("set_task_deadline")]
    [Description("Sets task deadline through the backend task service.")]
    public async Task<string> SetTaskDeadline(string taskId, string? deadlineUtc)
    {
        if (!Guid.TryParse(taskId, out var parsedTaskId))
        {
            return "Invalid task id.";
        }

        var request = new SetTaskDeadlineRequest
        {
            DeadlineUtc = DateTime.TryParse(deadlineUtc, out var parsedDeadline) ? parsedDeadline : null
        };

        var result = await taskService.SetTaskDeadlineAsync(parsedTaskId, request);
        return result.Success ? $"Task deadline updated: {result.Data?.DeadlineUtc}" : result.Message;
    }

    [KernelFunction("summarize_channel")]
    [Description("Reads channel messages through the backend channel service.")]
    public async Task<string> SummarizeChannel(string channelId)
    {
        if (!Guid.TryParse(channelId, out var parsedChannelId))
        {
            return "Invalid channel id.";
        }

        var result = await channelService.GetChannelMessagesAsync(parsedChannelId);
        if (!result.Success || result.Data is null)
        {
            return result.Message;
        }

        var messages = result.Data
            .Where(message => !message.Content.StartsWith(IAiCommandService.ChannelAiMessagePrefix, StringComparison.Ordinal))
            .TakeLast(20)
            .Select(message => $"{message.SenderName}: {message.Content}");
        return string.Join(Environment.NewLine, messages);
    }

    [KernelFunction("summarize_project")]
    [Description("Reads project dashboard data through the backend dashboard service.")]
    public async Task<string> SummarizeProject(string projectId)
    {
        if (!Guid.TryParse(projectId, out var parsedProjectId))
        {
            return "Invalid project id.";
        }

        var result = await dashboardService.GetProjectDashboardAsync(parsedProjectId);
        if (!result.Success || result.Data is null)
        {
            return result.Message;
        }

        return $"Tasks: {result.Data.TaskCount}, completed: {result.Data.CompletedTaskCount}, overdue: {result.Data.OverdueTaskCount}, completion: {result.Data.CompletionRate}%.";
    }

    [KernelFunction("analyze_project_risk")]
    [Description("Reads project dashboard data to identify delivery risks.")]
    public Task<string> AnalyzeProjectRisk(string projectId)
    {
        return SummarizeProject(projectId);
    }

    [KernelFunction("create_reminder")]
    [Description("Creates a reminder notification through the backend notification service.")]
    public async Task<string> CreateReminder(string workspaceId, string title, string message)
    {
        if (!Guid.TryParse(workspaceId, out var parsedWorkspaceId))
        {
            return "Invalid workspace id.";
        }

        var result = await notificationService.CreateNotificationAsync(new CreateNotificationRequest
        {
            UserId = currentUser.GetUserId(),
            WorkspaceId = parsedWorkspaceId,
            Title = title,
            Message = message,
            Type = NotificationType.Reminder
        });

        return result.Success ? "Reminder created." : result.Message;
    }

    [KernelFunction("get_upcoming_deadlines")]
    [Description("Gets upcoming task deadlines through the backend task service.")]
    public async Task<string> GetUpcomingDeadlines(string projectId)
    {
        if (!Guid.TryParse(projectId, out var parsedProjectId))
        {
            return "Invalid project id.";
        }

        var result = await taskService.GetProjectTasksAsync(parsedProjectId);
        if (!result.Success || result.Data is null)
        {
            return result.Message;
        }

        var upcoming = result.Data
            .Where(task => task.DeadlineUtc is not null && task.Status != TaskItemStatus.Done)
            .OrderBy(task => task.DeadlineUtc)
            .Take(10)
            .Select(task => $"{task.Title}: {task.DeadlineUtc:u}");

        return string.Join(Environment.NewLine, upcoming);
    }
}
