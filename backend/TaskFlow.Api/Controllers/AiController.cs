using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using TaskFlow.Api.DTOs.AI;
using TaskFlow.Api.Helpers;
using TaskFlow.Api.Services.Interfaces;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api/ai")]
[Authorize]
public class AiController(IAiCommandService aiCommandService) : ControllerBase
{
    [HttpPost("command")]
    public async Task<ActionResult> ExecuteCommand(AiCommandRequest request, CancellationToken cancellationToken)
    {
        return this.ToActionResult(await aiCommandService.ExecuteCommandAsync(request, cancellationToken));
    }

    [HttpPost("channel-summary")]
    public async Task<ActionResult> SummarizeChannel(AiChannelSummaryRequest request, CancellationToken cancellationToken)
    {
        return this.ToActionResult(await aiCommandService.SummarizeChannelAsync(request, cancellationToken));
    }

    [HttpPost("project-summary")]
    public async Task<ActionResult> SummarizeProject(AiProjectSummaryRequest request, CancellationToken cancellationToken)
    {
        return this.ToActionResult(await aiCommandService.SummarizeProjectAsync(request, cancellationToken));
    }

    [HttpPost("risk-analysis")]
    public async Task<ActionResult> AnalyzeProjectRisk(AiRiskAnalysisRequest request, CancellationToken cancellationToken)
    {
        return this.ToActionResult(await aiCommandService.AnalyzeProjectRiskAsync(request, cancellationToken));
    }

    [HttpPost("generate-tasks-from-message")]
    public async Task<ActionResult> GenerateTasksFromMessage(AiGenerateTasksFromMessageRequest request, CancellationToken cancellationToken)
    {
        return this.ToActionResult(await aiCommandService.GenerateTasksFromMessageAsync(request, cancellationToken));
    }

    [HttpPost("workspace-question")]
    public async Task<ActionResult> AskWorkspaceKnowledge(AiWorkspaceQuestionRequest request, CancellationToken cancellationToken)
    {
        return this.ToActionResult(await aiCommandService.AskWorkspaceKnowledgeAsync(request, cancellationToken));
    }
}
