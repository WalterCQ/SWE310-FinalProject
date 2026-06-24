namespace TaskFlow.Api.Models;

public class Workspace
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public User? CreatedByUser { get; set; }
    public ICollection<WorkspaceMember> Members { get; set; } = [];
    public ICollection<Channel> Channels { get; set; } = [];
    public ICollection<Project> Projects { get; set; } = [];
    public ICollection<Notification> Notifications { get; set; } = [];
    public ICollection<ActivityLog> ActivityLogs { get; set; } = [];
}
