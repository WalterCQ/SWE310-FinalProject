using Microsoft.EntityFrameworkCore;
using System.Text;
using TaskFlow.Api.Data;
using TaskFlow.Api.DTOs.Agent;
using TaskFlow.Api.Helpers;
using TaskFlow.Api.Models;
using TaskFlow.Api.Services.Interfaces;

namespace TaskFlow.Api.Services;

public class AgentService(
    AppDbContext dbContext,
    ICurrentUserService currentUser,
    IPermissionService permissionService) : IAgentService
{
    public async Task<ApiResponse<AgentJobResponse>> CreateJobAsync(CreateAgentJobRequest request, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.GetUserId();
        if (!currentUser.IsAuthenticated() || userId == Guid.Empty)
        {
            return ApiResponse.Fail<AgentJobResponse>("Authentication is required.", StatusCodes.Status401Unauthorized);
        }

        if (!await permissionService.CanUseAiCommand(userId, request.WorkspaceId))
        {
            return ApiResponse.Fail<AgentJobResponse>("Agent access denied for this workspace.", StatusCodes.Status403Forbidden);
        }

        var providerCredentialId = request.ProviderCredentialId;
        var artifactTarget = NormalizeArtifactTarget(request.ArtifactTarget);
        if (!string.IsNullOrWhiteSpace(request.ArtifactTarget) && artifactTarget is null)
        {
            return ApiResponse.Fail<AgentJobResponse>("Artifact target must be docx, pptx, patch, or pull-request.", StatusCodes.Status400BadRequest);
        }

        if (artifactTarget == "pull-request" && !request.GitHubRepositoryId.HasValue)
        {
            return ApiResponse.Fail<AgentJobResponse>("A GitHub repository is required when artifact target is pull-request.", StatusCodes.Status400BadRequest);
        }

        if (providerCredentialId.HasValue)
        {
            var ownsProvider = await dbContext.AiProviderCredentials
                .AnyAsync(credential => credential.Id == providerCredentialId.Value && credential.UserId == userId, cancellationToken);
            if (!ownsProvider)
            {
                return ApiResponse.Fail<AgentJobResponse>("AI provider not found for this user.", StatusCodes.Status404NotFound);
            }
        }

        if (request.ChannelId.HasValue)
        {
            var channelWorkspaceId = await dbContext.Channels
                .Where(channel => channel.Id == request.ChannelId.Value)
                .Select(channel => (Guid?)channel.WorkspaceId)
                .FirstOrDefaultAsync(cancellationToken);
            if (channelWorkspaceId != request.WorkspaceId || !await permissionService.CanAccessChannel(userId, request.ChannelId.Value))
            {
                return ApiResponse.Fail<AgentJobResponse>("Channel not found or access denied.", StatusCodes.Status404NotFound);
            }
        }

        if (request.AttachmentId.HasValue)
        {
            var attachment = await dbContext.ChannelAttachments
                .AsNoTracking()
                .Include(item => item.Message)
                .FirstOrDefaultAsync(item => item.Id == request.AttachmentId.Value, cancellationToken);
            if (attachment is null
                || attachment.WorkspaceId != request.WorkspaceId
                || IsAiGeneratedContent(attachment.Message?.Content)
                || !await permissionService.CanAccessChannel(userId, attachment.ChannelId))
            {
                return ApiResponse.Fail<AgentJobResponse>("Attachment not found or access denied.", StatusCodes.Status404NotFound);
            }
        }

        if (request.GitHubRepositoryId.HasValue)
        {
            var repositoryExists = await dbContext.GitHubRepositoryConnections
                .AnyAsync(repository =>
                    repository.Id == request.GitHubRepositoryId.Value
                    && repository.WorkspaceId == request.WorkspaceId
                    && repository.IsEnabled,
                    cancellationToken);
            if (!repositoryExists)
            {
                return ApiResponse.Fail<AgentJobResponse>("GitHub repository connection not found.", StatusCodes.Status404NotFound);
            }

        }

        var job = new AgentJob
        {
            Id = Guid.NewGuid(),
            WorkspaceId = request.WorkspaceId,
            UserId = userId,
            ProviderCredentialId = providerCredentialId,
            ChannelId = request.ChannelId,
            AttachmentId = request.AttachmentId,
            GitHubRepositoryConnectionId = request.GitHubRepositoryId,
            ArtifactTarget = artifactTarget,
            Goal = request.Goal.Trim(),
            Status = AgentJobStatus.Planning
        };

        dbContext.AgentJobs.Add(job);
        AddEvent(job.Id, userId, AgentEventType.Created, "Agent job created.", $$"""{"status":"{{job.Status}}"}""");
        await dbContext.SaveChangesAsync(cancellationToken);

        return ApiResponse.Created(ToJobResponse(job), "Agent job created.");
    }

    public async Task<ApiResponse<AgentJobResponse>> GetJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await LoadJobAsync(jobId, cancellationToken);
        if (job is null)
        {
            return ApiResponse.Fail<AgentJobResponse>("Agent job not found.", StatusCodes.Status404NotFound);
        }

        var access = await EnsureCanAccessJob(job);
        if (!access.Success)
        {
            return ApiResponse.Fail<AgentJobResponse>(access.Message, access.StatusCode);
        }

        return ApiResponse.Ok(ToJobResponse(job));
    }

    public async Task<ApiResponse<IReadOnlyCollection<AgentEventResponse>>> GetJobEventsAsync(Guid jobId, DateTime? sinceUtc = null, CancellationToken cancellationToken = default)
    {
        var job = await dbContext.AgentJobs.AsNoTracking().FirstOrDefaultAsync(item => item.Id == jobId, cancellationToken);
        if (job is null)
        {
            return ApiResponse.Fail<IReadOnlyCollection<AgentEventResponse>>("Agent job not found.", StatusCodes.Status404NotFound);
        }

        var access = await EnsureCanAccessJob(job);
        if (!access.Success)
        {
            return ApiResponse.Fail<IReadOnlyCollection<AgentEventResponse>>(access.Message, access.StatusCode);
        }

        var query = dbContext.AgentEvents
            .AsNoTracking()
            .Where(agentEvent => agentEvent.AgentJobId == jobId);

        if (sinceUtc.HasValue)
        {
            query = query.Where(agentEvent => agentEvent.CreatedAtUtc > sinceUtc.Value);
        }

        var events = await query
            .OrderBy(agentEvent => agentEvent.CreatedAtUtc)
            .Take(200)
            .ToListAsync(cancellationToken);

        IReadOnlyCollection<AgentEventResponse> response = events.Select(ToEventResponse).ToArray();
        return ApiResponse.Ok(response);
    }

    public Task<ApiResponse<AgentApprovalResponse>> ApproveApprovalAsync(Guid approvalId, DecideAgentApprovalRequest request, CancellationToken cancellationToken = default)
    {
        return DecideApprovalAsync(approvalId, request, approved: true, cancellationToken);
    }

    public Task<ApiResponse<AgentApprovalResponse>> RejectApprovalAsync(Guid approvalId, DecideAgentApprovalRequest request, CancellationToken cancellationToken = default)
    {
        return DecideApprovalAsync(approvalId, request, approved: false, cancellationToken);
    }

    public async Task<ApiResponse<AgentJobResponse>> CancelJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await LoadJobAsync(jobId, cancellationToken);
        if (job is null)
        {
            return ApiResponse.Fail<AgentJobResponse>("Agent job not found.", StatusCodes.Status404NotFound);
        }

        var access = await EnsureCanDecideJob(job);
        if (!access.Success)
        {
            return ApiResponse.Fail<AgentJobResponse>(access.Message, access.StatusCode);
        }

        if (job.Status is AgentJobStatus.Completed or AgentJobStatus.Failed or AgentJobStatus.Canceled)
        {
            return ApiResponse.Fail<AgentJobResponse>("Agent job is already finished.");
        }

        var userId = currentUser.GetUserId();
        job.Status = AgentJobStatus.Canceled;
        job.LockedBy = null;
        job.LockedAtUtc = null;
        job.CompletedAtUtc = DateTime.UtcNow;
        job.UpdatedAtUtc = DateTime.UtcNow;
        AddEvent(job.Id, userId, AgentEventType.Canceled, "Agent job canceled by user.");
        await dbContext.SaveChangesAsync(cancellationToken);

        return ApiResponse.Ok(ToJobResponse(job), "Agent job canceled.");
    }

    public async Task<ApiResponse<AgentArtifactDownload>> DownloadArtifactAsync(Guid artifactId, CancellationToken cancellationToken = default)
    {
        var artifact = await dbContext.AgentArtifacts
            .Include(item => item.AgentJob)
            .Include(item => item.Blob)
            .FirstOrDefaultAsync(item => item.Id == artifactId, cancellationToken);
        if (artifact?.AgentJob is null)
        {
            return ApiResponse.Fail<AgentArtifactDownload>("Agent artifact not found.", StatusCodes.Status404NotFound);
        }

        var access = await EnsureCanAccessJob(artifact.AgentJob);
        if (!access.Success)
        {
            return ApiResponse.Fail<AgentArtifactDownload>(access.Message, access.StatusCode);
        }

        var content = artifact.Blob?.Content;
        if (content is null && artifact.Content is not null)
        {
            content = Encoding.UTF8.GetBytes(artifact.Content);
        }

        if (content is null || content.Length == 0)
        {
            return ApiResponse.Fail<AgentArtifactDownload>("Agent artifact has no downloadable content.", StatusCodes.Status404NotFound);
        }

        return ApiResponse.Ok(new AgentArtifactDownload(artifact.Name, artifact.ContentType, content));
    }

    private async Task<ApiResponse<AgentApprovalResponse>> DecideApprovalAsync(
        Guid approvalId,
        DecideAgentApprovalRequest request,
        bool approved,
        CancellationToken cancellationToken)
    {
        var approval = await dbContext.AgentApprovals
            .Include(item => item.AgentJob)
            .FirstOrDefaultAsync(item => item.Id == approvalId, cancellationToken);
        if (approval?.AgentJob is null)
        {
            return ApiResponse.Fail<AgentApprovalResponse>("Agent approval not found.", StatusCodes.Status404NotFound);
        }

        var access = await EnsureCanDecideJob(approval.AgentJob);
        if (!access.Success)
        {
            return ApiResponse.Fail<AgentApprovalResponse>(access.Message, access.StatusCode);
        }

        if (approval.Status != AgentApprovalStatus.Pending)
        {
            return ApiResponse.Fail<AgentApprovalResponse>("Agent approval was already decided.");
        }

        var userId = currentUser.GetUserId();
        approval.Status = approved ? AgentApprovalStatus.Approved : AgentApprovalStatus.Rejected;
        approval.DecidedByUserId = userId;
        approval.DecidedAtUtc = DateTime.UtcNow;
        approval.DecisionNote = request.Note?.Trim();

        if (approval.ApprovalType.Equals("Plan", StringComparison.OrdinalIgnoreCase))
        {
            approval.AgentJob.Status = approved ? AgentJobStatus.Running : AgentJobStatus.Canceled;
            approval.AgentJob.StartedAtUtc ??= approved ? DateTime.UtcNow : null;
            approval.AgentJob.CompletedAtUtc = approved ? null : DateTime.UtcNow;
        }
        else
        {
            approval.AgentJob.Status = approved ? AgentJobStatus.Running : AgentJobStatus.Paused;
        }

        approval.AgentJob.LockedBy = null;
        approval.AgentJob.LockedAtUtc = null;
        approval.AgentJob.UpdatedAtUtc = DateTime.UtcNow;

        AddEvent(
            approval.AgentJobId,
            userId,
            approved ? AgentEventType.ApprovalApproved : AgentEventType.ApprovalRejected,
            approved ? $"Approval accepted: {approval.Title}" : $"Approval rejected: {approval.Title}");

        await dbContext.SaveChangesAsync(cancellationToken);

        return ApiResponse.Ok(ToApprovalResponse(approval), approved ? "Agent approval accepted." : "Agent approval rejected.");
    }

    private async Task<ApiResponse<bool>> EnsureCanAccessJob(AgentJob job)
    {
        var userId = currentUser.GetUserId();
        if (!currentUser.IsAuthenticated() || userId == Guid.Empty)
        {
            return ApiResponse.Fail<bool>("Authentication is required.", StatusCodes.Status401Unauthorized);
        }

        return await permissionService.CanAccessWorkspace(userId, job.WorkspaceId)
            ? ApiResponse.Ok(true)
            : ApiResponse.Fail<bool>("Agent job not found or access denied.", StatusCodes.Status404NotFound);
    }

    private async Task<ApiResponse<bool>> EnsureCanDecideJob(AgentJob job)
    {
        var access = await EnsureCanAccessJob(job);
        if (!access.Success)
        {
            return access;
        }

        var userId = currentUser.GetUserId();
        if (job.UserId == userId || await permissionService.CanManageWorkspace(userId, job.WorkspaceId))
        {
            return ApiResponse.Ok(true);
        }

        return ApiResponse.Fail<bool>("Only the job owner or workspace manager can decide this approval.", StatusCodes.Status403Forbidden);
    }

    private async Task<AgentJob?> LoadJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        return await dbContext.AgentJobs
            .Include(job => job.Steps)
            .Include(job => job.Approvals)
            .Include(job => job.Artifacts)
            .FirstOrDefaultAsync(job => job.Id == jobId, cancellationToken);
    }

    private void AddEvent(Guid jobId, Guid? actorUserId, AgentEventType eventType, string message, string? dataJson = null)
    {
        dbContext.AgentEvents.Add(new AgentEvent
        {
            Id = Guid.NewGuid(),
            AgentJobId = jobId,
            ActorUserId = actorUserId,
            EventType = eventType,
            Message = message,
            DataJson = dataJson
        });
    }

    private static AgentJobResponse ToJobResponse(AgentJob job)
    {
        return new AgentJobResponse
        {
            Id = job.Id,
            WorkspaceId = job.WorkspaceId,
            UserId = job.UserId,
            ProviderCredentialId = job.ProviderCredentialId,
            ChannelId = job.ChannelId,
            AttachmentId = job.AttachmentId,
            GitHubRepositoryId = job.GitHubRepositoryConnectionId,
            ArtifactTarget = job.ArtifactTarget,
            Goal = job.Goal,
            Status = job.Status,
            PlanJson = job.PlanJson,
            CurrentSubAgent = job.CurrentSubAgent,
            ErrorMessage = job.ErrorMessage,
            CreatedAtUtc = job.CreatedAtUtc,
            UpdatedAtUtc = job.UpdatedAtUtc,
            StartedAtUtc = job.StartedAtUtc,
            CompletedAtUtc = job.CompletedAtUtc,
            Steps = job.Steps.OrderBy(step => step.Sequence).Select(ToStepResponse).ToArray(),
            Approvals = job.Approvals.OrderByDescending(approval => approval.RequestedAtUtc).Select(ToApprovalResponse).ToArray(),
            Artifacts = job.Artifacts.OrderByDescending(artifact => artifact.CreatedAtUtc).Select(ToArtifactResponse).ToArray()
        };
    }

    private static AgentStepResponse ToStepResponse(AgentStep step)
    {
        return new AgentStepResponse
        {
            Id = step.Id,
            Sequence = step.Sequence,
            Name = step.Name,
            SubAgentName = step.SubAgentName,
            Status = step.Status,
            InputJson = step.InputJson,
            OutputJson = step.OutputJson,
            ErrorMessage = step.ErrorMessage,
            CreatedAtUtc = step.CreatedAtUtc,
            StartedAtUtc = step.StartedAtUtc,
            CompletedAtUtc = step.CompletedAtUtc
        };
    }

    private static AgentApprovalResponse ToApprovalResponse(AgentApproval approval)
    {
        return new AgentApprovalResponse
        {
            Id = approval.Id,
            AgentJobId = approval.AgentJobId,
            AgentStepId = approval.AgentStepId,
            ApprovalType = approval.ApprovalType,
            Status = approval.Status,
            Title = approval.Title,
            ActionName = approval.ActionName,
            PreviewJson = approval.PreviewJson,
            PayloadJson = approval.PayloadJson,
            TargetEntityType = approval.TargetEntityType,
            TargetEntityId = approval.TargetEntityId,
            RequestedAtUtc = approval.RequestedAtUtc,
            DecidedByUserId = approval.DecidedByUserId,
            DecidedAtUtc = approval.DecidedAtUtc,
            DecisionNote = approval.DecisionNote,
            ExecutedAtUtc = approval.ExecutedAtUtc,
            ExecutionResultJson = approval.ExecutionResultJson
        };
    }

    private static AgentArtifactResponse ToArtifactResponse(AgentArtifact artifact)
    {
        return new AgentArtifactResponse
        {
            Id = artifact.Id,
            AgentJobId = artifact.AgentJobId,
            AgentStepId = artifact.AgentStepId,
            Kind = artifact.Kind,
            Name = artifact.Name,
            ContentType = artifact.ContentType,
            Content = artifact.Content,
            StorageUrl = artifact.StorageUrl,
            DownloadUrl = $"/api/agent/artifacts/{artifact.Id}/download",
            SizeBytes = artifact.SizeBytes,
            IsDownloadable = artifact.SizeBytes > 0 || artifact.Content is not null,
            CreatedAtUtc = artifact.CreatedAtUtc
        };
    }

    private static AgentEventResponse ToEventResponse(AgentEvent agentEvent)
    {
        return new AgentEventResponse
        {
            Id = agentEvent.Id,
            AgentJobId = agentEvent.AgentJobId,
            ActorUserId = agentEvent.ActorUserId,
            EventType = agentEvent.EventType,
            Message = agentEvent.Message,
            DataJson = agentEvent.DataJson,
            CreatedAtUtc = agentEvent.CreatedAtUtc
        };
    }

    private static string? NormalizeArtifactTarget(string? artifactTarget)
    {
        if (string.IsNullOrWhiteSpace(artifactTarget))
        {
            return null;
        }

        var normalized = artifactTarget.Trim().ToLowerInvariant();
        return normalized is "docx" or "pptx" or "patch" or "pull-request"
            ? normalized
            : null;
    }

    private static bool IsAiGeneratedContent(string? content)
    {
        return !string.IsNullOrWhiteSpace(content)
            && content.StartsWith(IAiCommandService.ChannelAiMessagePrefix, StringComparison.Ordinal);
    }
}
