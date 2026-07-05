using System.ComponentModel.DataAnnotations;
using TaskFlow.Api.Helpers;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.DTOs.Admin;

public class AdminOverviewResponse
{
    public IReadOnlyCollection<AdminMetricResponse> Metrics { get; set; } = [];
    public IReadOnlyCollection<AdminSecurityEvidenceResponse> SecurityEvidence { get; set; } = [];
}

public class AdminMetricResponse
{
    public string Label { get; set; } = string.Empty;
    public int Value { get; set; }
    public string HelpText { get; set; } = string.Empty;
}

public class AdminSecurityEvidenceResponse
{
    public string Area { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;
}

public class AdminUserResponse
{
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string GlobalRole { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public int WorkspaceCount { get; set; }
    public int ProjectCount { get; set; }
    public int ChannelCount { get; set; }
}

public class AdminUpdateUserRequest
{
    [Required, MaxLength(120), NonWhiteSpace]
    public string Name { get; set; } = string.Empty;

    [Required, DefinedEnum]
    public GlobalRole? GlobalRole { get; set; }
}

public class AdminResetUserPasswordRequest
{
    [Required, MinLength(6), NonWhiteSpace]
    public string NewPassword { get; set; } = string.Empty;
}
