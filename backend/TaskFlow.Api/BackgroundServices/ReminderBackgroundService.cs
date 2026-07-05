using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.DTOs.Notifications;
using TaskFlow.Api.Hubs;
using TaskFlow.Api.Models;
using TaskFlow.Api.Services.Interfaces;

namespace TaskFlow.Api.BackgroundServices;

public class ReminderBackgroundService(
    IServiceScopeFactory scopeFactory,
    IHubContext<NotificationHub> notificationHub,
    ILogger<ReminderBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckTasksAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Reminder background check failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }

    private async Task CheckTasksAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var now = DateTime.UtcNow;
        var tomorrow = now.Date.AddDays(1);
        var dayAfterTomorrow = tomorrow.AddDays(1);

        var tasks = await dbContext.TaskItems
            .Include(task => task.Project)
            .Where(task => task.Status != TaskItemStatus.Done
                && task.AssigneeId != null
                && (task.DeadlineUtc < now
                    || (task.DeadlineUtc >= tomorrow && task.DeadlineUtc < dayAfterTomorrow)))
            .OrderBy(task => task.DeadlineUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        foreach (var task in tasks)
        {
            var title = task.DeadlineUtc < now ? "Task overdue" : "Task reminder";
            var message = $"{task.Title} in {task.Project?.Name ?? "project"} needs attention.";
            var response = await notificationService.CreateNotificationAsync(new CreateNotificationRequest
            {
                UserId = task.AssigneeId!.Value,
                WorkspaceId = task.Project?.WorkspaceId,
                Title = title,
                Message = message,
                Type = NotificationType.Reminder
            });

            if (response.Success && response.Data is not null)
            {
                await notificationHub.Clients.Group($"user:{task.AssigneeId.Value}")
                    .SendAsync("NotificationReceived", response.Data, cancellationToken);
            }
        }

        var unassignedHighPriorityTasks = await dbContext.TaskItems
            .Include(task => task.Project)
            .ThenInclude(project => project!.Members)
            .Where(task => task.Status != TaskItemStatus.Done
                && task.Priority == TaskPriority.High
                && task.AssigneeId == null)
            .OrderBy(task => task.CreatedAtUtc)
            .Take(25)
            .ToListAsync(cancellationToken);

        foreach (var task in unassignedHighPriorityTasks)
        {
            var managerIds = task.Project?.Members
                .Where(member => member.RoleInProject == ProjectRole.Administrator)
                .Select(member => member.UserId)
                .Distinct()
                .ToList() ?? [];

            foreach (var managerId in managerIds)
            {
                await NotifyUser(notificationService, managerId, task.Project?.WorkspaceId, "High priority task needs assignee", $"{task.Title} has no assignee.", cancellationToken);
            }
        }

        var riskyProjects = await dbContext.Projects
            .Include(project => project.Tasks)
            .Include(project => project.Members)
            .Where(project => project.DeadlineUtc != null
                && project.DeadlineUtc <= now.AddDays(3)
                && project.Status != ProjectStatus.Completed)
            .OrderBy(project => project.DeadlineUtc)
            .Take(25)
            .ToListAsync(cancellationToken);

        foreach (var project in riskyProjects)
        {
            var completionRate = project.Tasks.Count == 0
                ? 0
                : project.Tasks.Count(task => task.Status == TaskItemStatus.Done) * 100.0 / project.Tasks.Count;

            if (completionRate >= 60)
            {
                continue;
            }

            foreach (var managerId in project.Members.Where(member => member.RoleInProject == ProjectRole.Administrator).Select(member => member.UserId))
            {
                await NotifyUser(notificationService, managerId, project.WorkspaceId, "Project deadline risk", $"{project.Name} is near its deadline with {completionRate:0}% completion.", cancellationToken);
            }
        }
    }

    private async Task NotifyUser(
        INotificationService notificationService,
        Guid userId,
        Guid? workspaceId,
        string title,
        string message,
        CancellationToken cancellationToken)
    {
        var response = await notificationService.CreateNotificationAsync(new CreateNotificationRequest
        {
            UserId = userId,
            WorkspaceId = workspaceId,
            Title = title,
            Message = message,
            Type = NotificationType.Reminder
        });

        if (response.Success && response.Data is not null)
        {
            await notificationHub.Clients.Group($"user:{userId}")
                .SendAsync("NotificationReceived", response.Data, cancellationToken);
        }
    }
}
