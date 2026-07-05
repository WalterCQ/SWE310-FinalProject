namespace TaskFlow.Api.Models;

public class ProjectMember
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid UserId { get; set; }
    public ProjectRole RoleInProject { get; set; } = ProjectRole.Manager;
    public DateTime JoinedAtUtc { get; set; } = DateTime.UtcNow;

    public Project? Project { get; set; }
    public User? User { get; set; }
}
