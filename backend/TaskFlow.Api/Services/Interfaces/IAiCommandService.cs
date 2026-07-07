using TaskFlow.Api.DTOs.AI;
using TaskFlow.Api.Helpers;

namespace TaskFlow.Api.Services.Interfaces;

public interface IAiCommandService
{
    const string ChannelAiMessagePrefix = "[[TASKFLOW_AI]]";

    Task<ApiResponse<AiResponse>> ExecuteCommandAsync(AiCommandRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AiResponse>> SummarizeChannelAsync(AiChannelSummaryRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AiResponse>> SummarizeProjectAsync(AiProjectSummaryRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AiResponse>> AnalyzeProjectRiskAsync(AiRiskAnalysisRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AiResponse>> GenerateTasksFromMessageAsync(AiGenerateTasksFromMessageRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AiResponse>> AskWorkspaceKnowledgeAsync(AiWorkspaceQuestionRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AiChannelCommandResponse>> HandleChannelMentionAsync(Guid channelId, AiChannelCommandRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AiChannelCommandResponse>> ShareChannelResultAsync(Guid channelId, AiChannelShareRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<ChannelAttachmentResponse>> IndexChannelAttachmentAsync(Guid channelId, IFormFile file, CancellationToken cancellationToken = default);
    Task<ApiResponse<ChannelAttachmentResponse>> IndexExistingChannelAttachmentAsync(Guid attachmentId, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyCollection<ChannelAttachmentResponse>>> ListChannelAttachmentsAsync(Guid channelId, CancellationToken cancellationToken = default);
}
