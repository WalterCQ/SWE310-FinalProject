using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using TaskFlow.Api.Data;
using TaskFlow.Api.Helpers;
using TaskFlow.Api.Services.Interfaces;

namespace TaskFlow.Api.Services;

public class AiContextService(
    IConfiguration configuration,
    AppDbContext dbContext,
    ICurrentUserService currentUser,
    IPermissionService permissionService,
    IAiProviderService aiProviderService,
    IPineconeVectorStore pineconeVectorStore,
    IHttpClientFactory httpClientFactory) : IAiContextService
{
    private const int RecentChannelMessageLimit = 80;
    private const int WorkspaceMessageScanLimit = 180;
    private const int RankedWorkspaceMessageLimit = 8;
    private const int SelectedAttachmentChunkLimit = 12;
    private const int ChannelChunkLimit = 8;
    private const int ProjectSnapshotLimit = 8;
    private const int TaskSnapshotLimit = 16;

    public async Task<ApiResponse<AiChannelContext>> BuildChannelContextAsync(
        Guid channelId,
        string query,
        IReadOnlyCollection<Guid>? attachmentIds = null,
        CancellationToken cancellationToken = default,
        bool channelOnly = false)
    {
        var userId = currentUser.GetUserId();
        var channel = await dbContext.Channels
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == channelId, cancellationToken);
        if (channel is null || !await permissionService.CanAccessChannel(userId, channelId))
        {
            return ApiResponse.Fail<AiChannelContext>("Channel not found or access denied.", StatusCodes.Status404NotFound);
        }

        var recentMessages = await dbContext.Messages
            .AsNoTracking()
            .Include(message => message.Sender)
            .Where(message =>
                message.ChannelId == channelId
                && !message.IsDeleted
                && !message.Content.StartsWith(IAiCommandService.ChannelAiMessagePrefix))
            .OrderByDescending(message => message.CreatedAtUtc)
            .Take(RecentChannelMessageLimit)
            .ToListAsync(cancellationToken);
        recentMessages.Reverse();

        var recentMessageContext = recentMessages.Count == 0
            ? "No human-authored messages in this channel yet."
            : string.Join(Environment.NewLine, recentMessages.Select(message =>
                $"- [{FormatDate(message.CreatedAtUtc)} UTC] {message.Sender!.Name}: {message.Content}"));

        var snippets = new List<ContextSnippet>();
        if (!channelOnly)
        {
            snippets.AddRange(await BuildWorkspaceMessageSnippetsAsync(channel.WorkspaceId, userId, query, cancellationToken));
        }

        var attachmentSnippetsResult = await BuildAttachmentSnippetsAsync(channel.WorkspaceId, channelId, query, attachmentIds, cancellationToken);
        if (!attachmentSnippetsResult.Success || attachmentSnippetsResult.Data is null)
        {
            return ApiResponse.Fail<AiChannelContext>(
                attachmentSnippetsResult.Message,
                attachmentSnippetsResult.StatusCode,
                attachmentSnippetsResult.Errors);
        }
        snippets.AddRange(attachmentSnippetsResult.Data);
        if (!channelOnly)
        {
            snippets.AddRange(await BuildProjectTaskSnippetsAsync(channel.WorkspaceId, cancellationToken));
        }

        var selected = RankSnippets(snippets, query, maxResults: 14)
            .GroupBy(snippet => $"{snippet.Source}\n{snippet.Text}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        var retrievedContext = selected.Length == 0
            ? channelOnly
                ? "No indexed attachment chunks matched this channel summary request."
                : "No accessible workspace messages, indexed attachment chunks, projects, or tasks matched this request."
            : string.Join(Environment.NewLine + Environment.NewLine, selected.Select((snippet, index) =>
                $"{index + 1}. Source: {snippet.Source}{Environment.NewLine}{snippet.Text}"));

        var sources = selected
            .Select(snippet => snippet.Source)
            .Concat(recentMessages.Count == 0 ? [] : [$"#{channel.Name} recent messages"])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return ApiResponse.Ok(new AiChannelContext(
            channel.WorkspaceId,
            channel.Id,
            channel.Name,
            recentMessageContext,
            retrievedContext,
            sources,
            selected.Select(snippet => $"{snippet.Source}: {snippet.Text}").ToArray()));
    }

    private async Task<IReadOnlyCollection<ContextSnippet>> BuildWorkspaceMessageSnippetsAsync(
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
            .Take(WorkspaceMessageScanLimit)
            .Select(message => new
            {
                ChannelName = message.Channel!.Name,
                SenderName = message.Sender!.Name,
                message.Content,
                message.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        var snippets = messages.Select(message => new ContextSnippet(
            $"Message: #{message.ChannelName}",
            $"Message in #{message.ChannelName} from {message.SenderName} at {FormatDate(message.CreatedAtUtc)} UTC: {message.Content}"));

        return RankSnippets(snippets, query, RankedWorkspaceMessageLimit);
    }

    private async Task<ApiResponse<IReadOnlyCollection<ContextSnippet>>> BuildAttachmentSnippetsAsync(
        Guid workspaceId,
        Guid channelId,
        string queryText,
        IReadOnlyCollection<Guid>? attachmentIds,
        CancellationToken cancellationToken)
    {
        var query = dbContext.ChannelKnowledgeChunks
            .AsNoTracking()
            .Where(chunk =>
                chunk.WorkspaceId == workspaceId
                && chunk.ChannelId == channelId
                && (chunk.Attachment == null
                    || chunk.Attachment.Message == null
                    || !chunk.Attachment.Message.Content.StartsWith(IAiCommandService.ChannelAiMessagePrefix)));

        if (attachmentIds is not null)
        {
            var selectedAttachmentIds = NormalizeAttachmentIds(attachmentIds);
            if (selectedAttachmentIds.Length == 0)
            {
                return ApiResponse.Ok<IReadOnlyCollection<ContextSnippet>>([]);
            }

            var selectedAttachments = await dbContext.ChannelAttachments
                .AsNoTracking()
                .Include(attachment => attachment.Message)
                .Where(attachment =>
                    selectedAttachmentIds.Contains(attachment.Id)
                    && attachment.WorkspaceId == workspaceId
                    && attachment.ChannelId == channelId)
                .ToListAsync(cancellationToken);
            if (selectedAttachments.Count != selectedAttachmentIds.Length)
            {
                return ApiResponse.Fail<IReadOnlyCollection<ContextSnippet>>(
                    "Attachment not found in this channel.",
                    StatusCodes.Status404NotFound);
            }

            if (selectedAttachments.Any(attachment => IsAiGeneratedContent(attachment.Message?.Content)))
            {
                return ApiResponse.Fail<IReadOnlyCollection<ContextSnippet>>(
                    "AI-generated outputs cannot be selected as channel context.",
                    StatusCodes.Status400BadRequest);
            }

            if (selectedAttachments.Any(attachment => !attachment.IsAiIndexed))
            {
                return ApiResponse.Fail<IReadOnlyCollection<ContextSnippet>>(
                    "Only indexed attachments can be selected as AI context.",
                    StatusCodes.Status400BadRequest);
            }

            var selectedAttachmentRows = await query
                .Where(chunk => chunk.AttachmentId.HasValue && selectedAttachmentIds.Contains(chunk.AttachmentId.Value))
                .OrderBy(chunk => chunk.SourceLabel)
                .ThenBy(chunk => chunk.CreatedAtUtc)
                .Select(chunk => new
                {
                    chunk.AttachmentId,
                    Snippet = new ContextSnippet(
                        $"{chunk.SourceType}: {chunk.SourceLabel}",
                        chunk.Content)
                })
                .ToListAsync(cancellationToken);

            IReadOnlyCollection<ContextSnippet> selectedAttachmentSnippets = selectedAttachmentRows
                .GroupBy(row => row.AttachmentId)
                .SelectMany(group => group.Take(SelectedAttachmentChunkLimit))
                .Select(row => row.Snippet)
                .ToArray();

            return ApiResponse.Ok(selectedAttachmentSnippets);
        }

        var allowedAttachmentIds = await query
            .Where(chunk => chunk.AttachmentId.HasValue)
            .Select(chunk => chunk.AttachmentId!.Value)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        if (allowedAttachmentIds.Length == 0)
        {
            return ApiResponse.Ok<IReadOnlyCollection<ContextSnippet>>([]);
        }

        var providerResult = await aiProviderService.ResolveWorkspaceProviderAsync(workspaceId, cancellationToken);
        if (!providerResult.Success || providerResult.Data is null)
        {
            return ApiResponse.Fail<IReadOnlyCollection<ContextSnippet>>(
                providerResult.Message,
                providerResult.StatusCode,
                providerResult.Errors);
        }

        try
        {
            var embedding = await GenerateEmbeddingAsync(queryText, providerResult.Data, cancellationToken);
            var matches = await pineconeVectorStore.SearchAsync(
                workspaceId,
                channelId,
                null,
                embedding,
                ChannelChunkLimit,
                cancellationToken);

            IReadOnlyCollection<ContextSnippet> snippets = matches
                .Where(match => match.AttachmentId.HasValue && allowedAttachmentIds.Contains(match.AttachmentId.Value))
                .Select(match => new ContextSnippet(match.Source, match.Text))
                .ToArray();
            return ApiResponse.Ok(snippets);
        }
        catch (AiContextEmbeddingException ex)
        {
            return ApiResponse.Fail<IReadOnlyCollection<ContextSnippet>>(ex.Message, StatusCodes.Status502BadGateway);
        }
        catch (PineconeVectorStoreException ex)
        {
            return ApiResponse.Fail<IReadOnlyCollection<ContextSnippet>>(ex.Message, StatusCodes.Status502BadGateway);
        }
    }

    private static Guid[] NormalizeAttachmentIds(IEnumerable<Guid> attachmentIds)
    {
        return attachmentIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
    }

    private static bool IsAiGeneratedContent(string? content)
    {
        return !string.IsNullOrWhiteSpace(content)
            && content.StartsWith(IAiCommandService.ChannelAiMessagePrefix, StringComparison.Ordinal);
    }

    private async Task<IReadOnlyCollection<ContextSnippet>> BuildProjectTaskSnippetsAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var snippets = new List<ContextSnippet>();
        var projects = await dbContext.Projects
            .AsNoTracking()
            .Where(project => project.WorkspaceId == workspaceId)
            .OrderByDescending(project => project.UpdatedAtUtc)
            .Take(ProjectSnapshotLimit)
            .Select(project => new
            {
                project.Id,
                project.Name,
                project.Status,
                project.DeadlineUtc
            })
            .ToListAsync(cancellationToken);

        snippets.AddRange(projects.Select(project => new ContextSnippet(
            $"Project: {project.Name}",
            $"Project {project.Name} is {project.Status}. Deadline: {FormatDate(project.DeadlineUtc)} UTC.")));

        var projectIds = projects.Select(project => project.Id).ToArray();
        var tasks = await dbContext.TaskItems
            .AsNoTracking()
            .Where(task => projectIds.Contains(task.ProjectId))
            .OrderByDescending(task => task.UpdatedAtUtc)
            .Take(TaskSnapshotLimit)
            .Select(task => new
            {
                task.Title,
                task.Description,
                task.Status,
                task.Priority,
                task.DeadlineUtc
            })
            .ToListAsync(cancellationToken);

        snippets.AddRange(tasks.Select(task => new ContextSnippet(
            $"Task: {task.Title}",
            $"Task {task.Title}: status {task.Status}, priority {task.Priority}, deadline {FormatDate(task.DeadlineUtc)} UTC. {task.Description}")));

        return snippets;
    }

    private static IReadOnlyCollection<ContextSnippet> RankSnippets(IEnumerable<ContextSnippet> snippets, string query, int maxResults)
    {
        var tokens = Tokenize(query);
        var ranked = snippets
            .Select(snippet => new
            {
                Snippet = snippet,
                Score = tokens.Count(token =>
                    snippet.Source.Contains(token, StringComparison.OrdinalIgnoreCase)
                    || snippet.Text.Contains(token, StringComparison.OrdinalIgnoreCase))
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Snippet.Source)
            .Take(maxResults)
            .Select(item => item.Snippet)
            .ToArray();

        return ranked.Length == 0
            ? snippets.Take(maxResults).ToArray()
            : ranked;
    }

    private static IReadOnlyCollection<string> Tokenize(string text)
    {
        return Regex.Split(text.ToLowerInvariant(), @"[^\p{L}\p{N}]+")
            .Where(token => token.Length >= 2)
            .Distinct()
            .Take(16)
            .ToArray();
    }

    private static string FormatDate(DateTime? value)
    {
        return value?.ToString("yyyy-MM-dd HH:mm") ?? "None";
    }

    private async Task<float[]> GenerateEmbeddingAsync(string input, AiProviderRuntime provider, CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = ResolveEmbeddingModel(provider),
            ["input"] = new[] { TrimTo(input, 2000) }
        };
        var client = httpClientFactory.CreateClient();
        client.BaseAddress = new Uri($"{provider.BaseUrl.TrimEnd('/')}/");
        client.Timeout = ResolveProviderTimeout();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);

        using var response = await PostEmbeddingRequestAsync(client, payload, provider, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new AiContextEmbeddingException($"Embedding provider error {(int)response.StatusCode}: {TrimProviderError(body, provider.ApiKey)}");
        }

        using var document = JsonDocument.Parse(body);
        var embeddingElement = document.RootElement
            .GetProperty("data")[0]
            .GetProperty("embedding");
        return embeddingElement.EnumerateArray()
            .Select(value => value.GetSingle())
            .ToArray();
    }

    private static async Task<HttpResponseMessage> PostEmbeddingRequestAsync(
        HttpClient client,
        Dictionary<string, object?> payload,
        AiProviderRuntime provider,
        CancellationToken cancellationToken)
    {
        try
        {
            return await client.PostAsJsonAsync("embeddings", payload, JsonOptions, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiContextEmbeddingException(
                $"Embedding provider request timed out after {client.Timeout.TotalSeconds:0} seconds. Try again later.",
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw new AiContextEmbeddingException(
                $"Embedding provider request failed: {RedactSecret(ex.Message, provider.ApiKey)}",
                ex);
        }
    }

    private string ResolveEmbeddingModel(AiProviderRuntime provider)
    {
        if (provider.ProviderName.Contains("Google", StringComparison.OrdinalIgnoreCase)
            || provider.ProviderName.Contains("Gemini", StringComparison.OrdinalIgnoreCase)
            || provider.BaseUrl.Contains("generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase))
        {
            return configuration["AI:GeminiEmbeddingModel"] ?? "gemini-embedding-001";
        }

        if (provider.ProviderName.Contains("GitHub", StringComparison.OrdinalIgnoreCase)
            || provider.BaseUrl.Contains("models.github.ai", StringComparison.OrdinalIgnoreCase))
        {
            return configuration["AI:GitHubEmbeddingModel"] ?? "openai/text-embedding-3-small";
        }

        return configuration["AI:EmbeddingModel"] ?? configuration["AI:Model"] ?? provider.Model;
    }

    private TimeSpan ResolveProviderTimeout()
    {
        var timeoutSeconds = configuration.GetValue("AI:ProviderTimeoutSeconds", 180);
        return TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 30, 300));
    }

    private static string TrimProviderError(string body, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "empty provider error response";
        }

        return TrimTo(RedactSecret(body, apiKey).Replace(Environment.NewLine, " ", StringComparison.Ordinal), 600);
    }

    private static string TrimTo(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength].Trim();
    }

    private static string RedactSecret(string value, string secret)
    {
        return string.IsNullOrWhiteSpace(secret)
            ? value
            : value.Replace(secret, "[redacted]", StringComparison.Ordinal);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record ContextSnippet(string Source, string Text);

    private sealed class AiContextEmbeddingException(string message, Exception? innerException = null)
        : Exception(message, innerException);
}
