namespace TaskFlow.Api.Models;

public class AiProviderCredential
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
    public string Model { get; set; } = string.Empty;
    public string EncryptedApiKey { get; set; } = string.Empty;
    public bool SupportsToolCalls { get; set; }
    public bool IsDefault { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
}

public class WorkspaceAiProviderCredential
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
    public string Model { get; set; } = string.Empty;
    public string EncryptedApiKey { get; set; } = string.Empty;
    public bool SupportsToolCalls { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime KeyLastUpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public Workspace? Workspace { get; set; }
    public User? CreatedByUser { get; set; }
    public User? UpdatedByUser { get; set; }
}

public class AgentJob
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid UserId { get; set; }
    public Guid? ProviderCredentialId { get; set; }
    public Guid? ChannelId { get; set; }
    public Guid? AttachmentId { get; set; }
    public Guid? GitHubRepositoryConnectionId { get; set; }
    public string? ArtifactTarget { get; set; }
    public string Goal { get; set; } = string.Empty;
    public AgentJobStatus Status { get; set; } = AgentJobStatus.Planning;
    public string? PlanJson { get; set; }
    public string? CurrentSubAgent { get; set; }
    public string? ErrorMessage { get; set; }
    public string? LockedBy { get; set; }
    public DateTime? LockedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public Workspace? Workspace { get; set; }
    public User? User { get; set; }
    public AiProviderCredential? ProviderCredential { get; set; }
    public GitHubRepositoryConnection? GitHubRepositoryConnection { get; set; }
    public ICollection<AgentStep> Steps { get; set; } = [];
    public ICollection<AgentSubJob> SubJobs { get; set; } = [];
    public ICollection<AgentApproval> Approvals { get; set; } = [];
    public ICollection<AgentArtifact> Artifacts { get; set; } = [];
    public ICollection<AgentEvent> Events { get; set; } = [];
}

public class AgentStep
{
    public Guid Id { get; set; }
    public Guid AgentJobId { get; set; }
    public int Sequence { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SubAgentName { get; set; } = string.Empty;
    public AgentStepStatus Status { get; set; } = AgentStepStatus.Pending;
    public string? InputJson { get; set; }
    public string? OutputJson { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public AgentJob? AgentJob { get; set; }
}

public class AgentSubJob
{
    public Guid Id { get; set; }
    public Guid AgentJobId { get; set; }
    public string SubAgentName { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public AgentSubJobStatus Status { get; set; } = AgentSubJobStatus.Pending;
    public string? ResultJson { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public AgentJob? AgentJob { get; set; }
}

public class AgentApproval
{
    public Guid Id { get; set; }
    public Guid AgentJobId { get; set; }
    public Guid? AgentStepId { get; set; }
    public string ApprovalType { get; set; } = string.Empty;
    public AgentApprovalStatus Status { get; set; } = AgentApprovalStatus.Pending;
    public string Title { get; set; } = string.Empty;
    public string? ActionName { get; set; }
    public string PreviewJson { get; set; } = "{}";
    public string PayloadJson { get; set; } = "{}";
    public string? TargetEntityType { get; set; }
    public Guid? TargetEntityId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? DecidedByUserId { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public string? DecisionNote { get; set; }
    public DateTime? ExecutedAtUtc { get; set; }
    public string? ExecutionResultJson { get; set; }

    public AgentJob? AgentJob { get; set; }
    public AgentStep? AgentStep { get; set; }
}

public class AgentArtifact
{
    public Guid Id { get; set; }
    public Guid AgentJobId { get; set; }
    public Guid? AgentStepId { get; set; }
    public AgentArtifactKind Kind { get; set; } = AgentArtifactKind.Other;
    public string Name { get; set; } = string.Empty;
    public string ContentType { get; set; } = "text/markdown";
    public string? Content { get; set; }
    public string? StorageUrl { get; set; }
    public long SizeBytes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public AgentJob? AgentJob { get; set; }
    public AgentStep? AgentStep { get; set; }
    public AgentArtifactBlob? Blob { get; set; }
}

public class AgentArtifactBlob
{
    public Guid AgentArtifactId { get; set; }
    public byte[] Content { get; set; } = [];

    public AgentArtifact? Artifact { get; set; }
}

public class GitHubRepositoryConnection
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public long InstallationId { get; set; }
    public long? RepositoryId { get; set; }
    public string Owner { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string DefaultBranch { get; set; } = "main";
    public string? ValidationCommand { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string PermissionStatus { get; set; } = "connected";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastSyncedAtUtc { get; set; }

    public Workspace? Workspace { get; set; }
    public User? CreatedByUser { get; set; }
}

public class AgentEvent
{
    public Guid Id { get; set; }
    public Guid AgentJobId { get; set; }
    public Guid? ActorUserId { get; set; }
    public AgentEventType EventType { get; set; } = AgentEventType.Created;
    public string Message { get; set; } = string.Empty;
    public string? DataJson { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public AgentJob? AgentJob { get; set; }
}
