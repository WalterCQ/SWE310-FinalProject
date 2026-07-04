using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.DTOs.Agent;
using TaskFlow.Api.Helpers;
using TaskFlow.Api.Services.Interfaces;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api/agent")]
[Authorize]
public class AgentController(IAgentService agentService) : ControllerBase
{
    [HttpPost("jobs")]
    public async Task<ActionResult> CreateJob(CreateAgentJobRequest request, CancellationToken cancellationToken)
    {
        return this.ToActionResult(await agentService.CreateJobAsync(request, cancellationToken));
    }

    [HttpGet("jobs/{jobId:guid}")]
    public async Task<ActionResult> GetJob(Guid jobId, CancellationToken cancellationToken)
    {
        return this.ToActionResult(await agentService.GetJobAsync(jobId, cancellationToken));
    }

    [HttpGet("jobs/{jobId:guid}/events")]
    public async Task<ActionResult> GetJobEvents(Guid jobId, [FromQuery] DateTime? sinceUtc, CancellationToken cancellationToken)
    {
        return this.ToActionResult(await agentService.GetJobEventsAsync(jobId, sinceUtc, cancellationToken));
    }

    [HttpPost("jobs/{jobId:guid}/cancel")]
    public async Task<ActionResult> CancelJob(Guid jobId, CancellationToken cancellationToken)
    {
        return this.ToActionResult(await agentService.CancelJobAsync(jobId, cancellationToken));
    }

    [HttpPost("approvals/{approvalId:guid}/approve")]
    public async Task<ActionResult> ApproveApproval(Guid approvalId, DecideAgentApprovalRequest request, CancellationToken cancellationToken)
    {
        return this.ToActionResult(await agentService.ApproveApprovalAsync(approvalId, request, cancellationToken));
    }

    [HttpPost("approvals/{approvalId:guid}/reject")]
    public async Task<ActionResult> RejectApproval(Guid approvalId, DecideAgentApprovalRequest request, CancellationToken cancellationToken)
    {
        return this.ToActionResult(await agentService.RejectApprovalAsync(approvalId, request, cancellationToken));
    }
}
