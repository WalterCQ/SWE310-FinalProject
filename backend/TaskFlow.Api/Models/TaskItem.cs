namespace TaskFlow.Api.Models;

public class TaskItem
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public TaskItemStatus Status { get; set; } = TaskItemStatus.Todo;
    public TaskPriority Priority { get; set; } = TaskPriority.Medium;
    public Guid CreatedByUserId { get; set; }
    public Guid? AssigneeId { get; set; }
    public DateTime? DeadlineUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }

    public Project? Project { get; set; }
    public User? CreatedByUser { get; set; }
    public User? Assignee { get; set; }
    public ICollection<TaskComment> Comments { get; set; } = [];
}
