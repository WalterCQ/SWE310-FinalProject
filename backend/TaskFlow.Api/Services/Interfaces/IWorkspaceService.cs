using TaskFlow.Api.DTOs.Workspaces;
using TaskFlow.Api.Helpers;

namespace TaskFlow.Api.Services.Interfaces;

public interface IWorkspaceService
{
    Task<ApiResponse<IEnumerable<WorkspaceResponse>>> GetWorkspacesAsync();
    Task<ApiResponse<WorkspaceResponse>> CreateWorkspaceAsync(CreateWorkspaceRequest request);
    Task<ApiResponse<WorkspaceResponse>> GetWorkspaceAsync(Guid workspaceId);
    Task<ApiResponse<WorkspaceResponse>> UpdateWorkspaceAsync(Guid workspaceId, UpdateWorkspaceRequest request);
    Task<ApiResponse<IEnumerable<WorkspaceMemberResponse>>> GetWorkspaceMembersAsync(Guid workspaceId);
    Task<ApiResponse<WorkspaceMemberResponse>> AddWorkspaceMemberAsync(Guid workspaceId, AddWorkspaceMemberRequest request);
    Task<ApiResponse<WorkspaceMemberResponse>> UpdateWorkspaceMemberRoleAsync(Guid workspaceId, Guid userId, UpdateWorkspaceMemberRoleRequest request);
    Task<ApiResponse<bool>> RemoveWorkspaceMemberAsync(Guid workspaceId, Guid userId);
}
