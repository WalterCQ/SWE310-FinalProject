using System.ComponentModel.DataAnnotations;

namespace TaskFlow.Api.DTOs.Workspaces;

public class CreateWorkspaceRequest
{
    [Required, StringLength(160)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }
}

public class UpdateWorkspaceRequest
{
    [Required, StringLength(160)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }
}

public class WorkspaceResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid CreatedByUserId { get; set; }
    public int MemberCount { get; set; }
    public int ChannelCount { get; set; }
    public int ProjectCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
