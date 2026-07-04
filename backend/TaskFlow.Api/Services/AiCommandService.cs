using Microsoft.SemanticKernel;
using Microsoft.EntityFrameworkCore;
using OpenAI;
using System.ClientModel;
using TaskFlow.Api.Data;
using TaskFlow.Api.DTOs.AI;
using TaskFlow.Api.Helpers;
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
    CollaborationAiPlugin collaborationAiPlugin) : IAiCommandService
{
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
        var model = configuration["AI:Model"] ?? "gpt-4o-mini";
        var endpoint = configuration["AI:Endpoint"];

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            builder.AddOpenAIChatCompletion(modelId: model, apiKey: apiKey!);
        }
        else
        {
            var client = new OpenAIClient(
                new ApiKeyCredential(apiKey!),
                new OpenAIClientOptions { Endpoint = new Uri(endpoint) });

            builder.AddOpenAIChatCompletion(modelId: model, openAIClient: client);
        }

        var kernel = builder.Build();
        kernel.Plugins.AddFromObject(collaborationAiPlugin, "Collaboration");
        return kernel;
    }

    private bool HasLlmConfiguration()
    {
        return !string.IsNullOrWhiteSpace(configuration["AI:ApiKey"]);
    }

    private sealed record KnowledgeSnippet(string Source, string Text);
}
