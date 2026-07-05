using TaskFlow.Api.DTOs.GitHub;
using TaskFlow.Api.Helpers;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Services.Interfaces;

public interface IGitHubRepositoryService
{
    Task<ApiResponse<GitHubSetupResponse>> GetSetupAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyCollection<GitHubRepositoryResponse>>> ListRepositoriesAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyCollection<GitHubRepositoryResponse>>> CompleteInstallationAsync(Guid workspaceId, CompleteGitHubInstallationRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<GitHubRepositoryResponse>> ConnectRepositoryAsync(Guid workspaceId, ConnectGitHubRepositoryRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<bool>> DeleteRepositoryAsync(Guid workspaceId, Guid repositoryId, CancellationToken cancellationToken = default);
    Task<ApiResponse<bool>> HandleWebhookAsync(IHeaderDictionary headers, string body, CancellationToken cancellationToken = default);
    Task<GitHubInstallationAccessToken> CreateInstallationAccessTokenAsync(long installationId, CancellationToken cancellationToken = default);
    Task<GitHubPullRequestResult> CreatePullRequestAsync(GitHubRepositoryConnection repository, string headBranch, string title, string body, CancellationToken cancellationToken = default);
}
