using TaskFlow.Api.DTOs.Agent;
using TaskFlow.Api.Helpers;

namespace TaskFlow.Api.Services.Interfaces;

public interface IAgentService
{
    Task<ApiResponse<AgentJobResponse>> CreateJobAsync(CreateAgentJobRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AgentJobResponse>> GetJobAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyCollection<AgentEventResponse>>> GetJobEventsAsync(Guid jobId, DateTime? sinceUtc = null, CancellationToken cancellationToken = default);
    Task<ApiResponse<AgentApprovalResponse>> ApproveApprovalAsync(Guid approvalId, DecideAgentApprovalRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AgentApprovalResponse>> RejectApprovalAsync(Guid approvalId, DecideAgentApprovalRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AgentJobResponse>> CancelJobAsync(Guid jobId, CancellationToken cancellationToken = default);
}
