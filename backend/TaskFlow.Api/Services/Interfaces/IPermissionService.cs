namespace TaskFlow.Api.Services.Interfaces;

public interface IPermissionService
{
    Task<bool> CanAccessWorkspace(Guid userId, Guid workspaceId);
    Task<bool> CanManageWorkspace(Guid userId, Guid workspaceId);
    Task<bool> CanAccessChannel(Guid userId, Guid channelId);
    Task<bool> CanSendMessage(Guid userId, Guid channelId);
    Task<bool> CanAccessProject(Guid userId, Guid projectId);
    Task<bool> CanManageProject(Guid userId, Guid projectId);
    Task<bool> CanCreateTask(Guid userId, Guid projectId);
    Task<bool> CanUpdateTask(Guid userId, Guid taskId);
    Task<bool> CanUseAiCommand(Guid userId, Guid workspaceId);
}
