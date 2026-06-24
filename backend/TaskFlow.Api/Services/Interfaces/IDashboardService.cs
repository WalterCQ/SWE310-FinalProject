using TaskFlow.Api.DTOs.Dashboard;
using TaskFlow.Api.Helpers;

namespace TaskFlow.Api.Services.Interfaces;

public interface IDashboardService
{
    Task<ApiResponse<WorkspaceDashboardResponse>> GetWorkspaceDashboardAsync(Guid workspaceId);
    Task<ApiResponse<ProjectDashboardResponse>> GetProjectDashboardAsync(Guid projectId);
}
