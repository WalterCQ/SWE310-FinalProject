using TaskFlow.Api.DTOs.Projects;
using TaskFlow.Api.Helpers;

namespace TaskFlow.Api.Services.Interfaces;

public interface IProjectService
{
    Task<ApiResponse<IEnumerable<ProjectResponse>>> GetWorkspaceProjectsAsync(Guid workspaceId);
    Task<ApiResponse<ProjectResponse>> CreateProjectAsync(Guid workspaceId, CreateProjectRequest request);
    Task<ApiResponse<ProjectResponse>> GetProjectAsync(Guid projectId);
    Task<ApiResponse<ProjectResponse>> UpdateProjectAsync(Guid projectId, UpdateProjectRequest request);
    Task<ApiResponse<bool>> DeleteProjectAsync(Guid projectId);
    Task<ApiResponse<IEnumerable<ProjectMemberResponse>>> GetProjectMembersAsync(Guid projectId);
    Task<ApiResponse<ProjectMemberResponse>> AddProjectMemberAsync(Guid projectId, AddProjectMemberRequest request);
    Task<ApiResponse<ProjectMemberResponse>> UpdateProjectMemberRoleAsync(Guid projectId, Guid userId, UpdateProjectMemberRoleRequest request);
    Task<ApiResponse<bool>> RemoveProjectMemberAsync(Guid projectId, Guid userId);
}
