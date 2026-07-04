using System.ComponentModel.DataAnnotations;

namespace TaskFlow.Api.DTOs.AI;

public class AiCommandRequest
{
    public Guid WorkspaceId { get; set; }

    [Required, StringLength(4000)]
    public string Command { get; set; } = string.Empty;
}

public class AiChannelSummaryRequest
{
    public Guid ChannelId { get; set; }
}

public class AiProjectSummaryRequest
{
    public Guid ProjectId { get; set; }
}

public class AiRiskAnalysisRequest
{
    public Guid ProjectId { get; set; }
}

public class AiGenerateTasksFromMessageRequest
{
    public Guid ProjectId { get; set; }
    public Guid? MessageId { get; set; }

    [StringLength(4000)]
    public string? MessageContent { get; set; }
}

public class AiWorkspaceQuestionRequest
{
    public Guid WorkspaceId { get; set; }

    [Required, StringLength(1000)]
    public string Question { get; set; } = string.Empty;
}

public class AiResponse
{
    public string Result { get; set; } = string.Empty;
    public bool UsedLlm { get; set; }
    public IReadOnlyCollection<string> Sources { get; set; } = [];
}
