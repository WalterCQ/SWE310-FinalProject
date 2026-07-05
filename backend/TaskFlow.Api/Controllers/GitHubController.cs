using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.DTOs.GitHub;
using TaskFlow.Api.Helpers;
using TaskFlow.Api.Services.Interfaces;

namespace TaskFlow.Api.Controllers;

[ApiController]
public class GitHubController(IGitHubRepositoryService gitHubRepositoryService) : ControllerBase
{
    [HttpGet("api/workspaces/{workspaceId:guid}/github/setup")]
    [Authorize]
    public async Task<ActionResult> GetSetup(Guid workspaceId, CancellationToken cancellationToken)
    {
        return this.ToActionResult(await gitHubRepositoryService.GetSetupAsync(workspaceId, cancellationToken));
    }

    [HttpGet("api/workspaces/{workspaceId:guid}/github/repositories")]
    [Authorize]
    public async Task<ActionResult> ListRepositories(Guid workspaceId, CancellationToken cancellationToken)
    {
        return this.ToActionResult(await gitHubRepositoryService.ListRepositoriesAsync(workspaceId, cancellationToken));
    }

    [HttpPost("api/workspaces/{workspaceId:guid}/github/installations/complete")]
    [Authorize]
    public async Task<ActionResult> CompleteInstallation(
        Guid workspaceId,
        CompleteGitHubInstallationRequest request,
        CancellationToken cancellationToken)
    {
        return this.ToActionResult(await gitHubRepositoryService.CompleteInstallationAsync(workspaceId, request, cancellationToken));
    }

    [HttpPost("api/workspaces/{workspaceId:guid}/github/repositories")]
    [Authorize]
    public async Task<ActionResult> ConnectRepository(
        Guid workspaceId,
        ConnectGitHubRepositoryRequest request,
        CancellationToken cancellationToken)
    {
        return this.ToActionResult(await gitHubRepositoryService.ConnectRepositoryAsync(workspaceId, request, cancellationToken));
    }

    [HttpDelete("api/workspaces/{workspaceId:guid}/github/repositories/{repositoryId:guid}")]
    [Authorize]
    public async Task<ActionResult> DeleteRepository(Guid workspaceId, Guid repositoryId, CancellationToken cancellationToken)
    {
        return this.ToActionResult(await gitHubRepositoryService.DeleteRepositoryAsync(workspaceId, repositoryId, cancellationToken));
    }

    [HttpPost("api/github/webhook")]
    [AllowAnonymous]
    public async Task<ActionResult> Webhook(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(cancellationToken);
        return this.ToActionResult(await gitHubRepositoryService.HandleWebhookAsync(Request.Headers, body, cancellationToken));
    }
}
