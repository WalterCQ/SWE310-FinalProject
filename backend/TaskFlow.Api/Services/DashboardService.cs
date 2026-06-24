using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.DTOs.Dashboard;
using TaskFlow.Api.Helpers;
using TaskFlow.Api.Models;
using TaskFlow.Api.Services.Interfaces;

namespace TaskFlow.Api.Services;

public class DashboardService(
    AppDbContext dbContext,
    ICurrentUserService currentUser,
    IPermissionService permissionService) : IDashboardService
{
    public async Task<ApiResponse<WorkspaceDashboardResponse>> GetWorkspaceDashboardAsync(Guid workspaceId)
    {
        var userId = currentUser.GetUserId();
        if (!await permissionService.CanAccessWorkspace(userId, workspaceId))
        {
            return ApiResponse.Fail<WorkspaceDashboardResponse>("Workspace not found or access denied.", StatusCodes.Status404NotFound);
        }

        var now = DateTime.UtcNow;
        var projects = await dbContext.Projects
            .Include(project => project.Tasks)
            .Where(project => project.WorkspaceId == workspaceId)
            .ToListAsync();

        var tasks = projects.SelectMany(project => project.Tasks).ToList();
        var activities = await dbContext.ActivityLogs
            .Where(log => log.WorkspaceId == workspaceId)
            .OrderByDescending(log => log.CreatedAtUtc)
            .Take(5)
            .Select(log => $"{log.Action} {log.EntityType}")
            .ToListAsync();

        var dashboard = new WorkspaceDashboardResponse
        {
            WorkspaceId = workspaceId,
            ProjectCount = projects.Count,
            ChannelCount = await dbContext.Channels.CountAsync(channel => channel.WorkspaceId == workspaceId),
            TaskCount = tasks.Count,
            CompletedTaskCount = tasks.Count(task => task.Status == TaskItemStatus.Done),
            OverdueTaskCount = tasks.Count(task => task.DeadlineUtc < now && task.Status != TaskItemStatus.Done),
            TasksByStatus = tasks
                .GroupBy(task => task.Status.ToString())
                .ToDictionary(group => group.Key, group => group.Count()),
            RecentActivities = activities
        };

        return ApiResponse.Ok(dashboard);
    }

    public async Task<ApiResponse<ProjectDashboardResponse>> GetProjectDashboardAsync(Guid projectId)
    {
        var userId = currentUser.GetUserId();
        if (!await permissionService.CanAccessProject(userId, projectId))
        {
            return ApiResponse.Fail<ProjectDashboardResponse>("Project not found or access denied.", StatusCodes.Status404NotFound);
        }

        var project = await dbContext.Projects
            .Include(item => item.Tasks)
            .FirstOrDefaultAsync(item => item.Id == projectId);

        if (project is null)
        {
            return ApiResponse.Fail<ProjectDashboardResponse>("Project not found.", StatusCodes.Status404NotFound);
        }

        var now = DateTime.UtcNow;
        var taskCount = project.Tasks.Count;
        var completedTaskCount = project.Tasks.Count(task => task.Status == TaskItemStatus.Done);
        var dashboard = new ProjectDashboardResponse
        {
            ProjectId = projectId,
            TaskCount = taskCount,
            CompletedTaskCount = completedTaskCount,
            OverdueTaskCount = project.Tasks.Count(task => task.DeadlineUtc < now && task.Status != TaskItemStatus.Done),
            CompletionRate = taskCount == 0 ? 0 : Math.Round(completedTaskCount * 100.0 / taskCount, 2),
            DeadlineUtc = project.DeadlineUtc,
            TasksByStatus = project.Tasks
                .GroupBy(task => task.Status.ToString())
                .ToDictionary(group => group.Key, group => group.Count())
        };

        return ApiResponse.Ok(dashboard);
    }
}
