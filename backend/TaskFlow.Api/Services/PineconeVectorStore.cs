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
            var index = GetIndex(chunks.First().Embedding.Length);
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

            var response = await GetIndex(queryEmbedding.Length).QueryAsync(new QueryRequest
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
                    var attachmentIdText = GetMetadataString(metadata, "attachmentId", "");
                    var attachmentId = Guid.TryParse(attachmentIdText, out var parsedAttachmentId)
                        ? parsedAttachmentId
                        : (Guid?)null;
                    var text = GetMetadataString(metadata, "text", "");
                    var source = string.IsNullOrWhiteSpace(chunkIndex)
                        ? $"{sourceType}: {sourceLabel} in #{channelName}"
                        : $"{sourceType}: {sourceLabel} chunk {chunkIndex} in #{channelName}";
                    return new PineconeKnowledgeMatch(source, text, match.Score, attachmentId);
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
            foreach (var index in GetIndexes())
            {
                try
                {
                    await index.DeleteAsync(new DeleteRequest
                    {
                        Namespace = ResolveNamespace(workspaceId),
                        Filter = new Metadata
                        {
                            ["attachmentId"] = new Metadata { ["$eq"] = attachmentId.ToString("D") }
                        }
                    });
                }
                catch (Exception ex) when (IsNamespaceNotFound(ex))
                {
                    // The namespace is already absent in this index, so the delete is complete.
                }
            }
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
            foreach (var index in GetIndexes())
            {
                try
                {
                    await index.DeleteNamespaceAsync(ResolveNamespace(workspaceId));
                }
                catch (Exception ex) when (IsNamespaceNotFound(ex))
                {
                    // The namespace is already absent in this index, so the delete is complete.
                }
            }
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

    private IndexClient GetIndex(int? embeddingDimension = null)
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

        var indexName = ResolveIndexName(embeddingDimension);
        if (string.IsNullOrWhiteSpace(indexName))
        {
            throw new PineconeVectorStoreException("Pinecone index name is not configured.");
        }

        return new PineconeClient(apiKey).Index(indexName);
    }

    private IReadOnlyCollection<IndexClient> GetIndexes()
    {
        var apiKey = configuration["Pinecone:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new PineconeVectorStoreException("Pinecone API key is not configured.");
        }

        var client = new PineconeClient(apiKey);
        return ResolveConfiguredIndexNames()
            .Select(indexName => client.Index(indexName))
            .ToArray();
    }

    private string? ResolveIndexName(int? embeddingDimension)
    {
        if (embeddingDimension == 1536)
        {
            return configuration["Pinecone:IndexName1536"]
                ?? configuration["Pinecone:GitHubIndexName"]
                ?? configuration["Pinecone:IndexName"];
        }

        if (embeddingDimension == 3072)
        {
            return configuration["Pinecone:IndexName3072"]
                ?? configuration["Pinecone:GeminiIndexName"]
                ?? configuration["Pinecone:IndexName"];
        }

        return configuration["Pinecone:IndexName"];
    }

    private IReadOnlyCollection<string> ResolveConfiguredIndexNames()
    {
        return new[]
            {
                configuration["Pinecone:IndexName"],
                configuration["Pinecone:IndexName1536"],
                configuration["Pinecone:GitHubIndexName"],
                configuration["Pinecone:IndexName3072"],
                configuration["Pinecone:GeminiIndexName"]
            }
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsNamespaceNotFound(Exception exception)
    {
        if (exception is PineconeApiException pineconeApiException)
        {
            var body = Convert.ToString(pineconeApiException.Body);
            return pineconeApiException.StatusCode == 5
                && (body?.Contains("namespace", StringComparison.OrdinalIgnoreCase) ?? false);
        }

        return exception.InnerException is not null && IsNamespaceNotFound(exception.InnerException);
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

        return value.IsT0 ? value.AsT0 : value.ToString() ?? fallback;
    }

    private static string TrimTo(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength].Trim();
    }
}
