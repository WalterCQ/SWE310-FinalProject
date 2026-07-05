using TaskFlow.Api.Models;

namespace TaskFlow.Api.Services.Interfaces;

public interface IPineconeVectorStore
{
    Task UpsertChunksAsync(
        Guid workspaceId,
        Guid channelId,
        Guid attachmentId,
        IReadOnlyCollection<PineconeKnowledgeChunk> chunks,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PineconeKnowledgeMatch>> SearchAsync(
        Guid workspaceId,
        Guid channelId,
        Guid? attachmentId,
        float[] queryEmbedding,
        int topK,
        CancellationToken cancellationToken = default);

    Task DeleteByAttachmentAsync(Guid workspaceId, Guid attachmentId, CancellationToken cancellationToken = default);

    Task DeleteByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);
}

public sealed record PineconeKnowledgeChunk(
    Guid ChunkId,
    string SourceType,
    string SourceLabel,
    string ChannelName,
    string Content,
    int ChunkIndex,
    float[] Embedding);

public sealed record PineconeKnowledgeMatch(
    string Source,
    string Text,
    double? Score);

public class PineconeVectorStoreException(string message, Exception? innerException = null)
    : Exception(message, innerException);
