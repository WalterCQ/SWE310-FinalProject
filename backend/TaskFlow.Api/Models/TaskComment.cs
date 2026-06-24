namespace TaskFlow.Api.Models;

public class TaskComment
{
    public Guid Id { get; set; }
    public Guid TaskItemId { get; set; }
    public Guid AuthorId { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public TaskItem? TaskItem { get; set; }
    public User? Author { get; set; }
}
