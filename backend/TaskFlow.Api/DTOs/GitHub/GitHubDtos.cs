using System.ComponentModel.DataAnnotations;

namespace TaskFlow.Api.DTOs.GitHub;

public class ConnectGitHubRepositoryRequest
{
    [Range(1, long.MaxValue)]
    public long InstallationId { get; set; }

    public long? RepositoryId { get; set; }

    [Required, StringLength(120)]
    public string Owner { get; set; } = string.Empty;

    [Required, StringLength(160)]
    public string Name { get; set; } = string.Empty;

    [StringLength(160)]
    public string? DefaultBranch { get; set; }

    [StringLength(500)]
    public string? ValidationCommand { get; set; }
}

public class GitHubRepositoryResponse
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public long InstallationId { get; set; }
    public long? RepositoryId { get; set; }
    public string Owner { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string DefaultBranch { get; set; } = string.Empty;
    public string? ValidationCommand { get; set; }
    public bool IsEnabled { get; set; }
    public string PermissionStatus { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? LastSyncedAtUtc { get; set; }
}

public class GitHubSetupResponse
{
    public string AppSlug { get; set; } = string.Empty;
    public string InstallUrl { get; set; } = string.Empty;
}

public class CompleteGitHubInstallationRequest
{
    [Range(1, long.MaxValue)]
    public long InstallationId { get; set; }
}

public record GitHubInstallationAccessToken(string Token, DateTimeOffset ExpiresAt);

public record GitHubPullRequestResult(int Number, string Url, string HeadBranch);
