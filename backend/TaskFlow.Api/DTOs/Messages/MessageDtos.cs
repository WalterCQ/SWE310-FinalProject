using System.ComponentModel.DataAnnotations;
using TaskFlow.Api.DTOs.AI;
using TaskFlow.Api.Helpers;

namespace TaskFlow.Api.DTOs.Messages;

public class CreateMessageRequest
{
    [Required, StringLength(4000), NonWhiteSpace]
    public string Content { get; set; } = string.Empty;
}

public class CreateMessageAttachmentRequest
{
    [StringLength(4000)]
    public string? Content { get; set; }

    [Required]
    public IFormFile? File { get; set; }
}

public class UpdateMessageRequest
{
    [Required, StringLength(4000), NonWhiteSpace]
    public string Content { get; set; } = string.Empty;
}

public class MessageResponse
{
    public Guid Id { get; set; }
    public Guid ChannelId { get; set; }
    public Guid SenderId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? EditedAtUtc { get; set; }
    public string? AiArtifactType { get; set; }
    public Guid? AgentJobId { get; set; }
    public bool RequiresApproval { get; set; }
    public IReadOnlyCollection<string> Sources { get; set; } = [];
    public IReadOnlyCollection<string> SuggestedTasks { get; set; } = [];
    public Guid? CreatedTaskId { get; set; }
    public string? CreatedTaskTitle { get; set; }
    public IReadOnlyCollection<ChannelAttachmentResponse> Attachments { get; set; } = [];
}
