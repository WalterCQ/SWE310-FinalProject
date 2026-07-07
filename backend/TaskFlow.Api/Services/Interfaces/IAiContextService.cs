using TaskFlow.Api.Helpers;

namespace TaskFlow.Api.Services.Interfaces;

public interface IAiContextService
{
    Task<ApiResponse<AiChannelContext>> BuildChannelContextAsync(
        Guid channelId,
        string query,
        IReadOnlyCollection<Guid>? attachmentIds = null,
        CancellationToken cancellationToken = default,
        bool channelOnly = false);
}

public sealed record AiChannelContext(
    Guid WorkspaceId,
    Guid ChannelId,
    string ChannelName,
    string RecentMessages,
    string RetrievedContext,
    IReadOnlyCollection<string> Sources,
    IReadOnlyCollection<string> ContextLines);
