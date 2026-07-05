using TaskFlow.Api.Helpers;

namespace TaskFlow.Api.Services.Interfaces;

public interface IAiContextService
{
    Task<ApiResponse<AiChannelContext>> BuildChannelContextAsync(
        Guid channelId,
        string query,
        Guid? attachmentId = null,
        CancellationToken cancellationToken = default);
}

public sealed record AiChannelContext(
    Guid WorkspaceId,
    Guid ChannelId,
    string ChannelName,
    string RecentMessages,
    string RetrievedContext,
    IReadOnlyCollection<string> Sources,
    IReadOnlyCollection<string> ContextLines);
