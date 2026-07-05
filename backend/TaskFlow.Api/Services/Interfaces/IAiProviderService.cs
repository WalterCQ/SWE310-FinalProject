using TaskFlow.Api.DTOs.Agent;
using TaskFlow.Api.Helpers;

namespace TaskFlow.Api.Services.Interfaces;

public interface IAiProviderService
{
    Task<ApiResponse<AiProviderResponse>> SaveProviderAsync(SaveAiProviderRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyCollection<AiProviderResponse>>> GetProvidersAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<WorkspaceAiProviderResponse>> GetWorkspaceProviderAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<ApiResponse<WorkspaceAiProviderResponse>> SaveWorkspaceProviderAsync(Guid workspaceId, SaveWorkspaceAiProviderRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<bool>> DeleteWorkspaceProviderAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<ApiResponse<AiProviderRuntime>> ResolveWorkspaceProviderAsync(Guid workspaceId, CancellationToken cancellationToken = default);
}

public class AiProviderRuntime
{
    public string ProviderName { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public bool SupportsToolCalls { get; set; }
}
