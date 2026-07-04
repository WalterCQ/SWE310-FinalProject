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

namespace TaskFlow.Api.Services;

public class AiCommandService(
    IConfiguration configuration,
    AppDbContext dbContext,
    ICurrentUserService currentUser,
    IPermissionService permissionService,
    IChannelService channelService,
    IDashboardService dashboardService,
    CollaborationAiPlugin collaborationAiPlugin,
    IHttpClientFactory httpClientFactory) : IAiCommandService
{
    private const string DefaultBaseUrl = "https://api.siliconflow.cn/v1";
    private const string DefaultMainModel = "deepseek-ai/DeepSeek-V4-Flash";
    private const string DefaultProModel = "deepseek-ai/DeepSeek-V4-Pro";
    private const string DefaultVisionModel = "Qwen/Qwen3.5-35B-A3B";
    private const string DefaultEmbeddingModel = "Qwen/Qwen3-Embedding-4B";
    private const int MaxUploadBytes = 20_000_000;
    private const int MaxExtractedTextLength = 60_000;
    private const int MaxChunksPerAttachment = 24;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ApiResponse<AiResponse>> ExecuteCommandAsync(AiCommandRequest request, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.GetUserId();
        if (!await permissionService.CanUseAiCommand(userId, request.WorkspaceId))
        {
            return ApiResponse.Fail<AiResponse>("AI command access denied.", StatusCodes.Status403Forbidden);
        }

        var prompt = $"""
            You are the TaskFlow Connect assistant. Use the available collaboration plugin functions only when the user asks to read or modify real app data.
            Workspace id: {request.WorkspaceId}
            User command: {request.Command}
            """;

        return await AskLlmOrFallback(prompt, $"AI provider is not configured. Received command: {request.Command}", cancellationToken);
    }

    public async Task<ApiResponse<AiResponse>> SummarizeChannelAsync(AiChannelSummaryRequest request, CancellationToken cancellationToken = default)
    {
        var messagesResult = await channelService.GetChannelMessagesAsync(request.ChannelId);
        if (!messagesResult.Success || messagesResult.Data is null)
        {
            return ApiResponse.Fail<AiResponse>(messagesResult.Message, messagesResult.StatusCode, messagesResult.Errors);
        }

        var context = string.Join(Environment.NewLine, messagesResult.Data.TakeLast(50).Select(message => $"{message.SenderName}: {message.Content}"));
        var fallback = string.IsNullOrWhiteSpace(context)
            ? "No messages found in this channel yet."
            : $"Recent channel activity includes {messagesResult.Data.Count()} messages. Latest: {messagesResult.Data.Last().Content}";

        return await AskLlmOrFallback($"Summarize this channel discussion clearly for a project team:\n{context}", fallback, cancellationToken);
    }

    public async Task<ApiResponse<AiResponse>> SummarizeProjectAsync(AiProjectSummaryRequest request, CancellationToken cancellationToken = default)
    {
        var dashboardResult = await dashboardService.GetProjectDashboardAsync(request.ProjectId);
        if (!dashboardResult.Success || dashboardResult.Data is null)
        {
            return ApiResponse.Fail<AiResponse>(dashboardResult.Message, dashboardResult.StatusCode, dashboardResult.Errors);
        }

        var dashboard = dashboardResult.Data;
        var fallback = $"Project has {dashboard.TaskCount} tasks, {dashboard.CompletedTaskCount} completed, {dashboard.OverdueTaskCount} overdue, and {dashboard.CompletionRate}% completion.";
        return await AskLlmOrFallback($"Summarize this project dashboard for stakeholders:\n{fallback}", fallback, cancellationToken);
    }

    public async Task<ApiResponse<AiResponse>> AnalyzeProjectRiskAsync(AiRiskAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        var dashboardResult = await dashboardService.GetProjectDashboardAsync(request.ProjectId);
        if (!dashboardResult.Success || dashboardResult.Data is null)
        {
            return ApiResponse.Fail<AiResponse>(dashboardResult.Message, dashboardResult.StatusCode, dashboardResult.Errors);
        }

        var dashboard = dashboardResult.Data;
        var fallback = dashboard.OverdueTaskCount > 0
            ? $"Risk detected: {dashboard.OverdueTaskCount} overdue tasks and {dashboard.CompletionRate}% completion."
            : $"No overdue tasks detected. Completion is {dashboard.CompletionRate}%.";

        return await AskLlmOrFallback($"Analyze delivery risk from this project dashboard:\n{fallback}", fallback, cancellationToken);
    }

    public async Task<ApiResponse<AiResponse>> GenerateTasksFromMessageAsync(AiGenerateTasksFromMessageRequest request, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.GetUserId();
        if (!await permissionService.CanCreateTask(userId, request.ProjectId))
        {
            return ApiResponse.Fail<AiResponse>("You do not have permission to generate tasks for this project.", StatusCodes.Status403Forbidden);
        }

        var content = request.MessageContent;
        if (string.IsNullOrWhiteSpace(content))
        {
            content = "No message content was provided. Ask the frontend to send MessageContent or wire MessageId lookup later.";
        }

        var fallback = $"Suggested task: Review and follow up on: {content}";
        return await AskLlmOrFallback($"Turn this team message into concise task suggestions:\n{content}", fallback, cancellationToken);
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
        var fallback = $"AI provider is not configured. I found related TaskFlow records: {string.Join("; ", sources.Take(5))}.";

        return await AskLlmOrFallback(prompt, fallback, cancellationToken, sources);
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

        if (!HasLlmConfiguration())
        {
            return ApiResponse.Fail<AiChannelCommandResponse>("AI provider is not configured. Set AI:ApiKey on the backend before using @TaskFlow AI.");
        }

        if (request.AttachmentId.HasValue)
        {
            var attachmentAllowed = await dbContext.ChannelAttachments
                .AsNoTracking()
                .AnyAsync(attachment =>
                    attachment.Id == request.AttachmentId.Value
                    && attachment.ChannelId == channelId,
                    cancellationToken);
            if (!attachmentAllowed)
            {
                return ApiResponse.Fail<AiChannelCommandResponse>("Attachment not found in this channel.", StatusCodes.Status404NotFound);
            }
        }

        try
        {
            var recentMessages = await dbContext.Messages
                .AsNoTracking()
                .Include(message => message.Sender)
                .Where(message => message.ChannelId == channelId && !message.IsDeleted)
                .OrderByDescending(message => message.CreatedAtUtc)
                .Take(80)
                .ToListAsync(cancellationToken);
            recentMessages.Reverse();

            var messageContext = recentMessages.Count == 0
                ? "No messages in this channel yet."
                : string.Join(Environment.NewLine, recentMessages.Select(message =>
                    $"- [{FormatDate(message.CreatedAtUtc)} UTC] {message.Sender!.Name}: {message.Content}"));

            var workspaceMessageSnippets = await RetrieveWorkspaceMessageKnowledgeAsync(
                channel.WorkspaceId,
                userId,
                command,
                cancellationToken);
            var attachmentSnippets = await RetrieveChannelKnowledgeAsync(
                channel.WorkspaceId,
                userId,
                command,
                request.AttachmentId,
                cancellationToken);
            var ragSnippets = workspaceMessageSnippets
                .Concat(attachmentSnippets)
                .GroupBy(snippet => $"{snippet.Source}\n{snippet.Text}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Take(12)
                .ToArray();
            var ragContext = ragSnippets.Length == 0
                ? "No accessible workspace messages or indexed attachment chunks matched this request."
                : string.Join(Environment.NewLine + Environment.NewLine, ragSnippets.Select((snippet, index) =>
                    $"{index + 1}. Source: {snippet.Source}{Environment.NewLine}{snippet.Text}"));

            var artifactType = ResolveArtifactType(command);
            var prompt = BuildChannelAiPrompt(channel.Name, artifactType, command, messageContext, ragContext);
            var model = ShouldUseProModel(command) ? ResolveProModel() : ResolveMainModel();
            var result = await InvokeChatCompletionAsync(
                model,
                "You are TaskFlow AI, a channel member inside a project collaboration app. Use only the provided accessible context.",
                prompt,
                cancellationToken);

            var sources = ragSnippets
                .Select(snippet => snippet.Source)
                .Concat(recentMessages.Count == 0 ? [] : [$"#{channel.Name} recent messages"])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var suggestedTasks = IsTaskCommand(command)
                ? ExtractSuggestedTasks(result)
                : [];
            var createdTask = IsTaskCommand(command)
                ? await TryCreateTaskFromAiResultAsync(channel.WorkspaceId, channel.Name, command, result, userId, cancellationToken)
                : null;

            return ApiResponse.Ok(new AiChannelCommandResponse
            {
                Result = result,
                ArtifactType = artifactType,
                UsedLlm = true,
                Sources = sources,
                SuggestedTasks = suggestedTasks,
                CreatedTaskId = createdTask?.Id,
                CreatedTaskTitle = createdTask?.Title
            });
        }
        catch (AttachmentProcessingException ex)
        {
            return ApiResponse.Fail<AiChannelCommandResponse>(ex.Message, ex.StatusCode);
        }
        catch (AiProviderException ex)
        {
            return ApiResponse.Fail<AiChannelCommandResponse>(ex.Message, StatusCodes.Status502BadGateway);
        }
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

        if (!HasLlmConfiguration())
        {
            return ApiResponse.Fail<ChannelAttachmentResponse>("AI provider is not configured. Set AI:ApiKey on the backend before uploading AI-indexed attachments.");
        }

        if (file is null || file.Length == 0)
        {
            return ApiResponse.Fail<ChannelAttachmentResponse>("Attachment file is required.");
        }

        if (file.Length > MaxUploadBytes)
        {
            return ApiResponse.Fail<ChannelAttachmentResponse>("Attachment exceeds the 20 MB upload limit.", StatusCodes.Status413PayloadTooLarge);
        }

        try
        {
            var processed = await ProcessAttachmentAsync(file, cancellationToken);
            var chunks = SplitIntoChunks(processed.IndexText)
                .Take(MaxChunksPerAttachment)
                .ToArray();
            if (chunks.Length == 0)
            {
                return ApiResponse.Fail<ChannelAttachmentResponse>("Attachment did not contain indexable content.");
            }

            var attachment = new ChannelAttachment
            {
                Id = Guid.NewGuid(),
                WorkspaceId = channel.WorkspaceId,
                ChannelId = channelId,
                UploadedByUserId = userId,
                FileName = Path.GetFileName(file.FileName),
                ContentType = ResolveContentType(file),
                SizeBytes = file.Length,
                Summary = TrimTo(processed.Summary, 4000),
                ExtractedText = TrimTo(processed.IndexText, MaxExtractedTextLength)
            };

            dbContext.ChannelAttachments.Add(attachment);
            foreach (var chunk in chunks)
            {
                var embedding = await GenerateEmbeddingAsync(chunk, cancellationToken);
                dbContext.ChannelKnowledgeChunks.Add(new ChannelKnowledgeChunk
                {
                    Id = Guid.NewGuid(),
                    WorkspaceId = channel.WorkspaceId,
                    ChannelId = channelId,
                    AttachmentId = attachment.Id,
                    SourceType = processed.SourceType,
                    SourceLabel = attachment.FileName,
                    Content = TrimTo(chunk, 2500),
                    EmbeddingJson = JsonSerializer.Serialize(embedding, JsonOptions)
                });
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return ApiResponse.Created(ToAttachmentResponse(attachment), "Attachment indexed for channel AI.");
        }
        catch (AttachmentProcessingException ex)
        {
            return ApiResponse.Fail<ChannelAttachmentResponse>(ex.Message, ex.StatusCode);
        }
        catch (AiProviderException ex)
        {
            return ApiResponse.Fail<ChannelAttachmentResponse>(ex.Message, StatusCodes.Status502BadGateway);
        }
    }

    public async Task<ApiResponse<IReadOnlyCollection<ChannelAttachmentResponse>>> ListChannelAttachmentsAsync(Guid channelId, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.GetUserId();
        if (!await permissionService.CanAccessChannel(userId, channelId))
        {
            return ApiResponse.Fail<IReadOnlyCollection<ChannelAttachmentResponse>>("Channel not found or access denied.", StatusCodes.Status404NotFound);
        }

        var attachments = await dbContext.ChannelAttachments
            .AsNoTracking()
            .Where(attachment => attachment.ChannelId == channelId)
            .OrderByDescending(attachment => attachment.CreatedAtUtc)
            .Take(30)
            .ToListAsync(cancellationToken);

        IReadOnlyCollection<ChannelAttachmentResponse> response = attachments
            .Select(ToAttachmentResponse)
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
        Guid userId,
        string query,
        Guid? attachmentId,
        CancellationToken cancellationToken)
    {
        var chunksQuery = dbContext.ChannelKnowledgeChunks
            .AsNoTracking()
            .Where(chunk =>
                chunk.WorkspaceId == workspaceId
                && (!chunk.Channel!.IsPrivate || chunk.Channel.Members.Any(member => member.UserId == userId)));

        if (attachmentId.HasValue)
        {
            chunksQuery = chunksQuery.Where(chunk => chunk.AttachmentId == attachmentId.Value);
        }

        var chunks = await chunksQuery
            .OrderByDescending(chunk => chunk.CreatedAtUtc)
            .Take(160)
            .Select(chunk => new
            {
                chunk.SourceLabel,
                chunk.SourceType,
                chunk.Content,
                chunk.EmbeddingJson,
                ChannelName = chunk.Channel!.Name,
                chunk.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        if (chunks.Count == 0)
        {
            return [];
        }

        var tokens = Tokenize(query);
        float[]? queryEmbedding = null;
        if (chunks.Any(chunk => !string.IsNullOrWhiteSpace(chunk.EmbeddingJson)))
        {
            queryEmbedding = await GenerateEmbeddingAsync(query, cancellationToken);
        }

        return chunks
            .Select(chunk =>
            {
                var embedding = ParseEmbedding(chunk.EmbeddingJson);
                var keywordScore = tokens.Sum(token =>
                    CountMatches(chunk.Content, token) + CountMatches(chunk.SourceLabel, token) * 2);
                var embeddingScore = queryEmbedding is not null && embedding is not null
                    ? Math.Max(0, CosineSimilarity(queryEmbedding, embedding)) * 12
                    : 0;
                var recencyScore = Math.Max(0, 1 - (DateTime.UtcNow - chunk.CreatedAtUtc).TotalDays / 30);

                return new
                {
                    Snippet = new KnowledgeSnippet(
                        $"{chunk.SourceType}: {chunk.SourceLabel} in #{chunk.ChannelName}",
                        chunk.Content),
                    Score = keywordScore + embeddingScore + recencyScore
                };
            })
            .Where(item => item.Score > 0 || tokens.Length == 0)
            .OrderByDescending(item => item.Score)
            .Take(8)
            .Select(item => item.Snippet)
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

    private async Task<AttachmentProcessingResult> ProcessAttachmentAsync(IFormFile file, CancellationToken cancellationToken)
    {
        var contentType = ResolveContentType(file);
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            await using var stream = file.OpenReadStream();
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, cancellationToken);
            var summary = await SummarizeImageAsync(memoryStream.ToArray(), contentType, file.FileName, cancellationToken);
            return new AttachmentProcessingResult("image", summary, summary);
        }

        if (IsPlainTextAttachment(contentType, extension))
        {
            var text = await ReadPlainTextAttachmentAsync(file, cancellationToken);
            var summary = await SummarizeTextAttachmentAsync(text, file.FileName, cancellationToken);
            return new AttachmentProcessingResult("file", summary, text);
        }

        if (extension == ".docx" || contentType.Equals("application/vnd.openxmlformats-officedocument.wordprocessingml.document", StringComparison.OrdinalIgnoreCase))
        {
            var text = await ExtractDocxTextAsync(file, cancellationToken);
            var summary = await SummarizeTextAttachmentAsync(text, file.FileName, cancellationToken);
            return new AttachmentProcessingResult("file", summary, text);
        }

        if (extension == ".pdf" || contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new AttachmentProcessingException(
                "PDF parsing is not enabled because no stable server-side PDF text parser is installed. Upload TXT, DOCX, or an image for this v1 AI flow.",
                StatusCodes.Status415UnsupportedMediaType);
        }

        throw new AttachmentProcessingException(
            $"Unsupported attachment type: {contentType}. Supported types are images, TXT/MD/CSV/JSON/XML, and DOCX.",
            StatusCodes.Status415UnsupportedMediaType);
    }

    private async Task<string> SummarizeImageAsync(byte[] imageBytes, string contentType, string fileName, CancellationToken cancellationToken)
    {
        var imageDataUrl = $"data:{contentType};base64,{Convert.ToBase64String(imageBytes)}";
        var payload = new Dictionary<string, object?>
        {
            ["model"] = ResolveVisionModel(),
            ["temperature"] = 0.1,
            ["max_tokens"] = 900,
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
                            ["text"] = $"Summarize image attachment '{fileName}'. Include visible text, objects, UI state, tasks, risks, and any action items. Do not invent unseen details."
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

        return await PostChatCompletionAsync(payload, cancellationToken);
    }

    private async Task<string> SummarizeTextAttachmentAsync(string text, string fileName, CancellationToken cancellationToken)
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
            ResolveMainModel(),
            "You create concise, grounded summaries of uploaded project files.",
            prompt,
            cancellationToken);
    }

    private async Task<string> ReadPlainTextAttachmentAsync(IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = await reader.ReadToEndAsync(cancellationToken);
        text = NormalizeExtractedText(text);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new AttachmentProcessingException("Attachment text extraction returned no content.");
        }

        return TrimTo(text, MaxExtractedTextLength);
    }

    private static async Task<string> ExtractDocxTextAsync(IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
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

    private async Task<float[]> GenerateEmbeddingAsync(string input, CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = ResolveEmbeddingModel(),
            ["input"] = new[] { TrimTo(input, 2000) }
        };
        var client = CreateAiHttpClient();
        using var response = await client.PostAsJsonAsync("embeddings", payload, JsonOptions, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new AiProviderException($"Embedding provider error {(int)response.StatusCode}: {TrimProviderError(body)}");
        }

        using var document = JsonDocument.Parse(body);
        var embeddingElement = document.RootElement
            .GetProperty("data")[0]
            .GetProperty("embedding");
        return embeddingElement.EnumerateArray()
            .Select(value => value.GetSingle())
            .ToArray();
    }

    private async Task<string> InvokeChatCompletionAsync(
        string model,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
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

        return await PostChatCompletionAsync(payload, cancellationToken);
    }

    private async Task<string> PostChatCompletionAsync(Dictionary<string, object?> payload, CancellationToken cancellationToken)
    {
        var client = CreateAiHttpClient();
        using var response = await client.PostAsJsonAsync("chat/completions", payload, JsonOptions, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new AiProviderException($"AI provider error {(int)response.StatusCode}: {TrimProviderError(body)}");
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

    private HttpClient CreateAiHttpClient()
    {
        var apiKey = configuration["AI:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new AiProviderException("AI provider is not configured. Set AI:ApiKey on the backend.");
        }

        var client = httpClientFactory.CreateClient();
        client.BaseAddress = new Uri($"{ResolveBaseUrl().TrimEnd('/')}/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return client;
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
            Do not claim access to private channels or files that are not present in the context.
            If the user asks for a report or PPT, generate a structured outline only, not a real file.
            If the user asks for code, provide code advice or a patch plan as text only.
            If the user asks to generate tasks, return concise actionable task titles first.
            Always include a short "Sources" section naming the provided sources you used.

            Requested artifact type: {artifactType}
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
        return result
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => Regex.Replace(line, @"^[-*\d\.\)\s]+", string.Empty).Trim())
            .Where(line => line.Length >= 6 && line.Length <= 180)
            .Where(line =>
                line.Contains("task", StringComparison.OrdinalIgnoreCase)
                || line.Contains("todo", StringComparison.OrdinalIgnoreCase)
                || line.Contains("follow", StringComparison.OrdinalIgnoreCase)
                || line.Contains('任')
                || line.Contains('做')
                || line.Contains('跟'))
            .Take(5)
            .ToArray();
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
        if (command.Contains("PPT", StringComparison.OrdinalIgnoreCase)
            || command.Contains("slides", StringComparison.OrdinalIgnoreCase)
            || command.Contains("presentation", StringComparison.OrdinalIgnoreCase)
            || command.Contains("幻灯片", StringComparison.OrdinalIgnoreCase))
        {
            return "ppt-outline";
        }

        if (command.Contains("report", StringComparison.OrdinalIgnoreCase)
            || command.Contains("报告", StringComparison.OrdinalIgnoreCase))
        {
            return "report-outline";
        }

        if (IsTaskCommand(command))
        {
            return "task-suggestions";
        }

        if (command.Contains("code", StringComparison.OrdinalIgnoreCase)
            || command.Contains("代码", StringComparison.OrdinalIgnoreCase))
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

    private static bool IsTaskCommand(string command)
    {
        return command.Contains("task", StringComparison.OrdinalIgnoreCase)
            || command.Contains("todo", StringComparison.OrdinalIgnoreCase)
            || command.Contains("任务", StringComparison.OrdinalIgnoreCase)
            || command.Contains("待办", StringComparison.OrdinalIgnoreCase);
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

    private string ResolveBaseUrl()
    {
        return configuration["AI:BaseUrl"]
            ?? configuration["AI:Endpoint"]
            ?? DefaultBaseUrl;
    }

    private string ResolveMainModel()
    {
        return configuration["AI:Model"]
            ?? configuration["AI:MainModel"]
            ?? DefaultMainModel;
    }

    private string ResolveProModel()
    {
        return configuration["AI:ProModel"] ?? DefaultProModel;
    }

    private string ResolveVisionModel()
    {
        return configuration["AI:VisionModel"] ?? DefaultVisionModel;
    }

    private string ResolveEmbeddingModel()
    {
        return configuration["AI:EmbeddingModel"] ?? DefaultEmbeddingModel;
    }

    private static ChannelAttachmentResponse ToAttachmentResponse(ChannelAttachment attachment)
    {
        return new ChannelAttachmentResponse
        {
            Id = attachment.Id,
            ChannelId = attachment.ChannelId,
            FileName = attachment.FileName,
            ContentType = attachment.ContentType,
            SizeBytes = attachment.SizeBytes,
            Summary = attachment.Summary,
            CreatedAtUtc = attachment.CreatedAtUtc
        };
    }

    private static float[]? ParseEmbedding(string? embeddingJson)
    {
        if (string.IsNullOrWhiteSpace(embeddingJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<float[]>(embeddingJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static double CosineSimilarity(float[] left, float[] right)
    {
        if (left.Length == 0 || left.Length != right.Length)
        {
            return 0;
        }

        double dot = 0;
        double leftMagnitude = 0;
        double rightMagnitude = 0;
        for (var index = 0; index < left.Length; index++)
        {
            dot += left[index] * right[index];
            leftMagnitude += left[index] * left[index];
            rightMagnitude += right[index] * right[index];
        }

        if (leftMagnitude == 0 || rightMagnitude == 0)
        {
            return 0;
        }

        return dot / (Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude));
    }

    private static string TrimProviderError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "empty provider error response";
        }

        return TrimTo(body.Replace(Environment.NewLine, " ", StringComparison.Ordinal), 600);
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

    private async Task<ApiResponse<AiResponse>> AskLlmOrFallback(
        string prompt,
        string fallback,
        CancellationToken cancellationToken,
        IReadOnlyCollection<string>? sources = null)
    {
        if (!HasLlmConfiguration())
        {
            return ApiResponse.Ok(new AiResponse
            {
                Result = fallback,
                UsedLlm = false,
                Sources = sources ?? []
            });
        }

        try
        {
            var kernel = BuildKernel();
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
            return ApiResponse.Ok(new AiResponse
            {
                Result = $"{fallback} LLM call failed: {ex.Message}",
                UsedLlm = false,
                Sources = sources ?? []
            });
        }
    }

    private Kernel BuildKernel()
    {
        var builder = Kernel.CreateBuilder();
        var apiKey = configuration["AI:ApiKey"];
        var model = ResolveMainModel();
        var endpoint = ResolveBaseUrl();

        var client = new OpenAIClient(
            new ApiKeyCredential(apiKey!),
            new OpenAIClientOptions { Endpoint = new Uri(endpoint) });

        builder.AddOpenAIChatCompletion(modelId: model, openAIClient: client);

        var kernel = builder.Build();
        kernel.Plugins.AddFromObject(collaborationAiPlugin, "Collaboration");
        return kernel;
    }

    private bool HasLlmConfiguration()
    {
        return !string.IsNullOrWhiteSpace(configuration["AI:ApiKey"]);
    }

    private sealed record KnowledgeSnippet(string Source, string Text);

    private sealed record AttachmentProcessingResult(string SourceType, string Summary, string IndexText);

    private sealed class AiProviderException(string message) : Exception(message);

    private sealed class AttachmentProcessingException(
        string message,
        int statusCode = StatusCodes.Status400BadRequest) : Exception(message)
    {
        public int StatusCode { get; } = statusCode;
    }
}
