namespace TaskFlow.Api.DTOs.Dashboard;

public class WorkspaceDashboardResponse
{
    public Guid WorkspaceId { get; set; }
    public int ProjectCount { get; set; }
    public int ChannelCount { get; set; }
    public int TaskCount { get; set; }
    public int CompletedTaskCount { get; set; }
    public int OverdueTaskCount { get; set; }
    public IReadOnlyDictionary<string, int> TasksByStatus { get; set; } = new Dictionary<string, int>();
    public IReadOnlyCollection<string> RecentActivities { get; set; } = [];
}

public class ProjectDashboardResponse
{
    public Guid ProjectId { get; set; }
    public int TaskCount { get; set; }
    public int CompletedTaskCount { get; set; }
    public int OverdueTaskCount { get; set; }
    public double CompletionRate { get; set; }
    public DateTime? DeadlineUtc { get; set; }
    public IReadOnlyDictionary<string, int> TasksByStatus { get; set; } = new Dictionary<string, int>();
}
