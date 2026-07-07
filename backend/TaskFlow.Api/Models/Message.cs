namespace TaskFlow.Api.Models;

public class Message
{
    public Guid Id { get; set; }
    public Guid ChannelId { get; set; }
    public Guid SenderId { get; set; }
    public string Content { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? EditedAtUtc { get; set; }
    public string? AiArtifactType { get; set; }
    public string? AiSourcesJson { get; set; }
    public string? AiSuggestedTasksJson { get; set; }
    public Guid? AiAgentJobId { get; set; }
    public bool AiRequiresApproval { get; set; }
    public Guid? AiCreatedTaskId { get; set; }
    public string? AiCreatedTaskTitle { get; set; }

    public Channel? Channel { get; set; }
    public User? Sender { get; set; }
    public ICollection<ChannelAttachment> Attachments { get; set; } = [];
}
