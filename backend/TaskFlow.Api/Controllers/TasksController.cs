using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using TaskFlow.Api.DTOs.Tasks;
using TaskFlow.Api.Helpers;
using TaskFlow.Api.Services.Interfaces;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class TasksController(ITaskService taskService) : ControllerBase
{
    [HttpGet("projects/{projectId:guid}/tasks")]
    public async Task<ActionResult> GetProjectTasks(Guid projectId)
    {
        return this.ToActionResult(await taskService.GetProjectTasksAsync(projectId));
    }

    [HttpPost("projects/{projectId:guid}/tasks")]
    public async Task<ActionResult> CreateTask(Guid projectId, CreateTaskRequest request)
    {
        return this.ToActionResult(await taskService.CreateTaskAsync(projectId, request));
    }

    [HttpGet("tasks/{taskId:guid}")]
    public async Task<ActionResult> GetTask(Guid taskId)
    {
        return this.ToActionResult(await taskService.GetTaskAsync(taskId));
    }

    [HttpPut("tasks/{taskId:guid}")]
    public async Task<ActionResult> UpdateTask(Guid taskId, UpdateTaskRequest request)
    {
        return this.ToActionResult(await taskService.UpdateTaskAsync(taskId, request));
    }

    [HttpDelete("tasks/{taskId:guid}")]
    public async Task<ActionResult> DeleteTask(Guid taskId)
    {
        return this.ToActionResult(await taskService.DeleteTaskAsync(taskId));
    }

    [HttpPut("tasks/{taskId:guid}/status")]
    public async Task<ActionResult> UpdateTaskStatus(Guid taskId, UpdateTaskStatusRequest request)
    {
        return this.ToActionResult(await taskService.UpdateTaskStatusAsync(taskId, request));
    }

    [HttpPut("tasks/{taskId:guid}/assign")]
    public async Task<ActionResult> AssignTask(Guid taskId, AssignTaskRequest request)
    {
        return this.ToActionResult(await taskService.AssignTaskAsync(taskId, request));
    }

    [HttpPut("tasks/{taskId:guid}/deadline")]
    public async Task<ActionResult> SetTaskDeadline(Guid taskId, SetTaskDeadlineRequest request)
    {
        return this.ToActionResult(await taskService.SetTaskDeadlineAsync(taskId, request));
    }
}
