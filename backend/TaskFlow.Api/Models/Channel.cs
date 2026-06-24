namespace TaskFlow.Api.Models;

public class Channel
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsPrivate { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Workspace? Workspace { get; set; }
    public User? CreatedByUser { get; set; }
    public ICollection<ChannelMember> Members { get; set; } = [];
    public ICollection<Message> Messages { get; set; } = [];
}
