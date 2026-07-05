namespace TaskFlow.Api.Models;

public enum GlobalRole
{
    Administrator,
    Member
}

public enum WorkspaceRole
{
    Administrator,
    Manager,
    Member
}

public enum ProjectRole
{
    Administrator,
    Manager,
    Member
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

public enum AgentJobStatus
{
    Planning,
    AwaitingApproval,
    Running,
    NeedsApproval,
    Paused,
    Completed,
    Failed,
    Canceled
}

public enum AgentStepStatus
{
    Pending,
    Running,
    WaitingForApproval,
    Completed,
    Failed,
    Skipped
}

public enum AgentSubJobStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Canceled
}

public enum AgentApprovalStatus
{
    Pending,
    Approved,
    Rejected,
    Expired
}

public enum AgentArtifactKind
{
    Summary,
    CodePatch,
    Deck,
    Report,
    TaskFlowAction,
    Other
}

public enum AgentEventType
{
    Created,
    StatusChanged,
    Planning,
    ApprovalRequested,
    ApprovalApproved,
    ApprovalRejected,
    StepStarted,
    StepCompleted,
    ArtifactCreated,
    ActionExecuted,
    Error,
    Canceled
}
