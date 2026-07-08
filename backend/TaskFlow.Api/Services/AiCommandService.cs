using Microsoft.SemanticKernel;
using Microsoft.EntityFrameworkCore;
using OpenAI;
using System.ClientModel;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TaskFlow.Api.Data;
using TaskFlow.Api.DTOs.AI;
using TaskFlow.Api.Helpers;
using TaskFlow.Api.Models;
using TaskFlow.Api.Plugins;
using TaskFlow.Api.Services.Interfaces;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace TaskFlow.Api.Services;

public class AiCommandService(
    IConfiguration configuration,
    AppDbContext dbContext,
    ICurrentUserService currentUser,
    IPermissionService permissionService,
    IChannelService channelService,
    IDashboardService dashboardService,
    IAiProviderService aiProviderService,
    IAiContextService aiContextService,
    CollaborationAiPlugin collaborationAiPlugin,
    IPineconeVectorStore pineconeVectorStore,
    IHttpClientFactory httpClientFactory) : IAiCommandService
{
    private const int MaxUploadBytes = 20_000_000;
    private const int MaxExtractedTextLength = 60_000;
    private const int MaxChunksPerAttachment = 24;
    private const int MaxDirectAttachmentChunks = 12;
    private const int MaxPdfImagesToSummarize = 3;
    private const int MaxImageSummaryTokens = 1800;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ApiResponse<AiResponse>> ExecuteCommandAsync(AiCommandRequest request, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.GetUserId();
        if (!await permissionService.CanUseAiCommand(userId, request.WorkspaceId))
        {
            return ApiResponse.Fail<AiResponse>("AI command access denied.", StatusCodes.Status403Forbidden);
        }

        var providerResult = await aiProviderService.ResolveWorkspaceProviderAsync(request.WorkspaceId, cancellationToken);
        if (!providerResult.Success || providerResult.Data is null)
        {
            return ToProviderFailure<AiResponse>(providerResult);
        }

        var prompt = $"""
            You are the TaskFlow Connect assistant. Use the available collaboration plugin functions only when the user asks to read or modify real app data.
            Workspace id: {request.WorkspaceId}
            User command: {request.Command}
            """;

        return await AskLlmOrFail(providerResult.Data, prompt, cancellationToken);
    }

    public async Task<ApiResponse<AiResponse>> SummarizeChannelAsync(AiChannelSummaryRequest request, CancellationToken cancellationToken = default)
    {
        var messagesResult = await channelService.GetChannelMessagesAsync(request.ChannelId);
        if (!messagesResult.Success || messagesResult.Data is null)
        {
            return ApiResponse.Fail<AiResponse>(messagesResult.Message, messagesResult.StatusCode, messagesResult.Errors);
        }

        var workspaceId = await GetChannelWorkspaceIdAsync(request.ChannelId, cancellationToken);
        if (!workspaceId.HasValue)
        {
            return ApiResponse.Fail<AiResponse>("Channel not found.", StatusCodes.Status404NotFound);
        }

        var providerResult = await aiProviderService.ResolveWorkspaceProviderAsync(workspaceId.Value, cancellationToken);
        if (!providerResult.Success || providerResult.Data is null)
        {
            return ToProviderFailure<AiResponse>(providerResult);
        }

        var humanMessages = messagesResult.Data
            .Where(message => !IsAiGeneratedContent(message.Content))
            .TakeLast(50)
            .ToArray();
        var context = string.Join(Environment.NewLine, humanMessages.Select(message => $"{message.SenderName}: {message.Content}"));
        var prompt = string.IsNullOrWhiteSpace(context)
            ? "Summarize this channel discussion clearly for a project team. The channel has no human-authored messages yet."
            : $"Summarize this channel discussion clearly for a project team:\n{context}";

        return await AskLlmOrFail(providerResult.Data, prompt, cancellationToken);
    }

    public async Task<ApiResponse<AiResponse>> SummarizeProjectAsync(AiProjectSummaryRequest request, CancellationToken cancellationToken = default)
    {
        var dashboardResult = await dashboardService.GetProjectDashboardAsync(request.ProjectId);
        if (!dashboardResult.Success || dashboardResult.Data is null)
        {
            return ApiResponse.Fail<AiResponse>(dashboardResult.Message, dashboardResult.StatusCode, dashboardResult.Errors);
        }

        var workspaceId = await GetProjectWorkspaceIdAsync(request.ProjectId, cancellationToken);
        if (!workspaceId.HasValue)
        {
            return ApiResponse.Fail<AiResponse>("Project not found.", StatusCodes.Status404NotFound);
        }

        var providerResult = await aiProviderService.ResolveWorkspaceProviderAsync(workspaceId.Value, cancellationToken);
        if (!providerResult.Success || providerResult.Data is null)
        {
            return ToProviderFailure<AiResponse>(providerResult);
        }

        var dashboard = dashboardResult.Data;
        var context = $"Project has {dashboard.TaskCount} tasks, {dashboard.CompletedTaskCount} completed, {dashboard.OverdueTaskCount} overdue, and {dashboard.CompletionRate}% completion.";
        return await AskLlmOrFail(providerResult.Data, $"Summarize this project dashboard for stakeholders:\n{context}", cancellationToken);
    }

    public async Task<ApiResponse<AiResponse>> AnalyzeProjectRiskAsync(AiRiskAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        var dashboardResult = await dashboardService.GetProjectDashboardAsync(request.ProjectId);
        if (!dashboardResult.Success || dashboardResult.Data is null)
        {
            return ApiResponse.Fail<AiResponse>(dashboardResult.Message, dashboardResult.StatusCode, dashboardResult.Errors);
        }

        var workspaceId = await GetProjectWorkspaceIdAsync(request.ProjectId, cancellationToken);
        if (!workspaceId.HasValue)
        {
            return ApiResponse.Fail<AiResponse>("Project not found.", StatusCodes.Status404NotFound);
        }

        var providerResult = await aiProviderService.ResolveWorkspaceProviderAsync(workspaceId.Value, cancellationToken);
        if (!providerResult.Success || providerResult.Data is null)
        {
            return ToProviderFailure<AiResponse>(providerResult);
        }

        var dashboard = dashboardResult.Data;
        var context = dashboard.OverdueTaskCount > 0
            ? $"Risk detected: {dashboard.OverdueTaskCount} overdue tasks and {dashboard.CompletionRate}% completion."
            : $"No overdue tasks detected. Completion is {dashboard.CompletionRate}%.";

        return await AskLlmOrFail(providerResult.Data, $"Analyze delivery risk from this project dashboard:\n{context}", cancellationToken);
    }

    public async Task<ApiResponse<AiResponse>> GenerateTasksFromMessageAsync(AiGenerateTasksFromMessageRequest request, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.GetUserId();
        if (!await permissionService.CanCreateTask(userId, request.ProjectId))
        {
            return ApiResponse.Fail<AiResponse>("You do not have permission to generate tasks for this project.", StatusCodes.Status403Forbidden);
        }

        var workspaceId = await GetProjectWorkspaceIdAsync(request.ProjectId, cancellationToken);
        if (!workspaceId.HasValue)
        {
            return ApiResponse.Fail<AiResponse>("Project not found.", StatusCodes.Status404NotFound);
        }

        var providerResult = await aiProviderService.ResolveWorkspaceProviderAsync(workspaceId.Value, cancellationToken);
        if (!providerResult.Success || providerResult.Data is null)
        {
            return ToProviderFailure<AiResponse>(providerResult);
        }

        var content = request.MessageContent;
        if (string.IsNullOrWhiteSpace(content))
        {
            content = "No message content was provided. Ask the frontend to send MessageContent or wire MessageId lookup later.";
        }

        return await AskLlmOrFail(providerResult.Data, $"Turn this team message into concise task suggestions:\n{content}", cancellationToken);
    }

    public async Task<ApiResponse<AiResponse>> AskWorkspaceKnowledgeAsync(AiWorkspaceQuestionRequest request, CancellationToken cancellationToken = default)
    {
        var workspaceExists = await dbContext.Workspaces
            .AsNoTracking()
            .AnyAsync(workspace => workspace.Id == request.WorkspaceId, cancellationToken);
        if (!workspaceExists)
        {
            return ApiResponse.Fail<AiResponse>("Workspace not found.", StatusCodes.Status404NotFound);
        }

        var userId = currentUser.GetUserId();
        if (!await permissionService.CanAccessWorkspace(userId, request.WorkspaceId))
        {
            return ApiResponse.Fail<AiResponse>("Workspace knowledge access denied.", StatusCodes.Status403Forbidden);
        }

        var providerResult = await aiProviderService.ResolveWorkspaceProviderAsync(request.WorkspaceId, cancellationToken);
        if (!providerResult.Success || providerResult.Data is null)
        {
            return ToProviderFailure<AiResponse>(providerResult);
        }

        var snippets = await RetrieveWorkspaceKnowledgeAsync(request.WorkspaceId, userId, cancellationToken);
        var matches = RankSnippets(snippets, request.Question, maxResults: 6);
        if (matches.Count == 0)
        {
            return ApiResponse.Ok(new AiResponse
            {
                Result = "No workspace knowledge is available yet.",
                UsedLlm = false
            });
        }

        var sources = matches
            .Select(match => match.Source)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var context = string.Join(Environment.NewLine, matches.Select((match, index) =>
            $"{index + 1}. Source: {match.Source}{Environment.NewLine}{match.Text}"));
        var prompt = $"""
            You are the TaskFlow Connect retrieval-grounded assistant.
            Answer the user's question using only the TaskFlow context below.
            If the answer is not in the context, say you do not know from the available TaskFlow data.

            Question:
            {request.Question}

            TaskFlow context:
            {context}
            """;

        return await AskLlmOrFail(providerResult.Data, prompt, cancellationToken, sources);
    }

    public async Task<ApiResponse<AiChannelCommandResponse>> HandleChannelMentionAsync(Guid channelId, AiChannelCommandRequest request, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.GetUserId();
        var command = request.Command.Trim();
        if (string.IsNullOrWhiteSpace(command))
        {
            return ApiResponse.Fail<AiChannelCommandResponse>("AI command is required.");
        }

        var channel = await dbContext.Channels
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == channelId, cancellationToken);
        if (channel is null || !await permissionService.CanAccessChannel(userId, channelId))
        {
            return ApiResponse.Fail<AiChannelCommandResponse>("Channel not found or access denied.", StatusCodes.Status404NotFound);
        }

        if (!await permissionService.CanUseAiCommand(userId, channel.WorkspaceId))
        {
            return ApiResponse.Fail<AiChannelCommandResponse>("AI command access denied.", StatusCodes.Status403Forbidden);
        }

        var providerResult = await aiProviderService.ResolveWorkspaceProviderAsync(channel.WorkspaceId, cancellationToken);
        if (!providerResult.Success || providerResult.Data is null)
        {
            return ToProviderFailure<AiChannelCommandResponse>(providerResult);
        }
        var provider = providerResult.Data;

        var intentCommand = NormalizeCommandForIntent(command);
        var isSummaryCommand = IsConversationSummaryCommand(intentCommand);
        var artifactType = ResolveArtifactType(intentCommand);
        var requestedAttachmentIds = ResolveRequestedAttachmentIds(request);
        var useChannelOnlyContext = isSummaryCommand || requestedAttachmentIds is { Count: 0 };

        if (requestedAttachmentIds is { Count: > 0 })
        {
            var selectedAttachments = await dbContext.ChannelAttachments
                .AsNoTracking()
                .Include(attachment => attachment.Message)
                .Where(attachment =>
                    requestedAttachmentIds.Contains(attachment.Id)
                    && attachment.WorkspaceId == channel.WorkspaceId
                    && attachment.ChannelId == channelId)
                .ToListAsync(cancellationToken);
            if (selectedAttachments.Count != requestedAttachmentIds.Count)
            {
                return ApiResponse.Fail<AiChannelCommandResponse>("Attachment not found in this channel.", StatusCodes.Status404NotFound);
            }

            if (selectedAttachments.Any(IsAiGeneratedAttachment))
            {
                return ApiResponse.Fail<AiChannelCommandResponse>(
                    "AI-generated outputs cannot be selected as channel context.",
                    StatusCodes.Status400BadRequest);
            }

            if (selectedAttachments.Any(attachment => !attachment.IsAiIndexed))
            {
                return ApiResponse.Fail<AiChannelCommandResponse>(
                    "Only indexed attachments can be selected as AI context.",
                    StatusCodes.Status400BadRequest);
            }
        }

        var selectedContextHasIndexedAttachment = requestedAttachmentIds is { Count: > 0 };
        if (RequiresIndexedAttachment(artifactType) && !selectedContextHasIndexedAttachment && requestedAttachmentIds is null)
        {
            selectedContextHasIndexedAttachment = await WhereContextSourceAttachments(dbContext.ChannelAttachments.AsNoTracking())
                .AnyAsync(attachment =>
                    attachment.ChannelId == channelId
                    && attachment.IsAiIndexed,
                    cancellationToken);
        }

        if (RequiresIndexedAttachment(artifactType) && !selectedContextHasIndexedAttachment)
        {
            return ApiResponse.Fail<AiChannelCommandResponse>(
                "Select an indexed attachment before requesting report, PPT, or code output.",
                StatusCodes.Status400BadRequest);
        }

        try
        {
            var contextResult = await aiContextService.BuildChannelContextAsync(
                channelId,
                command,
                requestedAttachmentIds,
                cancellationToken,
                channelOnly: useChannelOnlyContext);
            if (!contextResult.Success || contextResult.Data is null)
            {
                return ApiResponse.Fail<AiChannelCommandResponse>(contextResult.Message, contextResult.StatusCode, contextResult.Errors);
            }

            var context = contextResult.Data;
            if (ShouldRouteChannelCommandToAgent(artifactType))
            {
                var agentJob = await CreateChannelAgentJobAsync(channel, command, artifactType, requestedAttachmentIds, cancellationToken);
                return ApiResponse.Ok(new AiChannelCommandResponse
                {
                    Result = $"TaskFlow Agent job created for {artifactType}. Approve the generated plan before it performs any write actions.",
                    ArtifactType = artifactType,
                    UsedLlm = false,
                    AgentJobId = agentJob.Id,
                    RequiresApproval = true,
                    Sources = context.Sources,
                    CreatedAtUtc = agentJob.CreatedAtUtc
                });
            }

            var isTaskCommand = IsTaskCommand(intentCommand);
            var prompt = isTaskCommand
                ? BuildChannelTaskSuggestionPrompt(channel.Name, command, context.RecentMessages, context.RetrievedContext)
                : BuildChannelAiPrompt(channel.Name, artifactType, command, context.RecentMessages, context.RetrievedContext);
            var model = (request.UseProModel ?? ShouldUseProModel(command)) ? ResolveProModel(provider) : ResolveMainModel(provider);
            var result = await InvokeChatCompletionAsync(
                model,
                "You are TaskFlow AI, a channel member inside a project collaboration app. Use only the provided accessible context.",
                prompt,
                provider,
                cancellationToken,
                responseFormatJson: isTaskCommand);

            var suggestedTasks = Array.Empty<string>();
            if (isTaskCommand)
            {
                suggestedTasks = ExtractSuggestedTasks(result).ToArray();
                if (suggestedTasks.Length == 0)
                {
                    throw new AiProviderException("AI provider did not return task suggestions in the required JSON format.");
                }
                result = FormatSuggestedTasks(suggestedTasks);
            }

            return ApiResponse.Ok(new AiChannelCommandResponse
            {
                Result = result,
                ArtifactType = artifactType,
                UsedLlm = true,
                Sources = context.Sources,
                SuggestedTasks = suggestedTasks,
            });
        }
        catch (AttachmentProcessingException ex)
        {
            return ApiResponse.Fail<AiChannelCommandResponse>(ex.Message, ex.StatusCode);
        }
        catch (PineconeVectorStoreException ex)
        {
            return ApiResponse.Fail<AiChannelCommandResponse>(ex.Message, StatusCodes.Status502BadGateway);
        }
        catch (AiProviderException ex)
        {
            return ApiResponse.Fail<AiChannelCommandResponse>(ex.Message, StatusCodes.Status502BadGateway);
        }
    }

    public async Task<ApiResponse<AiChannelCommandResponse>> ShareChannelResultAsync(
        Guid channelId,
        AiChannelShareRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.GetUserId();
        var channel = await dbContext.Channels
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == channelId, cancellationToken);
        if (channel is null || !await permissionService.CanSendMessage(userId, channelId))
        {
            return ApiResponse.Fail<AiChannelCommandResponse>("Channel not found or access denied.", StatusCodes.Status404NotFound);
        }

        if (!await permissionService.CanUseAiCommand(userId, channel.WorkspaceId))
        {
            return ApiResponse.Fail<AiChannelCommandResponse>("AI command access denied.", StatusCodes.Status403Forbidden);
        }

        var result = request.Result.Trim();
        var artifactType = NormalizeShareArtifactType(request.ArtifactType);
        if (artifactType is "report-outline" or "ppt-outline" && request.AgentJobId is null)
        {
            return ApiResponse.Fail<AiChannelCommandResponse>(
                "Report and PPT outputs are generated by a TaskFlow Agent job. Run the channel AI command again so the artifact can be created through the approval flow.",
                StatusCodes.Status400BadRequest);
        }

        GeneratedArtifact? generatedAttachment = null;
        var sharedMessage = await CreateSharedAiMessageAsync(
            channel,
            userId,
            result,
            generatedAttachment,
            artifactType,
            request.Sources,
            request.SuggestedTasks,
            request.AgentJobId,
            request.RequiresApproval,
            null,
            null,
            cancellationToken);

        return ApiResponse.Ok(new AiChannelCommandResponse
        {
            Result = result,
            ArtifactType = artifactType,
            UsedLlm = false,
            SharedToChannel = true,
            MessageId = sharedMessage.Id,
            AgentJobId = request.AgentJobId,
            RequiresApproval = request.RequiresApproval,
            Sources = sharedMessage.ToResponse().Sources,
            SuggestedTasks = sharedMessage.ToResponse().SuggestedTasks,
            CreatedAtUtc = sharedMessage.CreatedAtUtc
        });
    }

    private async Task<AgentJob> CreateChannelAgentJobAsync(
        Channel channel,
        string command,
        string artifactType,
        IReadOnlyCollection<Guid>? attachmentIds,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId();
        var repository = await ResolveDefaultRepositoryAsync(channel.WorkspaceId, artifactType, cancellationToken);
        var artifactTarget = ResolveAgentArtifactTarget(artifactType, repository is not null);
        var primaryAttachmentId = attachmentIds?.FirstOrDefault();
        var job = new AgentJob
        {
            Id = Guid.NewGuid(),
            WorkspaceId = channel.WorkspaceId,
            UserId = userId,
            ChannelId = channel.Id,
            AttachmentId = primaryAttachmentId == Guid.Empty ? null : primaryAttachmentId,
            ContextAttachmentIdsJson = attachmentIds is null ? null : JsonSerializer.Serialize(attachmentIds, JsonOptions),
            GitHubRepositoryConnectionId = repository?.Id,
            ArtifactTarget = artifactTarget,
            Goal = command,
            Status = AgentJobStatus.Planning
        };

        dbContext.AgentJobs.Add(job);
        dbContext.AgentEvents.Add(new AgentEvent
        {
            Id = Guid.NewGuid(),
            AgentJobId = job.Id,
            ActorUserId = userId,
            EventType = AgentEventType.Created,
            Message = $"Agent job created from channel #{channel.Name}.",
            DataJson = JsonSerializer.Serialize(new
            {
                artifactType,
                artifactTarget,
                channelId = channel.Id,
                attachmentId = primaryAttachmentId == Guid.Empty ? null : primaryAttachmentId,
                attachmentIds,
                repositoryId = repository?.Id
            }, JsonOptions)
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return job;
    }

    private async Task<GitHubRepositoryConnection?> ResolveDefaultRepositoryAsync(
        Guid workspaceId,
        string artifactType,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(artifactType, "code-advice", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return await dbContext.GitHubRepositoryConnections
            .AsNoTracking()
            .Where(repository => repository.WorkspaceId == workspaceId && repository.IsEnabled)
            .OrderByDescending(repository => repository.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static bool ShouldRouteChannelCommandToAgent(string artifactType)
    {
        return artifactType is "report-outline" or "ppt-outline" or "code-advice";
    }

    private static bool RequiresIndexedAttachment(string artifactType)
    {
        return artifactType is "report-outline" or "ppt-outline" or "code-advice";
    }

    private static string NormalizeShareArtifactType(string? artifactType)
    {
        var normalized = (artifactType ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "summary" => "summary",
            "requirements" => "requirements",
            "task-suggestions" => "task-suggestions",
            "report-outline" => "report-outline",
            "ppt-outline" => "ppt-outline",
            "code-advice" => "code-advice",
            _ => "answer"
        };
    }

    private static string? ResolveAgentArtifactTarget(string artifactType, bool hasRepository)
    {
        return artifactType switch
        {
            "report-outline" => "docx",
            "ppt-outline" => "pptx",
            "code-advice" => hasRepository ? "pull-request" : "patch",
            _ => null
        };
    }

    public async Task<ApiResponse<ChannelAttachmentResponse>> IndexChannelAttachmentAsync(Guid channelId, IFormFile file, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.GetUserId();
        var channel = await dbContext.Channels
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == channelId, cancellationToken);
        if (channel is null || !await permissionService.CanAccessChannel(userId, channelId))
        {
            return ApiResponse.Fail<ChannelAttachmentResponse>("Channel not found or access denied.", StatusCodes.Status404NotFound);
        }

        if (file is null || file.Length == 0)
        {
            return ApiResponse.Fail<ChannelAttachmentResponse>("Attachment file is required.");
        }

        if (file.Length > MaxUploadBytes)
        {
            return ApiResponse.Fail<ChannelAttachmentResponse>("Attachment exceeds the 20 MB upload limit.", StatusCodes.Status413PayloadTooLarge);
        }

        var providerResult = await aiProviderService.ResolveWorkspaceProviderAsync(channel.WorkspaceId, cancellationToken);
        if (!providerResult.Success || providerResult.Data is null)
        {
            return ToProviderFailure<ChannelAttachmentResponse>(providerResult);
        }

        await using var stream = file.OpenReadStream();
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken);
        var fileBytes = memoryStream.ToArray();
        var contentType = ResolveContentType(file);
        var fileName = Path.GetFileName(file.FileName);
        var attachment = new ChannelAttachment
        {
            Id = Guid.NewGuid(),
            WorkspaceId = channel.WorkspaceId,
            ChannelId = channelId,
            UploadedByUserId = userId,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = file.Length,
            Summary = string.Empty,
            ExtractedText = string.Empty,
            IsAiIndexed = false
        };

        try
        {
            var provider = providerResult.Data;
            var indexed = await BuildAttachmentIndexAsync(attachment, fileBytes, provider, cancellationToken);

            dbContext.ChannelAttachments.Add(attachment);
            dbContext.ChannelAttachmentBlobs.Add(new ChannelAttachmentBlob
            {
                AttachmentId = attachment.Id,
                Content = fileBytes
            });

            await pineconeVectorStore.UpsertChunksAsync(
                channel.WorkspaceId,
                channelId,
                attachment.Id,
                indexed.PineconeChunks,
                cancellationToken);

            dbContext.ChannelKnowledgeChunks.AddRange(indexed.ChunkEntities);
            await dbContext.SaveChangesAsync(cancellationToken);
            return ApiResponse.Created(attachment.ToResponse(), "Attachment indexed for channel AI.");
        }
        catch (AttachmentProcessingException ex)
        {
            return ApiResponse.Fail<ChannelAttachmentResponse>(ex.Message, ex.StatusCode);
        }
        catch (AiProviderException ex)
        {
            return ApiResponse.Fail<ChannelAttachmentResponse>(ex.Message, StatusCodes.Status502BadGateway);
        }
        catch (PineconeVectorStoreException ex)
        {
            return ApiResponse.Fail<ChannelAttachmentResponse>(ex.Message, StatusCodes.Status502BadGateway);
        }
    }

    public async Task<ApiResponse<ChannelAttachmentResponse>> IndexExistingChannelAttachmentAsync(Guid attachmentId, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.GetUserId();
        var attachment = await dbContext.ChannelAttachments
            .Include(item => item.Message)
            .Include(item => item.Blob)
            .FirstOrDefaultAsync(item => item.Id == attachmentId, cancellationToken);
        if (attachment?.Blob is null)
        {
            return ApiResponse.Fail<ChannelAttachmentResponse>("Attachment not found.", StatusCodes.Status404NotFound);
        }

        if (!await permissionService.CanAccessChannel(userId, attachment.ChannelId))
        {
            return ApiResponse.Fail<ChannelAttachmentResponse>("Attachment not found or access denied.", StatusCodes.Status404NotFound);
        }

        if (IsAiGeneratedAttachment(attachment))
        {
            return ApiResponse.Fail<ChannelAttachmentResponse>(
                "AI-generated outputs cannot be indexed as channel context.",
                StatusCodes.Status400BadRequest);
        }

        var providerResult = await aiProviderService.ResolveWorkspaceProviderAsync(attachment.WorkspaceId, cancellationToken);
        if (!providerResult.Success || providerResult.Data is null)
        {
            return ToProviderFailure<ChannelAttachmentResponse>(providerResult);
        }

        try
        {
            if (attachment.IsAiIndexed)
            {
                await pineconeVectorStore.DeleteByAttachmentAsync(attachment.WorkspaceId, attachment.Id, cancellationToken);
            }

            var existingChunks = await dbContext.ChannelKnowledgeChunks
                .Where(chunk => chunk.AttachmentId == attachment.Id)
                .ToListAsync(cancellationToken);
            dbContext.ChannelKnowledgeChunks.RemoveRange(existingChunks);

            var indexed = await BuildAttachmentIndexAsync(attachment, attachment.Blob.Content, providerResult.Data, cancellationToken);
            await pineconeVectorStore.UpsertChunksAsync(
                attachment.WorkspaceId,
                attachment.ChannelId,
                attachment.Id,
                indexed.PineconeChunks,
                cancellationToken);

            dbContext.ChannelKnowledgeChunks.AddRange(indexed.ChunkEntities);
            await dbContext.SaveChangesAsync(cancellationToken);
            return ApiResponse.Ok(attachment.ToResponse(), "Attachment indexed for channel AI.");
        }
        catch (AttachmentProcessingException ex)
        {
            return ApiResponse.Fail<ChannelAttachmentResponse>(ex.Message, ex.StatusCode);
        }
        catch (AiProviderException ex)
        {
            return ApiResponse.Fail<ChannelAttachmentResponse>(ex.Message, StatusCodes.Status502BadGateway);
        }
        catch (PineconeVectorStoreException ex)
        {
            return ApiResponse.Fail<ChannelAttachmentResponse>(ex.Message, StatusCodes.Status502BadGateway);
        }
    }

    private async Task<AttachmentIndexBuildResult> BuildAttachmentIndexAsync(
        ChannelAttachment attachment,
        byte[] fileBytes,
        AiProviderRuntime provider,
        CancellationToken cancellationToken)
    {
        var processed = await ProcessAttachmentAsync(fileBytes, attachment.FileName, attachment.ContentType, provider, cancellationToken);
        var chunks = BuildIndexChunks(processed.Sections).ToArray();
        if (chunks.Length == 0)
        {
            throw new AttachmentProcessingException(
                "Attachment parsing returned no searchable content.",
                StatusCodes.Status422UnprocessableEntity);
        }

        attachment.Summary = TrimTo(processed.Summary, 4000);
        attachment.ExtractedText = TrimTo(string.Join(Environment.NewLine, chunks.Select(chunk => chunk.Content)), MaxExtractedTextLength);
        attachment.IsAiIndexed = true;

        var channelName = attachment.Channel?.Name
            ?? await dbContext.Channels
                .AsNoTracking()
                .Where(channel => channel.Id == attachment.ChannelId)
                .Select(channel => channel.Name)
                .FirstAsync(cancellationToken);
        var trimmedChunks = chunks
            .Select(chunk => chunk with { Content = TrimTo(chunk.Content, 2500) })
            .ToArray();
        var embeddings = await GenerateEmbeddingsAsync(
            trimmedChunks.Select(chunk => chunk.Content).ToArray(),
            provider,
            cancellationToken);
        if (embeddings.Count != trimmedChunks.Length)
        {
            throw new AiProviderException(
                $"Embedding provider returned {embeddings.Count} vectors for {trimmedChunks.Length} attachment chunks.");
        }

        var chunkEntities = new List<ChannelKnowledgeChunk>();
        var pineconeChunks = new List<PineconeKnowledgeChunk>();
        for (var index = 0; index < trimmedChunks.Length; index++)
        {
            var chunk = trimmedChunks[index];
            var chunkId = Guid.NewGuid();

            chunkEntities.Add(new ChannelKnowledgeChunk
            {
                Id = chunkId,
                WorkspaceId = attachment.WorkspaceId,
                ChannelId = attachment.ChannelId,
                AttachmentId = attachment.Id,
                SourceType = chunk.SourceType,
                SourceLabel = chunk.SourceLabel,
                Content = chunk.Content
            });
            pineconeChunks.Add(new PineconeKnowledgeChunk(
                chunkId,
                chunk.SourceType,
                chunk.SourceLabel,
                channelName,
                chunk.Content,
                index + 1,
                embeddings[index]));
        }

        return new AttachmentIndexBuildResult(chunkEntities, pineconeChunks);
    }

    public async Task<ApiResponse<IReadOnlyCollection<ChannelAttachmentResponse>>> ListChannelAttachmentsAsync(Guid channelId, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.GetUserId();
        if (!await permissionService.CanAccessChannel(userId, channelId))
        {
            return ApiResponse.Fail<IReadOnlyCollection<ChannelAttachmentResponse>>("Channel not found or access denied.", StatusCodes.Status404NotFound);
        }

        var attachments = await WhereContextSourceAttachments(dbContext.ChannelAttachments.AsNoTracking())
            .Where(attachment => attachment.ChannelId == channelId)
            .OrderByDescending(attachment => attachment.CreatedAtUtc)
            .Take(30)
            .ToListAsync(cancellationToken);

        IReadOnlyCollection<ChannelAttachmentResponse> response = attachments
            .Select(attachment => attachment.ToResponse())
            .ToArray();
        return ApiResponse.Ok(response);
    }

    private async Task<IReadOnlyList<KnowledgeSnippet>> RetrieveWorkspaceKnowledgeAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken)
    {
        var snippets = new List<KnowledgeSnippet>();

        var workspace = await dbContext.Workspaces
            .AsNoTracking()
            .Where(item => item.Id == workspaceId)
            .Select(item => new
            {
                item.Name,
                item.Description
            })
            .FirstAsync(cancellationToken);
        snippets.Add(new KnowledgeSnippet("Workspace", $"Workspace {workspace.Name}. Description: {workspace.Description ?? "No description"}"));

        var projects = await dbContext.Projects
            .AsNoTracking()
            .Where(project => project.WorkspaceId == workspaceId)
            .OrderByDescending(project => project.UpdatedAtUtc)
            .Take(50)
            .Select(project => new
            {
                project.Name,
                project.Description,
                project.Status,
                project.DeadlineUtc,
                project.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken);
        snippets.AddRange(projects.Select(project => new KnowledgeSnippet(
            $"Project: {project.Name}",
            $"Project {project.Name}. Status: {project.Status}. Deadline UTC: {FormatDate(project.DeadlineUtc)}. Updated UTC: {FormatDate(project.UpdatedAtUtc)}. Description: {project.Description ?? "No description"}")));

        var tasks = await dbContext.TaskItems
            .AsNoTracking()
            .Where(task => task.Project!.WorkspaceId == workspaceId)
            .OrderByDescending(task => task.UpdatedAtUtc)
            .Take(120)
            .Select(task => new
            {
                ProjectName = task.Project!.Name,
                task.Title,
                task.Description,
                task.Status,
                task.Priority,
                task.DeadlineUtc,
                task.UpdatedAtUtc,
                AssigneeName = task.Assignee == null ? null : task.Assignee.Name
            })
            .ToListAsync(cancellationToken);
        snippets.AddRange(tasks.Select(task => new KnowledgeSnippet(
            $"Task: {task.Title}",
            $"Task {task.Title} in project {task.ProjectName}. Status: {task.Status}. Priority: {task.Priority}. Assignee: {task.AssigneeName ?? "Unassigned"}. Deadline UTC: {FormatDate(task.DeadlineUtc)}. Updated UTC: {FormatDate(task.UpdatedAtUtc)}. Description: {task.Description ?? "No description"}")));

        var messages = await dbContext.Messages
            .AsNoTracking()
            .Where(message =>
                message.Channel!.WorkspaceId == workspaceId
                && !message.IsDeleted
                && !message.Content.StartsWith(IAiCommandService.ChannelAiMessagePrefix)
                && (!message.Channel!.IsPrivate || message.Channel.Members.Any(member => member.UserId == userId)))
            .OrderByDescending(message => message.CreatedAtUtc)
            .Take(120)
            .Select(message => new
            {
                ChannelName = message.Channel!.Name,
                SenderName = message.Sender!.Name,
                message.Content,
                message.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);
        snippets.AddRange(messages.Select(message => new KnowledgeSnippet(
            $"Message: #{message.ChannelName}",
            $"Message in #{message.ChannelName} from {message.SenderName} at {FormatDate(message.CreatedAtUtc)} UTC: {message.Content}")));

        return snippets;
    }

    private async Task<IReadOnlyList<KnowledgeSnippet>> RetrieveChannelKnowledgeAsync(
        Guid workspaceId,
        Guid channelId,
        string query,
        Guid? attachmentId,
        AiProviderRuntime provider,
        CancellationToken cancellationToken)
    {
        var chunksQuery = dbContext.ChannelKnowledgeChunks
            .AsNoTracking()
            .Where(chunk =>
                chunk.WorkspaceId == workspaceId
                && chunk.ChannelId == channelId);

        if (attachmentId.HasValue)
        {
            return await chunksQuery
                .Where(chunk => chunk.AttachmentId == attachmentId.Value)
                .OrderBy(chunk => chunk.CreatedAtUtc)
                .Take(MaxDirectAttachmentChunks)
                .Select(chunk => new KnowledgeSnippet(
                    $"{chunk.SourceType}: {chunk.SourceLabel}",
                    chunk.Content))
                .ToListAsync(cancellationToken);
        }

        if (!await chunksQuery.AnyAsync(cancellationToken))
        {
            return [];
        }

        var queryEmbedding = await GenerateEmbeddingAsync(query, provider, cancellationToken);
        var matches = await pineconeVectorStore.SearchAsync(
            workspaceId,
            channelId,
            attachmentId,
            queryEmbedding,
            topK: 8,
            cancellationToken);

        return matches
            .Select(match => new KnowledgeSnippet(match.Source, match.Text))
            .ToArray();
    }

    private async Task<IReadOnlyList<KnowledgeSnippet>> RetrieveWorkspaceMessageKnowledgeAsync(
        Guid workspaceId,
        Guid userId,
        string query,
        CancellationToken cancellationToken)
    {
        var messages = await dbContext.Messages
            .AsNoTracking()
            .Where(message =>
                message.Channel!.WorkspaceId == workspaceId
                && !message.IsDeleted
                && !message.Content.StartsWith(IAiCommandService.ChannelAiMessagePrefix)
                && (!message.Channel!.IsPrivate || message.Channel.Members.Any(member => member.UserId == userId)))
            .OrderByDescending(message => message.CreatedAtUtc)
            .Take(180)
            .Select(message => new
            {
                ChannelName = message.Channel!.Name,
                SenderName = message.Sender!.Name,
                message.Content,
                message.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        var snippets = messages.Select(message => new KnowledgeSnippet(
            $"Message: #{message.ChannelName}",
            $"Message in #{message.ChannelName} from {message.SenderName} at {FormatDate(message.CreatedAtUtc)} UTC: {message.Content}"))
            .ToArray();

        return RankSnippets(snippets, query, 8);
    }

    private async Task<AttachmentProcessingResult> ProcessAttachmentAsync(
        byte[] fileBytes,
        string fileName,
        string contentType,
        AiProviderRuntime provider,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            var summary = await SummarizeImageAsync(fileBytes, contentType, fileName, provider, cancellationToken);
            return new AttachmentProcessingResult(
                summary,
                [new AttachmentIndexSection("image", fileName, summary)]);
        }

        if (IsPlainTextAttachment(contentType, extension))
        {
            var text = await ReadPlainTextAttachmentAsync(fileBytes, cancellationToken);
            var summary = await SummarizeTextAttachmentAsync(text, fileName, provider, cancellationToken);
            return new AttachmentProcessingResult(
                summary,
                [new AttachmentIndexSection("file", fileName, text)]);
        }

        if (extension == ".docx" || contentType.Equals("application/vnd.openxmlformats-officedocument.wordprocessingml.document", StringComparison.OrdinalIgnoreCase))
        {
            var text = await ExtractDocxTextAsync(fileBytes, cancellationToken);
            var summary = await SummarizeTextAttachmentAsync(text, fileName, provider, cancellationToken);
            return new AttachmentProcessingResult(
                summary,
                [new AttachmentIndexSection("file", fileName, text)]);
        }

        if (extension == ".pdf" || contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            return await ProcessPdfAttachmentAsync(fileBytes, fileName, provider, cancellationToken);
        }

        throw new AttachmentProcessingException(
            $"Unsupported attachment type: {contentType}. Supported types are PDF, images, TXT/MD/CSV/JSON/XML, and DOCX.",
            StatusCodes.Status415UnsupportedMediaType);
    }

    private async Task<AttachmentProcessingResult> ProcessPdfAttachmentAsync(
        byte[] fileBytes,
        string fileName,
        AiProviderRuntime provider,
        CancellationToken cancellationToken)
    {
        try
        {
            var sections = new List<AttachmentIndexSection>();
            using var document = PdfDocument.Open(fileBytes);

            foreach (var page in document.GetPages())
            {
                var pageText = NormalizeExtractedText(ContentOrderTextExtractor.GetText(page));
                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    sections.Add(new AttachmentIndexSection(
                        "pdf-text",
                        $"{fileName} page {page.Number}",
                        pageText));
                }
            }

            if (sections.Count == 0 || configuration.GetValue("AI:SummarizePdfImagesWithText", false))
            {
                var imageCount = 0;
                foreach (var page in document.GetPages())
                {
                    foreach (var image in page.GetImages())
                    {
                        if (imageCount >= MaxPdfImagesToSummarize)
                        {
                            break;
                        }

                        var imagePayload = TryExtractPdfImage(image);
                        if (imagePayload is null)
                        {
                            continue;
                        }

                        imageCount++;
                        var imageLabel = $"{fileName} page {page.Number} image {imageCount}";
                        var imageSummary = await SummarizeImageAsync(
                            imagePayload.Bytes,
                            imagePayload.ContentType,
                            imageLabel,
                            provider,
                            cancellationToken);
                        sections.Add(new AttachmentIndexSection("pdf-image", imageLabel, imageSummary));
                    }

                    if (imageCount >= MaxPdfImagesToSummarize)
                    {
                        break;
                    }
                }
            }

            if (sections.Count == 0)
            {
                throw new AttachmentProcessingException(
                    "PDF text and embedded image extraction returned no searchable content. Scanned PDFs require OCR, which is not enabled in this flow.",
                    StatusCodes.Status422UnprocessableEntity);
            }

            var summary = await SummarizeTextAttachmentAsync(
                string.Join(Environment.NewLine, sections.Select(section =>
                    $"Source: {section.SourceLabel}{Environment.NewLine}{section.Content}")),
                fileName,
                provider,
                cancellationToken);

            return new AttachmentProcessingResult(summary, sections);
        }
        catch (AttachmentProcessingException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new AttachmentProcessingException(
                $"PDF parsing failed: {ex.Message}",
                StatusCodes.Status422UnprocessableEntity);
        }
    }

    private async Task<string> SummarizeImageAsync(byte[] imageBytes, string contentType, string fileName, AiProviderRuntime provider, CancellationToken cancellationToken)
    {
        var imageDataUrl = $"data:{contentType};base64,{Convert.ToBase64String(imageBytes)}";
        var payload = new Dictionary<string, object?>
        {
            ["model"] = ResolveVisionModel(provider),
            ["temperature"] = 0.1,
            ["max_tokens"] = MaxImageSummaryTokens,
            ["messages"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["role"] = "system",
                    ["content"] = "You extract structured, factual image summaries for a project collaboration RAG system."
                },
                new Dictionary<string, object?>
                {
                    ["role"] = "user",
                    ["content"] = new object[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["type"] = "text",
                            ["text"] = $"""
                                Describe image attachment '{fileName}' for a project collaboration RAG index.
                                Return a complete factual description, not a short summary.
                                Cover the image from top to bottom and left to right, including:
                                - all visible text, labels, numbers, dates, statuses, and table/list content;
                                - key objects, people, diagrams, UI controls, layout, colors, and spatial relationships;
                                - project tasks, requirements, risks, blockers, decisions, and action items visible in the image.
                                If the image is a long screenshot, continue until every visible region is described.
                                Do not invent unseen details.
                                """
                        },
                        new Dictionary<string, object?>
                        {
                            ["type"] = "image_url",
                            ["image_url"] = new Dictionary<string, object?>
                            {
                                ["url"] = imageDataUrl
                            }
                        }
                    }
                }
            }
        };

        return await PostChatCompletionAsync(payload, provider, cancellationToken);
    }

    private async Task<string> SummarizeTextAttachmentAsync(string text, string fileName, AiProviderRuntime provider, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new AttachmentProcessingException("Attachment text extraction returned no content.");
        }

        var prompt = $"""
            Summarize this uploaded TaskFlow channel attachment for later RAG retrieval.
            File: {fileName}

            Required output:
            - Key facts
            - Decisions or requirements
            - Action items
            - Risks or blockers

            Attachment text:
            {TrimTo(text, 12000)}
            """;

        return await InvokeChatCompletionAsync(
            ResolveMainModel(provider),
            "You create concise, grounded summaries of uploaded project files.",
            prompt,
            provider,
            cancellationToken);
    }

    private async Task<string> ReadPlainTextAttachmentAsync(byte[] fileBytes, CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream(fileBytes);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = await reader.ReadToEndAsync(cancellationToken);
        text = NormalizeExtractedText(text);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new AttachmentProcessingException("Attachment text extraction returned no content.");
        }

        return TrimTo(text, MaxExtractedTextLength);
    }

    private static async Task<string> ExtractDocxTextAsync(byte[] fileBytes, CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream(fileBytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var documentEntry = archive.GetEntry("word/document.xml")
            ?? throw new AttachmentProcessingException("DOCX document.xml was not found.");

        await using var documentStream = documentEntry.Open();
        var document = await XDocument.LoadAsync(documentStream, LoadOptions.None, cancellationToken);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var text = string.Join(" ", document.Descendants(w + "t").Select(element => element.Value));
        text = NormalizeExtractedText(text);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new AttachmentProcessingException("DOCX text extraction returned no content.");
        }

        return TrimTo(text, MaxExtractedTextLength);
    }

    private async Task<float[]> GenerateEmbeddingAsync(string input, AiProviderRuntime provider, CancellationToken cancellationToken)
    {
        var embeddings = await GenerateEmbeddingsAsync([input], provider, cancellationToken);
        return embeddings.Count == 1
            ? embeddings[0]
            : throw new AiProviderException($"Embedding provider returned {embeddings.Count} vectors for one input.");
    }

    private async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IReadOnlyCollection<string> inputs, AiProviderRuntime provider, CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = ResolveEmbeddingModel(provider),
            ["input"] = inputs.Select(input => TrimTo(input, 2000)).ToArray()
        };
        var client = CreateAiHttpClient(provider);
        using var response = await PostProviderJsonAsync(client, "embeddings", payload, provider, "Embedding provider", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new AiProviderException($"Embedding provider error {(int)response.StatusCode}: {TrimProviderError(body, provider.ApiKey)}");
        }

        using var document = JsonDocument.Parse(body);
        return document.RootElement
            .GetProperty("data")
            .EnumerateArray()
            .OrderBy(item => item.TryGetProperty("index", out var indexElement) ? indexElement.GetInt32() : 0)
            .Select(item => item.GetProperty("embedding")
                .EnumerateArray()
                .Select(value => value.GetSingle())
                .ToArray())
            .ToArray();
    }

    private async Task<Message> CreateSharedAiMessageAsync(
        Channel channel,
        Guid userId,
        string result,
        GeneratedArtifact? generatedArtifact,
        string artifactType,
        IReadOnlyCollection<string> sources,
        IReadOnlyCollection<string> suggestedTasks,
        Guid? agentJobId,
        bool requiresApproval,
        Guid? createdTaskId,
        string? createdTaskTitle,
        CancellationToken cancellationToken)
    {
        var normalizedSources = (sources ?? Array.Empty<string>())
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct()
            .Take(20)
            .ToArray();
        var normalizedSuggestedTasks = (suggestedTasks ?? Array.Empty<string>())
            .Where(task => !string.IsNullOrWhiteSpace(task))
            .Distinct()
            .Take(10)
            .ToArray();
        var message = new Message
        {
            Id = Guid.NewGuid(),
            ChannelId = channel.Id,
            SenderId = userId,
            Content = $"{IAiCommandService.ChannelAiMessagePrefix}{TrimTo(result, 3900)}",
            AiArtifactType = artifactType,
            AiSourcesJson = JsonSerializer.Serialize(normalizedSources, JsonOptions),
            AiSuggestedTasksJson = JsonSerializer.Serialize(normalizedSuggestedTasks, JsonOptions),
            AiAgentJobId = agentJobId,
            AiRequiresApproval = requiresApproval,
            AiCreatedTaskId = createdTaskId,
            AiCreatedTaskTitle = createdTaskTitle
        };

        dbContext.Messages.Add(message);

        if (generatedArtifact is not null)
        {
            var attachment = new ChannelAttachment
            {
                Id = Guid.NewGuid(),
                WorkspaceId = channel.WorkspaceId,
                ChannelId = channel.Id,
                MessageId = message.Id,
                UploadedByUserId = userId,
                FileName = generatedArtifact.FileName,
                ContentType = generatedArtifact.ContentType,
                SizeBytes = generatedArtifact.Content.Length,
                Summary = generatedArtifact.Summary,
                ExtractedText = TrimTo(result, MaxExtractedTextLength),
                IsAiIndexed = false
            };
            var blob = new ChannelAttachmentBlob
            {
                AttachmentId = attachment.Id,
                Content = generatedArtifact.Content
            };

            dbContext.ChannelAttachments.Add(attachment);
            dbContext.ChannelAttachmentBlobs.Add(blob);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return await dbContext.Messages
            .Include(item => item.Sender)
            .Include(item => item.Attachments)
            .FirstAsync(item => item.Id == message.Id, cancellationToken);
    }

    private static GeneratedArtifact? BuildGeneratedArtifact(string artifactType, string channelName, string markdown)
    {
        var baseName = SafeFileName($"taskflow-{channelName}-{DateTime.UtcNow:yyyyMMdd-HHmm}");
        return artifactType switch
        {
            "report-outline" => new GeneratedArtifact(
                $"{baseName}-report.docx",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                BuildDocx(markdown),
                "AI-generated DOCX report outline."),
            "ppt-outline" => new GeneratedArtifact(
                $"{baseName}-presentation.pptx",
                "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                BuildPptx(markdown),
                "AI-generated PPTX presentation outline."),
            _ => null
        };
    }

    private static byte[] BuildDocx(string markdown)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddZipEntry(archive, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                </Types>
                """);
            AddZipEntry(archive, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);

            var paragraphs = SplitMarkdownLines(markdown)
                .Select(line => $"<w:p><w:r><w:t xml:space=\"preserve\">{XmlEscape(line)}</w:t></w:r></w:p>");
            AddZipEntry(archive, "word/document.xml", $$"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    {{string.Join(Environment.NewLine, paragraphs)}}
                    <w:sectPr><w:pgSz w:w="12240" w:h="15840"/><w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440"/></w:sectPr>
                  </w:body>
                </w:document>
                """);
        }

        return output.ToArray();
    }

    private static byte[] BuildPptx(string markdown)
    {
        var slides = BuildSlideTexts(markdown);
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddZipEntry(archive, "[Content_Types].xml", $$"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/ppt/presentation.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml"/>
                  {{string.Join(Environment.NewLine, slides.Select((_, index) => $"""<Override PartName="/ppt/slides/slide{index + 1}.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slide+xml"/>"""))}}
                </Types>
                """);
            AddZipEntry(archive, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="ppt/presentation.xml"/>
                </Relationships>
                """);
            AddZipEntry(archive, "ppt/_rels/presentation.xml.rels", $$"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  {{string.Join(Environment.NewLine, slides.Select((_, index) => $"""<Relationship Id="rId{index + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide" Target="slides/slide{index + 1}.xml"/>"""))}}
                </Relationships>
                """);
            AddZipEntry(archive, "ppt/presentation.xml", $$"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <p:presentation xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main">
                  <p:sldIdLst>
                    {{string.Join(Environment.NewLine, slides.Select((_, index) => $"""<p:sldId id="{256 + index}" r:id="rId{index + 1}"/>"""))}}
                  </p:sldIdLst>
                  <p:sldSz cx="9144000" cy="5143500" type="screen16x9"/>
                </p:presentation>
                """);

            for (var index = 0; index < slides.Count; index++)
            {
                var slide = slides[index];
                AddZipEntry(archive, $"ppt/slides/slide{index + 1}.xml", BuildSlideXml(slide.Title, slide.Body));
            }
        }

        return output.ToArray();
    }

    private static string BuildSlideXml(string title, IReadOnlyCollection<string> bodyLines)
    {
        var body = string.Join(Environment.NewLine, bodyLines.Select((line, index) =>
            $"""<a:p><a:r><a:rPr lang="en-US" sz="2400"/><a:t>{XmlEscape(line)}</a:t></a:r></a:p>"""));

        return $$"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <p:sld xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main">
              <p:cSld><p:spTree>
                <p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/>
                <p:sp>
                  <p:nvSpPr><p:cNvPr id="2" name="Title"/><p:cNvSpPr txBox="1"/><p:nvPr/></p:nvSpPr>
                  <p:spPr><a:xfrm><a:off x="457200" y="274320"/><a:ext cx="8229600" cy="800000"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></p:spPr>
                  <p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:rPr lang="en-US" sz="3600" b="1"/><a:t>{{XmlEscape(title)}}</a:t></a:r></a:p></p:txBody>
                </p:sp>
                <p:sp>
                  <p:nvSpPr><p:cNvPr id="3" name="Body"/><p:cNvSpPr txBox="1"/><p:nvPr/></p:nvSpPr>
                  <p:spPr><a:xfrm><a:off x="685800" y="1371600"/><a:ext cx="7772400" cy="3314700"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></p:spPr>
                  <p:txBody><a:bodyPr wrap="square"/><a:lstStyle/>{{body}}</p:txBody>
                </p:sp>
              </p:spTree></p:cSld>
            </p:sld>
            """;
    }

    private static IReadOnlyList<GeneratedSlide> BuildSlideTexts(string markdown)
    {
        var lines = SplitMarkdownLines(markdown);
        var slides = new List<GeneratedSlide>();
        foreach (var line in lines)
        {
            var normalized = Regex.Replace(line, @"^#+\s*", string.Empty).Trim();
            if ((line.StartsWith('#') || slides.Count == 0) && normalized.Length > 0)
            {
                slides.Add(new GeneratedSlide(TrimTo(normalized, 80), []));
                continue;
            }

            if (slides.Count == 0)
            {
                slides.Add(new GeneratedSlide("TaskFlow AI Output", []));
            }

            if (slides[^1].Body.Count < 7)
            {
                slides[^1].Body.Add(TrimTo(Regex.Replace(normalized, @"^[-*]\s*", string.Empty), 140));
            }
        }

        return slides.Take(12).ToArray();
    }

    private static IReadOnlyList<string> SplitMarkdownLines(string markdown)
    {
        var lines = markdown
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        return lines.Length == 0 ? ["TaskFlow AI generated this artifact."] : lines;
    }

    private static void AddZipEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content.Trim());
    }

    private static string SafeFileName(string value)
    {
        var sanitized = Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9._-]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "taskflow-ai" : TrimTo(sanitized, 120);
    }

    private static string XmlEscape(string value)
    {
        return System.Security.SecurityElement.Escape(value) ?? string.Empty;
    }

    private async Task<string> InvokeChatCompletionAsync(
        string model,
        string systemPrompt,
        string userPrompt,
        AiProviderRuntime provider,
        CancellationToken cancellationToken,
        bool responseFormatJson = false)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["temperature"] = 0.2,
            ["max_tokens"] = 1800,
            ["messages"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["role"] = "system",
                    ["content"] = systemPrompt
                },
                new Dictionary<string, object?>
                {
                    ["role"] = "user",
                    ["content"] = userPrompt
                }
            }
        };

        if (responseFormatJson)
        {
            payload["response_format"] = new Dictionary<string, object?>
            {
                ["type"] = "json_object"
            };
        }

        return await PostChatCompletionAsync(payload, provider, cancellationToken);
    }

    private async Task<string> PostChatCompletionAsync(Dictionary<string, object?> payload, AiProviderRuntime provider, CancellationToken cancellationToken)
    {
        var client = CreateAiHttpClient(provider);
        using var response = await PostProviderJsonAsync(client, "chat/completions", payload, provider, "AI provider", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var requestedModel = payload.TryGetValue("model", out var m) ? m?.ToString() : "unknown";
            throw new AiProviderException($"AI provider error {(int)response.StatusCode} (Model: '{requestedModel}', Url: '{provider.BaseUrl}'): {TrimProviderError(body, provider.ApiKey)}");
        }

        using var document = JsonDocument.Parse(body);
        var content = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new AiProviderException("AI provider returned an empty response.");
        }

        return content.Trim();
    }

    private static async Task<HttpResponseMessage> PostProviderJsonAsync(
        HttpClient client,
        string requestUri,
        Dictionary<string, object?> payload,
        AiProviderRuntime provider,
        string providerName,
        CancellationToken cancellationToken)
    {
        try
        {
            return await client.PostAsJsonAsync(requestUri, payload, JsonOptions, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiProviderException(
                $"{providerName} request timed out after {client.Timeout.TotalSeconds:0} seconds. Try again later or use a smaller attachment.",
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw new AiProviderException(
                $"{providerName} request failed: {RedactSecret(ex.Message, provider.ApiKey)}",
                ex);
        }
    }

    private HttpClient CreateAiHttpClient(AiProviderRuntime provider)
    {
        if (string.IsNullOrWhiteSpace(provider.ApiKey))
        {
            throw new AiProviderException("Workspace AI provider API key is not configured.");
        }

        var client = httpClientFactory.CreateClient();
        client.BaseAddress = new Uri($"{provider.BaseUrl.TrimEnd('/')}/");
        client.Timeout = ResolveProviderTimeout();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
        return client;
    }

    private TimeSpan ResolveProviderTimeout()
    {
        var timeoutSeconds = configuration.GetValue("AI:ProviderTimeoutSeconds", 180);
        return TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 30, 300));
    }

    private async Task<TaskItem?> TryCreateTaskFromAiResultAsync(
        Guid workspaceId,
        string channelName,
        string command,
        string result,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var projects = await dbContext.Projects
            .Where(project => project.WorkspaceId == workspaceId && project.Status != ProjectStatus.Archived)
            .OrderByDescending(project => project.Status == ProjectStatus.Active)
            .ThenByDescending(project => project.UpdatedAtUtc)
            .ToListAsync(cancellationToken);

        foreach (var project in projects)
        {
            if (!await permissionService.CanCreateTask(userId, project.Id))
            {
                continue;
            }

            var title = ExtractTaskTitle(result, command);
            var task = new TaskItem
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Title = title,
                Description = TrimTo($"Created from @TaskFlow AI in #{channelName}.{Environment.NewLine}{Environment.NewLine}{result}", 2000),
                Priority = TaskPriority.Medium,
                CreatedByUserId = userId
            };
            dbContext.TaskItems.Add(task);
            await dbContext.SaveChangesAsync(cancellationToken);
            return task;
        }

        return null;
    }

    private static string BuildChannelAiPrompt(string channelName, string artifactType, string command, string messageContext, string ragContext)
    {
        return $"""
            You are @TaskFlow AI inside channel #{channelName}.
            The backend has already filtered context to the current user's workspace/channel permissions.
            Use only the provided context below. Do not infer file, folder, image, PDF, URL, or attachment contents unless extracted text or image summaries are present below.
            Do not claim access to private channels or files that are not present in the context.

            Required behavior:
            - Keep normal chat answers concise: 3-6 focused bullets unless the user explicitly asks for a report, PPT, or code plan.
            - Separate project updates, casual chat, attachment context, action items, and missing context.
            - Cite sources for every important claim using the source labels shown below.
            - If attachment content is unavailable, write: "Attachment content was not available to TaskFlow AI."
            - Do not create tasks or claim tasks were created. For task requests, return suggested task titles only.
            - Follow the requested artifact type. For report-outline, write DOCX-ready report content. For ppt-outline, write PPTX-ready slide content. For code-advice, write an implementation plan for the agent approval flow.
            - Respect approval and indexing constraints already enforced by the backend. Do not claim to have approved, deployed, or applied changes.
            - If evidence is weak or missing, say so directly.

            Output format:
            Use compact Markdown. Include Summary, Action Items, Missing Context, and Sources only when that section has useful content.

            Available channel AI tools:
            - summary: summarize recent channel and selected context.
            - requirements: extract requirements, deliverables, grading criteria, and acceptance checks.
            - task-suggestions: propose task titles only.
            - report-outline: generate content for a DOCX report artifact from the selected indexed attachments.
            - ppt-outline: generate content for a PPTX presentation artifact from the selected indexed attachments.
            - code-advice: create a code-advice agent job that requires approval.

            Requested artifact type: {artifactType}
            User command:
            {command}

            Recent channel messages:
            {messageContext}

            Retrieved attachment/file/image context:
            {ragContext}
            """;
    }

    private static string BuildChannelTaskSuggestionPrompt(string channelName, string command, string messageContext, string ragContext)
    {
        return $"""
            You are @TaskFlow AI inside channel #{channelName}.
            The backend has already filtered context to the current user's workspace/channel permissions.
            Use only the provided context below. Do not infer file, folder, image, PDF, URL, or attachment contents unless extracted text or image summaries are present below.

            Return only valid JSON with one top-level property named "tasks".
            The "tasks" value must be an array of strings, for example two verb-led task title strings.

            Task suggestion rules:
            - Provide 3 to 5 concise task titles.
            - Each task title must be one actionable title, 8 to 90 characters.
            - Do not include Markdown, prose, numbering, headings, source labels, explanations, or created-task claims.
            - Do not create tasks. The frontend will create tasks only after user approval.

            User command:
            {command}

            Recent channel messages:
            {messageContext}

            Retrieved attachment/file/image context:
            {ragContext}
            """;
    }

    private static IReadOnlyCollection<string> ExtractSuggestedTasks(string result)
    {
        try
        {
            using var document = JsonDocument.Parse(NormalizeJsonObject(result));
            if (!document.RootElement.TryGetProperty("tasks", out var tasksElement)
                || tasksElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return tasksElement
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => NormalizeTaskTitle(item.GetString() ?? string.Empty))
                .Where(title => title.Length >= 8 && title.Length <= 90)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string NormalizeJsonObject(string result)
    {
        var text = result.Trim();
        if (text.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            text = text[7..].Trim();
        }
        else if (text.StartsWith("```", StringComparison.Ordinal))
        {
            text = text[3..].Trim();
        }

        if (text.EndsWith("```", StringComparison.Ordinal))
        {
            text = text[..^3].Trim();
        }

        return text;
    }

    private static string NormalizeTaskTitle(string title)
    {
        return Regex.Replace(title.Trim(), @"\s+", " ");
    }

    private static string FormatSuggestedTasks(IReadOnlyCollection<string> suggestedTasks)
    {
        var builder = new StringBuilder("Suggested tasks");
        foreach (var task in suggestedTasks)
        {
            builder.AppendLine();
            builder.Append("- ").Append(task);
        }

        return builder.ToString();
    }

    private static string ExtractTaskTitle(string result, string command)
    {
        var candidate = ExtractSuggestedTasks(result).FirstOrDefault()
            ?? result.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => Regex.Replace(line, @"^[-*\d\.\)\s#]+", string.Empty).Trim())
                .FirstOrDefault(line => line.Length >= 6)
            ?? command;

        candidate = Regex.Replace(candidate, @"^(Task|Title|任务|标题)\s*[:：]\s*", string.Empty, RegexOptions.IgnoreCase).Trim();
        return TrimTo(candidate, 200);
    }

    private static string ResolveArtifactType(string command)
    {
        if (IsConversationSummaryCommand(command))
        {
            return "summary";
        }

        if (command.Contains("PPT", StringComparison.OrdinalIgnoreCase)
            || command.Contains("deck", StringComparison.OrdinalIgnoreCase)
            || command.Contains("slides", StringComparison.OrdinalIgnoreCase)
            || command.Contains("presentation", StringComparison.OrdinalIgnoreCase)
            || command.Contains("powerpoint", StringComparison.OrdinalIgnoreCase)
            || command.Contains("幻灯片", StringComparison.OrdinalIgnoreCase)
            || command.Contains("演示", StringComparison.OrdinalIgnoreCase))
        {
            return "ppt-outline";
        }

        if (command.Contains("report", StringComparison.OrdinalIgnoreCase)
            || command.Contains("docx", StringComparison.OrdinalIgnoreCase)
            || command.Contains("document", StringComparison.OrdinalIgnoreCase)
            || command.Contains("报告", StringComparison.OrdinalIgnoreCase)
            || command.Contains("文档", StringComparison.OrdinalIgnoreCase))
        {
            return "report-outline";
        }

        if (command.Contains("requirement", StringComparison.OrdinalIgnoreCase)
            || command.Contains("rubric", StringComparison.OrdinalIgnoreCase)
            || command.Contains("criteria", StringComparison.OrdinalIgnoreCase)
            || command.Contains("deliverable", StringComparison.OrdinalIgnoreCase)
            || command.Contains("acceptance", StringComparison.OrdinalIgnoreCase)
            || command.Contains("grading", StringComparison.OrdinalIgnoreCase)
            || command.Contains("需求", StringComparison.OrdinalIgnoreCase)
            || command.Contains("要求", StringComparison.OrdinalIgnoreCase)
            || command.Contains("交付", StringComparison.OrdinalIgnoreCase)
            || command.Contains("验收", StringComparison.OrdinalIgnoreCase)
            || command.Contains("评分", StringComparison.OrdinalIgnoreCase))
        {
            return "requirements";
        }

        if (IsTaskCommand(command))
        {
            return "task-suggestions";
        }

        if (command.Contains("code", StringComparison.OrdinalIgnoreCase)
            || command.Contains("implementation", StringComparison.OrdinalIgnoreCase)
            || command.Contains("patch", StringComparison.OrdinalIgnoreCase)
            || command.Contains("bug", StringComparison.OrdinalIgnoreCase)
            || command.Contains("fix", StringComparison.OrdinalIgnoreCase)
            || command.Contains("architecture", StringComparison.OrdinalIgnoreCase)
            || command.Contains("代码", StringComparison.OrdinalIgnoreCase)
            || command.Contains("修复", StringComparison.OrdinalIgnoreCase)
            || command.Contains("实现", StringComparison.OrdinalIgnoreCase)
            || command.Contains("架构", StringComparison.OrdinalIgnoreCase))
        {
            return "code-advice";
        }

        if (command.Contains("image", StringComparison.OrdinalIgnoreCase)
            || command.Contains("图片", StringComparison.OrdinalIgnoreCase)
            || command.Contains("附件", StringComparison.OrdinalIgnoreCase)
            || command.Contains("file", StringComparison.OrdinalIgnoreCase))
        {
            return "attachment-summary";
        }

        return "answer";
    }

    private static string NormalizeCommandForIntent(string command)
    {
        return Regex
            .Replace(command, @"@?\s*TaskFlow\s+AI\b\s*[:,：-]?", string.Empty, RegexOptions.IgnoreCase)
            .Trim();
    }

    private static bool IsTaskCommand(string command)
    {
        return Regex.IsMatch(command, @"\b(tasks?|todos?)\b", RegexOptions.IgnoreCase)
            || Regex.IsMatch(command, @"\b(action items?|checklist)\b", RegexOptions.IgnoreCase)
            || command.Contains("任务", StringComparison.OrdinalIgnoreCase)
            || command.Contains("待办", StringComparison.OrdinalIgnoreCase)
            || command.Contains("行动项", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsConversationSummaryCommand(string command)
    {
        return Regex.IsMatch(command, @"\b(summarize|summary)\b", RegexOptions.IgnoreCase)
            || command.Contains("总结", StringComparison.OrdinalIgnoreCase)
            || command.Contains("概括", StringComparison.OrdinalIgnoreCase)
            || command.Contains("Ҷамъбаст", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldUseProModel(string command)
    {
        return command.Contains("code", StringComparison.OrdinalIgnoreCase)
            || command.Contains("architecture", StringComparison.OrdinalIgnoreCase)
            || command.Contains("复杂", StringComparison.OrdinalIgnoreCase)
            || command.Contains("代码", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPlainTextAttachment(string contentType, string extension)
    {
        return contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || extension is ".txt" or ".md" or ".csv" or ".json" or ".log" or ".xml";
    }

    private static IEnumerable<AttachmentIndexSection> BuildIndexChunks(IReadOnlyCollection<AttachmentIndexSection> sections)
    {
        var imageChunks = sections
            .Where(IsImageSection)
            .SelectMany(section => SplitIntoChunks(section.Content)
                .Select(chunk => section with { Content = chunk }))
            .Take(MaxChunksPerAttachment)
            .ToArray();
        var textSectionLimit = Math.Max(0, MaxChunksPerAttachment - imageChunks.Length);
        var emitted = 0;

        foreach (var section in sections.Where(section => !IsImageSection(section)))
        {
            foreach (var chunk in SplitIntoChunks(section.Content))
            {
                if (emitted >= textSectionLimit)
                {
                    break;
                }

                emitted++;
                yield return section with { Content = chunk };
            }
        }

        foreach (var section in imageChunks)
        {
            if (emitted >= MaxChunksPerAttachment)
            {
                yield break;
            }

            emitted++;
            yield return section;
        }
    }

    private static bool IsImageSection(AttachmentIndexSection section)
    {
        return section.SourceType.Contains("image", StringComparison.OrdinalIgnoreCase);
    }

    private static PdfImagePayload? TryExtractPdfImage(IPdfImage image)
    {
        if (image.TryGetPng(out var pngBytes) && pngBytes.Length > 0)
        {
            return new PdfImagePayload("image/png", pngBytes);
        }

        var rawBytes = image.RawBytes.ToArray();
        if (rawBytes.Length > 0 && IsJpeg(rawBytes))
        {
            return new PdfImagePayload("image/jpeg", rawBytes);
        }

        return null;
    }

    private static bool IsJpeg(byte[] bytes)
    {
        return bytes.Length >= 4
            && bytes[0] == 0xFF
            && bytes[1] == 0xD8
            && bytes[^2] == 0xFF
            && bytes[^1] == 0xD9;
    }

    private static IEnumerable<string> SplitIntoChunks(string text)
    {
        text = NormalizeExtractedText(text);
        const int chunkSize = 1400;
        const int overlap = 180;

        for (var start = 0; start < text.Length; start += chunkSize - overlap)
        {
            var length = Math.Min(chunkSize, text.Length - start);
            var chunk = text.Substring(start, length).Trim();
            if (!string.IsNullOrWhiteSpace(chunk))
            {
                yield return chunk;
            }

            if (start + length >= text.Length)
            {
                yield break;
            }
        }
    }

    private static string NormalizeExtractedText(string text)
    {
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private static string TrimTo(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength].Trim();
    }

    private static string ResolveContentType(IFormFile file)
    {
        return string.IsNullOrWhiteSpace(file.ContentType)
            ? "application/octet-stream"
            : file.ContentType.Trim();
    }

    private string ResolveMainModel(AiProviderRuntime provider)
    {
        if (IsGeminiProvider(provider))
        {
            return "gemini-3.5-flash";
        }

        if (IsSiliconFlowProvider(provider))
        {
            return "deepseek-ai/DeepSeek-V4-Flash";
        }

        if (IsGitHubModelsProvider(provider))
        {
            return provider.Model;
        }

        return configuration["AI:MainModel"] ?? configuration["AI:Model"] ?? provider.Model;
    }

    private string ResolveProModel(AiProviderRuntime provider)
    {
        if (IsGeminiProvider(provider))
        {
            return "gemini-3.5-flash";
        }

        if (IsSiliconFlowProvider(provider))
        {
            return "deepseek-ai/DeepSeek-V4-Pro";
        }

        if (IsGitHubModelsProvider(provider))
        {
            return provider.Model;
        }

        return configuration["AI:ProModel"] ?? configuration["AI:MainModel"] ?? configuration["AI:Model"] ?? provider.Model;
    }

    private string ResolveVisionModel(AiProviderRuntime provider)
    {
        if (IsGeminiProvider(provider))
        {
            return "gemini-3.5-flash";
        }

        if (IsSiliconFlowProvider(provider))
        {
            return "Qwen/Qwen3.6-27B";
        }

        if (IsGitHubModelsProvider(provider))
        {
            return provider.Model;
        }

        return configuration["AI:VisionModel"] ?? configuration["AI:Model"] ?? provider.Model;
    }

    private string ResolveEmbeddingModel(AiProviderRuntime provider)
    {
        if (IsGeminiProvider(provider))
        {
            return configuration["AI:GeminiEmbeddingModel"] ?? "gemini-embedding-001";
        }

        if (IsGitHubModelsProvider(provider))
        {
            return configuration["AI:GitHubEmbeddingModel"] ?? "openai/text-embedding-3-small";
        }

        return configuration["AI:EmbeddingModel"] ?? configuration["AI:Model"] ?? provider.Model;
    }

    private static bool IsGeminiProvider(AiProviderRuntime provider)
    {
        return provider.ProviderName.Contains("Google", StringComparison.OrdinalIgnoreCase)
            || provider.ProviderName.Contains("Gemini", StringComparison.OrdinalIgnoreCase)
            || provider.BaseUrl.Contains("generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGitHubModelsProvider(AiProviderRuntime provider)
    {
        return provider.ProviderName.Contains("GitHub", StringComparison.OrdinalIgnoreCase)
            || provider.BaseUrl.Contains("models.github.ai", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSiliconFlowProvider(AiProviderRuntime provider)
    {
        return provider.ProviderName.Contains("Silicon", StringComparison.OrdinalIgnoreCase)
            || provider.BaseUrl.Contains("api.siliconflow.cn", StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimProviderError(string body, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "empty provider error response";
        }

        return TrimTo(RedactSecret(body, apiKey).Replace(Environment.NewLine, " ", StringComparison.Ordinal), 600);
    }

    private static IReadOnlyList<KnowledgeSnippet> RankSnippets(IReadOnlyList<KnowledgeSnippet> snippets, string question, int maxResults)
    {
        var tokens = Tokenize(question);
        if (tokens.Length == 0)
        {
            return snippets.Take(maxResults).ToArray();
        }

        var ranked = snippets
            .Select(snippet => new
            {
                Snippet = snippet,
                Score = tokens.Sum(token => CountMatches(snippet.Text, token) + CountMatches(snippet.Source, token) * 2)
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .Take(maxResults)
            .Select(item => item.Snippet)
            .ToArray();

        return ranked.Length > 0
            ? ranked
            : snippets.Take(maxResults).ToArray();
    }

    private static string[] Tokenize(string text)
    {
        return text
            .Split([' ', '\r', '\n', '\t', '.', ',', ';', ':', '?', '!', '(', ')', '[', ']', '{', '}', '/', '\\', '-', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim().ToLowerInvariant())
            .Where(token => token.Length >= 3)
            .Distinct()
            .ToArray();
    }

    private static int CountMatches(string text, string token)
    {
        var count = 0;
        var start = 0;
        while (start < text.Length)
        {
            var index = text.IndexOf(token, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                break;
            }

            count++;
            start = index + token.Length;
        }

        return count;
    }

    private static string FormatDate(DateTime? value)
    {
        return value?.ToString("yyyy-MM-dd HH:mm") ?? "None";
    }

    private async Task<ApiResponse<AiResponse>> AskLlmOrFail(
        AiProviderRuntime provider,
        string prompt,
        CancellationToken cancellationToken,
        IReadOnlyCollection<string>? sources = null)
    {
        try
        {
            var kernel = BuildKernel(provider);
            var result = await kernel.InvokePromptAsync(prompt, cancellationToken: cancellationToken);
            return ApiResponse.Ok(new AiResponse
            {
                Result = result.ToString(),
                UsedLlm = true,
                Sources = sources ?? []
            });
        }
        catch (Exception ex)
        {
            var model = ResolveMainModel(provider);
            return ApiResponse.Fail<AiResponse>(
                $"AI provider request failed for model '{model}' at '{provider.BaseUrl}': {RedactSecret(ex.Message, provider.ApiKey)}",
                StatusCodes.Status502BadGateway);
        }
    }

    private Kernel BuildKernel(AiProviderRuntime provider)
    {
        var builder = Kernel.CreateBuilder();

        var client = new OpenAIClient(
            new ApiKeyCredential(provider.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(provider.BaseUrl) });

        builder.AddOpenAIChatCompletion(modelId: ResolveMainModel(provider), openAIClient: client);

        var kernel = builder.Build();
        kernel.Plugins.AddFromObject(collaborationAiPlugin, "Collaboration");
        return kernel;
    }

    private static ApiResponse<T> ToProviderFailure<T>(ApiResponse<AiProviderRuntime> providerResult)
    {
        return ApiResponse.Fail<T>(providerResult.Message, providerResult.StatusCode, providerResult.Errors);
    }

    private async Task<Guid?> GetChannelWorkspaceIdAsync(Guid channelId, CancellationToken cancellationToken)
    {
        return await dbContext.Channels
            .AsNoTracking()
            .Where(channel => channel.Id == channelId)
            .Select(channel => (Guid?)channel.WorkspaceId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<Guid?> GetProjectWorkspaceIdAsync(Guid projectId, CancellationToken cancellationToken)
    {
        return await dbContext.Projects
            .AsNoTracking()
            .Where(project => project.Id == projectId)
            .Select(project => (Guid?)project.WorkspaceId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string RedactSecret(string value, string secret)
    {
        return string.IsNullOrWhiteSpace(secret)
            ? value
            : value.Replace(secret, "[redacted]", StringComparison.Ordinal);
    }

    private static bool IsAiGeneratedContent(string? content)
    {
        return !string.IsNullOrWhiteSpace(content)
            && content.StartsWith(IAiCommandService.ChannelAiMessagePrefix, StringComparison.Ordinal);
    }

    private static bool IsAiGeneratedAttachment(ChannelAttachment attachment)
    {
        return IsAiGeneratedContent(attachment.Message?.Content);
    }

    private static IQueryable<ChannelAttachment> WhereContextSourceAttachments(IQueryable<ChannelAttachment> query)
    {
        return query.Where(attachment =>
            attachment.Message == null
            || !attachment.Message.Content.StartsWith(IAiCommandService.ChannelAiMessagePrefix));
    }

    private static IReadOnlyCollection<Guid>? ResolveRequestedAttachmentIds(AiChannelCommandRequest request)
    {
        if (request.AttachmentIds is not null)
        {
            return request.AttachmentIds
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToArray();
        }

        return request.AttachmentId.HasValue && request.AttachmentId.Value != Guid.Empty
            ? [request.AttachmentId.Value]
            : null;
    }

    private sealed record KnowledgeSnippet(string Source, string Text);

    private sealed record AttachmentProcessingResult(string Summary, IReadOnlyCollection<AttachmentIndexSection> Sections);

    private sealed record AttachmentIndexSection(string SourceType, string SourceLabel, string Content);

    private sealed record GeneratedArtifact(string FileName, string ContentType, byte[] Content, string Summary);

    private sealed record GeneratedSlide(string Title, List<string> Body);

    private sealed record AttachmentIndexBuildResult(
        IReadOnlyCollection<ChannelKnowledgeChunk> ChunkEntities,
        IReadOnlyCollection<PineconeKnowledgeChunk> PineconeChunks);

    private sealed record PdfImagePayload(string ContentType, byte[] Bytes);

    private sealed class AiProviderException(string message, Exception? innerException = null)
        : Exception(message, innerException);

    private sealed class AttachmentProcessingException(
        string message,
        int statusCode = StatusCodes.Status400BadRequest) : Exception(message)
    {
        public int StatusCode { get; } = statusCode;
    }
}
