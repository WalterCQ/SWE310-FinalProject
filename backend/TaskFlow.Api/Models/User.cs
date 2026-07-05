namespace TaskFlow.Api.Models;

public class User
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set ;} = string.Empty;
    public GlobalRole GlobalRole { get; set; } = GlobalRole.Member;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<WorkspaceMember> WorkspaceMemberships { get; set; } = [];
    public ICollection<ChannelMember> ChannelMemberships { get; set; } = [];
    public ICollection<ProjectMember> ProjectMemberships { get; set; } = [];
    public ICollection<Message> Messages { get; set; } = [];
    public ICollection<Notification> Notifications { get; set; } = [];
}
