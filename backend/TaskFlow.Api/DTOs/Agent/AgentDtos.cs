using System.ComponentModel.DataAnnotations;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.DTOs.Agent;

public class CreateAgentJobRequest
{
    public Guid WorkspaceId { get; set; }

    [Required, StringLength(4000)]
    public string Goal { get; set; } = string.Empty;

    public Guid? ProviderCredentialId { get; set; }
}

public class DecideAgentApprovalRequest
{
    [StringLength(1000)]
    public string? Note { get; set; }
}

public class AgentJobResponse
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid UserId { get; set; }
    public Guid? ProviderCredentialId { get; set; }
    public string Goal { get; set; } = string.Empty;
    public AgentJobStatus Status { get; set; }
    public string? PlanJson { get; set; }
    public string? CurrentSubAgent { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public IReadOnlyCollection<AgentStepResponse> Steps { get; set; } = [];
    public IReadOnlyCollection<AgentApprovalResponse> Approvals { get; set; } = [];
    public IReadOnlyCollection<AgentArtifactResponse> Artifacts { get; set; } = [];
}

public class AgentStepResponse
{
    public Guid Id { get; set; }
    public int Sequence { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SubAgentName { get; set; } = string.Empty;
    public AgentStepStatus Status { get; set; }
    public string? InputJson { get; set; }
    public string? OutputJson { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}

public class AgentApprovalResponse
{
    public Guid Id { get; set; }
    public Guid AgentJobId { get; set; }
    public Guid? AgentStepId { get; set; }
    public string ApprovalType { get; set; } = string.Empty;
    public AgentApprovalStatus Status { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? ActionName { get; set; }
    public string PreviewJson { get; set; } = "{}";
    public string PayloadJson { get; set; } = "{}";
    public string? TargetEntityType { get; set; }
    public Guid? TargetEntityId { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public string? DecisionNote { get; set; }
    public DateTime? ExecutedAtUtc { get; set; }
    public string? ExecutionResultJson { get; set; }
}

public class AgentArtifactResponse
{
    public Guid Id { get; set; }
    public Guid AgentJobId { get; set; }
    public Guid? AgentStepId { get; set; }
    public AgentArtifactKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string? Content { get; set; }
    public string? StorageUrl { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class AgentEventResponse
{
    public Guid Id { get; set; }
    public Guid AgentJobId { get; set; }
    public Guid? ActorUserId { get; set; }
    public AgentEventType EventType { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? DataJson { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class SaveAiProviderRequest
{
    [Required, StringLength(80)]
    public string ProviderName { get; set; } = string.Empty;

    [StringLength(500)]
    public string? BaseUrl { get; set; }

    [Required, StringLength(120)]
    public string Model { get; set; } = string.Empty;

    [Required, StringLength(4000)]
    public string ApiKey { get; set; } = string.Empty;

    public bool? SupportsToolCalls { get; set; }
    public bool IsDefault { get; set; } = true;
}

public class AiProviderResponse
{
    public Guid Id { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
    public string Model { get; set; } = string.Empty;
    public bool SupportsToolCalls { get; set; }
    public bool IsDefault { get; set; }
    public bool HasApiKey { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public class SaveWorkspaceAiProviderRequest
{
    [Required, StringLength(80)]
    public string ProviderName { get; set; } = string.Empty;

    [StringLength(500)]
    public string? BaseUrl { get; set; }

    [Required, StringLength(120)]
    public string Model { get; set; } = string.Empty;

    [StringLength(4000)]
    public string? ApiKey { get; set; }

    public bool? SupportsToolCalls { get; set; }
}

public class WorkspaceAiProviderResponse
{
    public Guid WorkspaceId { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
    public string Model { get; set; } = string.Empty;
    public bool SupportsToolCalls { get; set; }
    public bool HasApiKey { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime KeyLastUpdatedAtUtc { get; set; }
}
