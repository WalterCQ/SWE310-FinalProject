using Pinecone;
using TaskFlow.Api.Services.Interfaces;

namespace TaskFlow.Api.Services;

public class PineconeVectorStore(IConfiguration configuration) : IPineconeVectorStore
{
    private const int MaxMetadataTextLength = 2500;
    private const int UpsertBatchSize = 100;

    public async Task UpsertChunksAsync(
        Guid workspaceId,
        Guid channelId,
        Guid attachmentId,
        IReadOnlyCollection<PineconeKnowledgeChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        if (chunks.Count == 0)
        {
            return;
        }

        try
        {
            var index = GetIndex();
            var @namespace = ResolveNamespace(workspaceId);
            var vectors = chunks.Select(chunk => new Vector
            {
                Id = chunk.ChunkId.ToString("D"),
                Values = chunk.Embedding,
                Metadata = new Metadata
                {
                    ["workspaceId"] = workspaceId.ToString("D"),
                    ["channelId"] = channelId.ToString("D"),
                    ["attachmentId"] = attachmentId.ToString("D"),
                    ["sourceType"] = chunk.SourceType,
                    ["sourceLabel"] = chunk.SourceLabel,
                    ["channelName"] = chunk.ChannelName,
                    ["chunkIndex"] = chunk.ChunkIndex,
                    ["text"] = TrimTo(chunk.Content, MaxMetadataTextLength)
                }
            }).ToArray();

            foreach (var batch in vectors.Chunk(UpsertBatchSize))
            {
                await index.UpsertAsync(new UpsertRequest
                {
                    Namespace = @namespace,
                    Vectors = batch.ToList()
                });
            }
        }
        catch (PineconeVectorStoreException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new PineconeVectorStoreException(
                "Pinecone upsert failed. Check the index name, API key, and embedding dimension.",
                ex);
        }
    }

    public async Task<IReadOnlyList<PineconeKnowledgeMatch>> SearchAsync(
        Guid workspaceId,
        Guid channelId,
        Guid? attachmentId,
        float[] queryEmbedding,
        int topK,
        CancellationToken cancellationToken = default)
    {
        if (queryEmbedding.Length == 0)
        {
            return [];
        }

        try
        {
            var filter = new Metadata
            {
                ["channelId"] = new Metadata { ["$eq"] = channelId.ToString("D") }
            };

            if (attachmentId.HasValue)
            {
                filter["attachmentId"] = new Metadata { ["$eq"] = attachmentId.Value.ToString("D") };
            }

            var response = await GetIndex().QueryAsync(new QueryRequest
            {
                Namespace = ResolveNamespace(workspaceId),
                Vector = queryEmbedding,
                TopK = (uint)Math.Max(1, topK),
                IncludeMetadata = true,
                Filter = filter
            });

            return (response.Matches ?? [])
                .Where(match => match.Metadata is not null)
                .Select(match =>
                {
                    var metadata = match.Metadata!;
                    var sourceType = GetMetadataString(metadata, "sourceType", "file");
                    var sourceLabel = GetMetadataString(metadata, "sourceLabel", "attachment");
                    var channelName = GetMetadataString(metadata, "channelName", "channel");
                    var chunkIndex = GetMetadataString(metadata, "chunkIndex", "");
                    var text = GetMetadataString(metadata, "text", "");
                    var source = string.IsNullOrWhiteSpace(chunkIndex)
                        ? $"{sourceType}: {sourceLabel} in #{channelName}"
                        : $"{sourceType}: {sourceLabel} chunk {chunkIndex} in #{channelName}";
                    return new PineconeKnowledgeMatch(source, text, match.Score);
                })
                .Where(match => !string.IsNullOrWhiteSpace(match.Text))
                .ToArray();
        }
        catch (PineconeVectorStoreException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new PineconeVectorStoreException(
                "Pinecone search failed. Check the index name, API key, metadata filters, and embedding dimension.",
                ex);
        }
    }

    public async Task DeleteByAttachmentAsync(Guid workspaceId, Guid attachmentId, CancellationToken cancellationToken = default)
    {
        try
        {
            await GetIndex().DeleteAsync(new DeleteRequest
            {
                Namespace = ResolveNamespace(workspaceId),
                Filter = new Metadata
                {
                    ["attachmentId"] = new Metadata { ["$eq"] = attachmentId.ToString("D") }
                }
            });
        }
        catch (PineconeVectorStoreException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new PineconeVectorStoreException("Pinecone attachment cleanup failed.", ex);
        }
    }

    public async Task DeleteByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        try
        {
            await GetIndex().DeleteNamespaceAsync(ResolveNamespace(workspaceId));
        }
        catch (PineconeVectorStoreException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new PineconeVectorStoreException("Pinecone workspace namespace cleanup failed.", ex);
        }
    }

    private IndexClient GetIndex()
    {
        if (!configuration.GetValue("Pinecone:Enabled", false))
        {
            throw new PineconeVectorStoreException("Pinecone RAG is disabled. Set Pinecone:Enabled=true before using channel AI attachments.");
        }

        var apiKey = configuration["Pinecone:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new PineconeVectorStoreException("Pinecone API key is not configured.");
        }

        var indexName = configuration["Pinecone:IndexName"];
        if (string.IsNullOrWhiteSpace(indexName))
        {
            throw new PineconeVectorStoreException("Pinecone index name is not configured.");
        }

        return new PineconeClient(apiKey).Index(indexName);
    }

    private string ResolveNamespace(Guid workspaceId)
    {
        var prefix = configuration["Pinecone:NamespacePrefix"];
        prefix = string.IsNullOrWhiteSpace(prefix) ? "taskflow" : prefix.Trim();
        return $"{prefix}-{workspaceId:D}";
    }

    private static string GetMetadataString(Metadata metadata, string key, string fallback)
    {
        if (!metadata.TryGetValue(key, out var value) || value is null)
        {
            return fallback;
        }

        return value.ToString() ?? fallback;
    }

    private static string TrimTo(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength].Trim();
    }
}
