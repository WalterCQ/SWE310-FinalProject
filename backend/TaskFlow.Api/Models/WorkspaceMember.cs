namespace TaskFlow.Api.Models;

public class WorkspaceMember
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid UserId { get; set; }
    public WorkspaceRole Role { get; set; } = WorkspaceRole.Member;
    public DateTime JoinedAtUtc { get; set; } = DateTime.UtcNow;

    public Workspace? Workspace { get; set; }
    public User? User { get; set; }
}
