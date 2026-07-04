using TaskFlow.Api.DTOs.Agent;
using TaskFlow.Api.Helpers;

namespace TaskFlow.Api.Services.Interfaces;

public interface IAiProviderService
{
    Task<ApiResponse<AiProviderResponse>> SaveProviderAsync(SaveAiProviderRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyCollection<AiProviderResponse>>> GetProvidersAsync(CancellationToken cancellationToken = default);
}
