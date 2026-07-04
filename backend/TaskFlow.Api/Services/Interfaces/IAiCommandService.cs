using TaskFlow.Api.DTOs.AI;
using TaskFlow.Api.Helpers;

namespace TaskFlow.Api.Services.Interfaces;

public interface IAiCommandService
{
    Task<ApiResponse<AiResponse>> ExecuteCommandAsync(AiCommandRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AiResponse>> SummarizeChannelAsync(AiChannelSummaryRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AiResponse>> SummarizeProjectAsync(AiProjectSummaryRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AiResponse>> AnalyzeProjectRiskAsync(AiRiskAnalysisRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AiResponse>> GenerateTasksFromMessageAsync(AiGenerateTasksFromMessageRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AiResponse>> AskWorkspaceKnowledgeAsync(AiWorkspaceQuestionRequest request, CancellationToken cancellationToken = default);
}
