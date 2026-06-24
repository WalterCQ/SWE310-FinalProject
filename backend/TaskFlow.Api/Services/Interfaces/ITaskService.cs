using TaskFlow.Api.DTOs.Tasks;
using TaskFlow.Api.Helpers;

namespace TaskFlow.Api.Services.Interfaces;

public interface ITaskService
{
    Task<ApiResponse<IEnumerable<TaskResponse>>> GetProjectTasksAsync(Guid projectId);
    Task<ApiResponse<TaskResponse>> CreateTaskAsync(Guid projectId, CreateTaskRequest request);
    Task<ApiResponse<TaskResponse>> GetTaskAsync(Guid taskId);
    Task<ApiResponse<TaskResponse>> UpdateTaskAsync(Guid taskId, UpdateTaskRequest request);
    Task<ApiResponse<bool>> DeleteTaskAsync(Guid taskId);
    Task<ApiResponse<TaskResponse>> UpdateTaskStatusAsync(Guid taskId, UpdateTaskStatusRequest request);
    Task<ApiResponse<TaskResponse>> AssignTaskAsync(Guid taskId, AssignTaskRequest request);
    Task<ApiResponse<TaskResponse>> SetTaskDeadlineAsync(Guid taskId, SetTaskDeadlineRequest request);
}
