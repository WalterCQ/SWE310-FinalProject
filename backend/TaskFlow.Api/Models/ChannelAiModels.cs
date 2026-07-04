namespace TaskFlow.Api.Models;

public class ChannelAttachment
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ChannelId { get; set; }
    public Guid UploadedByUserId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string ExtractedText { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Workspace? Workspace { get; set; }
    public Channel? Channel { get; set; }
    public User? UploadedByUser { get; set; }
    public ICollection<ChannelKnowledgeChunk> KnowledgeChunks { get; set; } = [];
}

public class ChannelKnowledgeChunk
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ChannelId { get; set; }
    public Guid? AttachmentId { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string SourceLabel { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? EmbeddingJson { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Workspace? Workspace { get; set; }
    public Channel? Channel { get; set; }
    public ChannelAttachment? Attachment { get; set; }
}
