using System.ComponentModel.DataAnnotations;
using TaskFlow.Api.Helpers;

namespace TaskFlow.Api.DTOs.AI;

public class AiCommandRequest
{
    [NonEmptyGuid]
    public Guid WorkspaceId { get; set; }

    [Required, StringLength(4000), NonWhiteSpace]
    public string Command { get; set; } = string.Empty;
}

public class AiChannelSummaryRequest
{
    [NonEmptyGuid]
    public Guid ChannelId { get; set; }
}

public class AiProjectSummaryRequest
{
    [NonEmptyGuid]
    public Guid ProjectId { get; set; }
}

public class AiRiskAnalysisRequest
{
    [NonEmptyGuid]
    public Guid ProjectId { get; set; }
}

public class AiGenerateTasksFromMessageRequest
{
    [NonEmptyGuid]
    public Guid ProjectId { get; set; }
    public Guid? MessageId { get; set; }

    [StringLength(4000)]
    public string? MessageContent { get; set; }
}

public class AiWorkspaceQuestionRequest
{
    [NonEmptyGuid]
    public Guid WorkspaceId { get; set; }

    [Required, StringLength(1000), NonWhiteSpace]
    public string Question { get; set; } = string.Empty;
}

public class AiResponse
{
    public string Result { get; set; } = string.Empty;
    public bool UsedLlm { get; set; }
    public IReadOnlyCollection<string> Sources { get; set; } = [];
}

public class AiChannelCommandRequest
{
    [Required, StringLength(4000), NonWhiteSpace]
    public string Command { get; set; } = string.Empty;

    public Guid? AttachmentId { get; set; }
}

public class AiChannelCommandResponse
{
    public string Result { get; set; } = string.Empty;
    public string ArtifactType { get; set; } = "answer";
    public bool UsedLlm { get; set; }
    public Guid? AgentJobId { get; set; }
    public bool RequiresApproval { get; set; }
    public IReadOnlyCollection<string> Sources { get; set; } = [];
    public IReadOnlyCollection<string> SuggestedTasks { get; set; } = [];
    public Guid? CreatedTaskId { get; set; }
    public string? CreatedTaskTitle { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class ChannelAttachmentResponse
{
    public Guid Id { get; set; }
    public Guid ChannelId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Summary { get; set; } = string.Empty;
    public bool IsAiIndexed { get; set; }
    public string DownloadUrl { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
