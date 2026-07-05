using System.ComponentModel.DataAnnotations;
using TaskFlow.Api.Helpers;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.DTOs.Projects;

public class CreateProjectRequest
{
    [Required, StringLength(160), NonWhiteSpace]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    public DateTime? DeadlineUtc { get; set; }
}

public class UpdateProjectRequest
{
    [Required, StringLength(160), NonWhiteSpace]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    [DefinedEnum]
    public ProjectStatus Status { get; set; } = ProjectStatus.Active;
    public DateTime? DeadlineUtc { get; set; }
}

public class ProjectResponse
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ProjectStatus Status { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string CreatedByUserName { get; set; } = string.Empty;
    public DateTime? DeadlineUtc { get; set; }
    public int MemberCount { get; set; }
    public int TaskCount { get; set; }
    public int CompletedTaskCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public class AddProjectMemberRequest
{
    [Required, EmailAddress, StringLength(256), NonWhiteSpace]
    public string Email { get; set; } = string.Empty;

    [DefinedEnum]
    public ProjectRole RoleInProject { get; set; } = ProjectRole.Manager;
}

public class UpdateProjectMemberRoleRequest
{
    [DefinedEnum]
    public ProjectRole RoleInProject { get; set; } = ProjectRole.Manager;
}

public class ProjectMemberResponse
{
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public ProjectRole RoleInProject { get; set; }
    public DateTime JoinedAtUtc { get; set; }
}
