using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.DTOs.Agent;
using TaskFlow.Api.Helpers;
using TaskFlow.Api.Services.Interfaces;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class AiProvidersController(IAiProviderService aiProviderService) : ControllerBase
{
    [HttpGet("ai/providers")]
    public async Task<ActionResult> GetProviders(CancellationToken cancellationToken)
    {
        return this.ToActionResult(await aiProviderService.GetProvidersAsync(cancellationToken));
    }

    [HttpPost("ai/providers")]
    public async Task<ActionResult> SaveProvider(SaveAiProviderRequest request, CancellationToken cancellationToken)
    {
        return this.ToActionResult(await aiProviderService.SaveProviderAsync(request, cancellationToken));
    }

    [HttpGet("workspaces/{workspaceId:guid}/ai/provider")]
    public async Task<ActionResult> GetWorkspaceProvider(Guid workspaceId, CancellationToken cancellationToken)
    {
        return this.ToActionResult(await aiProviderService.GetWorkspaceProviderAsync(workspaceId, cancellationToken));
    }

    [HttpPut("workspaces/{workspaceId:guid}/ai/provider")]
    public async Task<ActionResult> SaveWorkspaceProvider(
        Guid workspaceId,
        SaveWorkspaceAiProviderRequest request,
        CancellationToken cancellationToken)
    {
        return this.ToActionResult(await aiProviderService.SaveWorkspaceProviderAsync(workspaceId, request, cancellationToken));
    }

    [HttpDelete("workspaces/{workspaceId:guid}/ai/provider")]
    public async Task<ActionResult> DeleteWorkspaceProvider(Guid workspaceId, CancellationToken cancellationToken)
    {
        return this.ToActionResult(await aiProviderService.DeleteWorkspaceProviderAsync(workspaceId, cancellationToken));
    }
}
