namespace TaskFlow.Api.Models;

public enum GlobalRole
{
    Admin,
    User
}

public enum WorkspaceRole
{
    Owner,
    Admin,
    Member
}

public enum ProjectRole
{
    ProjectManager,
    Contributor,
    Viewer
}

public enum ProjectStatus
{
    Planned,
    Active,
    Completed,
    Archived
}

public enum TaskItemStatus
{
    Todo,
    InProgress,
    Blocked,
    Done
}

public enum TaskPriority
{
    Low,
    Medium,
    High
}

public enum NotificationType
{
    General,
    Message,
    Task,
    Reminder,
    Ai
}
