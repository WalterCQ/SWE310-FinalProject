using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.DTOs.Tasks;
using TaskFlow.Api.Helpers;
using TaskFlow.Api.Models;
using TaskFlow.Api.Services.Interfaces;

namespace TaskFlow.Api.Services;

public class TaskService(
    AppDbContext dbContext,
    ICurrentUserService currentUser,
    IPermissionService permissionService) : ITaskService
{
    public async Task<ApiResponse<IEnumerable<TaskResponse>>> GetProjectTasksAsync(Guid projectId)
    {
        var userId = currentUser.GetUserId();
        if (!await permissionService.CanAccessProject(userId, projectId))
        {
            return ApiResponse.Fail<IEnumerable<TaskResponse>>("Project not found or access denied.", StatusCodes.Status404NotFound);
        }

        var tasks = await TaskQuery()
            .Where(task => task.ProjectId == projectId)
            .OrderBy(task => task.Status)
            .ThenBy(task => task.DeadlineUtc)
            .ToListAsync();

        return ApiResponse.Ok(tasks.Select(task => task.ToResponse()));
    }

    public async Task<ApiResponse<TaskResponse>> CreateTaskAsync(Guid projectId, CreateTaskRequest request)
    {
        var userId = currentUser.GetUserId();
        if (!await permissionService.CanCreateTask(userId, projectId))
        {
            return ApiResponse.Fail<TaskResponse>("You do not have permission to create tasks in this project.", StatusCodes.Status403Forbidden);
        }

        if (request.AssigneeId.HasValue && !await IsProjectMember(projectId, request.AssigneeId.Value))
        {
            return ApiResponse.Fail<TaskResponse>("Assignee must be a project member.");
        }

        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            Priority = request.Priority,
            AssigneeId = request.AssigneeId,
            DeadlineUtc = request.DeadlineUtc,
            CreatedByUserId = userId
        };

        dbContext.TaskItems.Add(task);
        await dbContext.SaveChangesAsync();

        var savedTask = await TaskQuery().FirstAsync(item => item.Id == task.Id);
        return ApiResponse.Created(savedTask.ToResponse(), "Task created.");
    }

    public async Task<ApiResponse<TaskResponse>> GetTaskAsync(Guid taskId)
    {
        var task = await TaskQuery().FirstOrDefaultAsync(item => item.Id == taskId);
        if (task is null)
        {
            return ApiResponse.Fail<TaskResponse>("Task not found.", StatusCodes.Status404NotFound);
        }

        var userId = currentUser.GetUserId();
        if (!await permissionService.CanAccessProject(userId, task.ProjectId))
        {
            return ApiResponse.Fail<TaskResponse>("Task not found or access denied.", StatusCodes.Status404NotFound);
        }

        return ApiResponse.Ok(task.ToResponse());
    }

    public async Task<ApiResponse<TaskResponse>> UpdateTaskAsync(Guid taskId, UpdateTaskRequest request)
    {
        var task = await TaskQuery().FirstOrDefaultAsync(item => item.Id == taskId);
        if (task is null)
        {
            return ApiResponse.Fail<TaskResponse>("Task not found.", StatusCodes.Status404NotFound);
        }

        var userId = currentUser.GetUserId();
        if (!await permissionService.CanUpdateTask(userId, taskId))
        {
            return ApiResponse.Fail<TaskResponse>("You do not have permission to update this task.", StatusCodes.Status403Forbidden);
        }

        if (request.AssigneeId.HasValue && !await IsProjectMember(task.ProjectId, request.AssigneeId.Value))
        {
            return ApiResponse.Fail<TaskResponse>("Assignee must be a project member.");
        }

        task.Title = request.Title.Trim();
        task.Description = request.Description?.Trim();
        task.Priority = request.Priority;
        task.AssigneeId = request.AssigneeId;
        task.DeadlineUtc = request.DeadlineUtc;
        task.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();

        return ApiResponse.Ok(task.ToResponse(), "Task updated.");
    }

    public async Task<ApiResponse<bool>> DeleteTaskAsync(Guid taskId)
    {
        var task = await dbContext.TaskItems.FirstOrDefaultAsync(item => item.Id == taskId);
        if (task is null)
        {
            return ApiResponse.Fail<bool>("Task not found.", StatusCodes.Status404NotFound);
        }

        var userId = currentUser.GetUserId();
        if (!await permissionService.CanUpdateTask(userId, taskId))
        {
            return ApiResponse.Fail<bool>("You do not have permission to delete this task.", StatusCodes.Status403Forbidden);
        }

        dbContext.TaskItems.Remove(task);
        await dbContext.SaveChangesAsync();

        return ApiResponse.NoData("Task deleted.");
    }

    public async Task<ApiResponse<TaskResponse>> UpdateTaskStatusAsync(Guid taskId, UpdateTaskStatusRequest request)
    {
        var task = await TaskQuery().FirstOrDefaultAsync(item => item.Id == taskId);
        if (task is null)
        {
            return ApiResponse.Fail<TaskResponse>("Task not found.", StatusCodes.Status404NotFound);
        }

        var userId = currentUser.GetUserId();
        if (!await permissionService.CanUpdateTask(userId, taskId))
        {
            return ApiResponse.Fail<TaskResponse>("You do not have permission to update this task.", StatusCodes.Status403Forbidden);
        }

        task.Status = request.Status;
        task.CompletedAtUtc = request.Status == TaskItemStatus.Done ? DateTime.UtcNow : null;
        task.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();

        return ApiResponse.Ok(task.ToResponse(), "Task status updated.");
    }

    public async Task<ApiResponse<TaskResponse>> AssignTaskAsync(Guid taskId, AssignTaskRequest request)
    {
        var task = await TaskQuery().FirstOrDefaultAsync(item => item.Id == taskId);
        if (task is null)
        {
            return ApiResponse.Fail<TaskResponse>("Task not found.", StatusCodes.Status404NotFound);
        }

        var userId = currentUser.GetUserId();
        if (!await permissionService.CanUpdateTask(userId, taskId))
        {
            return ApiResponse.Fail<TaskResponse>("You do not have permission to assign this task.", StatusCodes.Status403Forbidden);
        }

        if (request.AssigneeId.HasValue && !await IsProjectMember(task.ProjectId, request.AssigneeId.Value))
        {
            return ApiResponse.Fail<TaskResponse>("Assignee must be a project member.");
        }

        task.AssigneeId = request.AssigneeId;
        task.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();

        return ApiResponse.Ok(task.ToResponse(), "Task assignment updated.");
    }

    public async Task<ApiResponse<TaskResponse>> SetTaskDeadlineAsync(Guid taskId, SetTaskDeadlineRequest request)
    {
        var task = await TaskQuery().FirstOrDefaultAsync(item => item.Id == taskId);
        if (task is null)
        {
            return ApiResponse.Fail<TaskResponse>("Task not found.", StatusCodes.Status404NotFound);
        }

        var userId = currentUser.GetUserId();
        if (!await permissionService.CanUpdateTask(userId, taskId))
        {
            return ApiResponse.Fail<TaskResponse>("You do not have permission to update this task deadline.", StatusCodes.Status403Forbidden);
        }

        task.DeadlineUtc = request.DeadlineUtc;
        task.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();

        return ApiResponse.Ok(task.ToResponse(), "Task deadline updated.");
    }

    private IQueryable<TaskItem> TaskQuery()
    {
        return dbContext.TaskItems
            .Include(task => task.Assignee);
    }

    private async Task<bool> IsProjectMember(Guid projectId, Guid userId)
    {
        return await dbContext.ProjectMembers.AnyAsync(member =>
            member.ProjectId == projectId && member.UserId == userId);
    }
}
