using Microsoft.SemanticKernel;
using OpenAI;
using System.ClientModel;
using TaskFlow.Api.DTOs.AI;
using TaskFlow.Api.Helpers;
using TaskFlow.Api.Plugins;
using TaskFlow.Api.Services.Interfaces;

namespace TaskFlow.Api.Services;

public class AiCommandService(
    IConfiguration configuration,
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

    private async Task<ApiResponse<AiResponse>> AskLlmOrFallback(string prompt, string fallback, CancellationToken cancellationToken)
    {
        if (!HasLlmConfiguration())
        {
            return ApiResponse.Ok(new AiResponse { Result = fallback, UsedLlm = false });
        }

        try
        {
            var kernel = BuildKernel();
            var result = await kernel.InvokePromptAsync(prompt, cancellationToken: cancellationToken);
            return ApiResponse.Ok(new AiResponse { Result = result.ToString(), UsedLlm = true });
        }
        catch (Exception ex)
        {
            return ApiResponse.Ok(new AiResponse
            {
                Result = $"{fallback} LLM call failed: {ex.Message}",
                UsedLlm = false
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
}
