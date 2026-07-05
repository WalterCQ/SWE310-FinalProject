using System.ComponentModel.DataAnnotations;
using TaskFlow.Api.Helpers;

namespace TaskFlow.Api.DTOs.Messages;

public class CreateMessageRequest
{
    [Required, StringLength(4000), NonWhiteSpace]
    public string Content { get; set; } = string.Empty;
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
}
