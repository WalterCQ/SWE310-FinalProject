using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.DTOs.Agent;
using TaskFlow.Api.Helpers;
using TaskFlow.Api.Services.Interfaces;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api/ai/providers")]
[Authorize]
public class AiProvidersController(IAiProviderService aiProviderService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> GetProviders(CancellationToken cancellationToken)
    {
        return this.ToActionResult(await aiProviderService.GetProvidersAsync(cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult> SaveProvider(SaveAiProviderRequest request, CancellationToken cancellationToken)
    {
        return this.ToActionResult(await aiProviderService.SaveProviderAsync(request, cancellationToken));
    }
}
