using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.DTOs.GitHub;
using TaskFlow.Api.Helpers;
using TaskFlow.Api.Models;
using TaskFlow.Api.Services.Interfaces;

namespace TaskFlow.Api.Services;

public class GitHubRepositoryService(
    AppDbContext dbContext,
    ICurrentUserService currentUser,
    IPermissionService permissionService,
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory) : IGitHubRepositoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ApiResponse<GitHubSetupResponse>> GetSetupAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.GetUserId();
        if (!currentUser.IsAuthenticated() || userId == Guid.Empty)
        {
            return ApiResponse.Fail<GitHubSetupResponse>("Authentication is required.", StatusCodes.Status401Unauthorized);
        }

        if (!await permissionService.CanManageWorkspace(userId, workspaceId))
        {
            return ApiResponse.Fail<GitHubSetupResponse>("Only workspace administrators or managers can connect GitHub repositories.", StatusCodes.Status403Forbidden);
        }

        var appSlug = configuration["GitHub:AppSlug"]?.Trim();
        if (string.IsNullOrWhiteSpace(appSlug))
        {
            return ApiResponse.Fail<GitHubSetupResponse>("GitHub:AppSlug is not configured.", StatusCodes.Status503ServiceUnavailable);
        }

        return ApiResponse.Ok(new GitHubSetupResponse
        {
            AppSlug = appSlug,
            InstallUrl = $"https://github.com/apps/{Uri.EscapeDataString(appSlug)}/installations/new?state={workspaceId}"
        });
    }

    public async Task<ApiResponse<IReadOnlyCollection<GitHubRepositoryResponse>>> ListRepositoriesAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.GetUserId();
        if (!currentUser.IsAuthenticated() || userId == Guid.Empty)
        {
            return ApiResponse.Fail<IReadOnlyCollection<GitHubRepositoryResponse>>("Authentication is required.", StatusCodes.Status401Unauthorized);
        }

        if (!await permissionService.CanAccessWorkspace(userId, workspaceId))
        {
            return ApiResponse.Fail<IReadOnlyCollection<GitHubRepositoryResponse>>("Workspace not found or access denied.", StatusCodes.Status404NotFound);
        }

        var repositories = await dbContext.GitHubRepositoryConnections
            .AsNoTracking()
            .Where(repository => repository.WorkspaceId == workspaceId)
            .OrderBy(repository => repository.FullName)
            .Select(repository => ToResponse(repository))
            .ToListAsync(cancellationToken);

        return ApiResponse.Ok<IReadOnlyCollection<GitHubRepositoryResponse>>(repositories);
    }

    public async Task<ApiResponse<IReadOnlyCollection<GitHubRepositoryResponse>>> CompleteInstallationAsync(
        Guid workspaceId,
        CompleteGitHubInstallationRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.GetUserId();
        if (!currentUser.IsAuthenticated() || userId == Guid.Empty)
        {
            return ApiResponse.Fail<IReadOnlyCollection<GitHubRepositoryResponse>>("Authentication is required.", StatusCodes.Status401Unauthorized);
        }

        if (!await permissionService.CanManageWorkspace(userId, workspaceId))
        {
            return ApiResponse.Fail<IReadOnlyCollection<GitHubRepositoryResponse>>("Only workspace administrators or managers can connect GitHub repositories.", StatusCodes.Status403Forbidden);
        }

        IReadOnlyCollection<GitHubRepositoryMetadata> repositories;
        try
        {
            var accessToken = await CreateInstallationAccessTokenAsync(request.InstallationId, cancellationToken);
            repositories = await FetchInstallationRepositoriesAsync(request.InstallationId, accessToken.Token, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return ApiResponse.Fail<IReadOnlyCollection<GitHubRepositoryResponse>>(ex.Message, GetGitHubSetupStatusCode(ex.Message));
        }

        if (repositories.Count == 0)
        {
            return ApiResponse.Fail<IReadOnlyCollection<GitHubRepositoryResponse>>("GitHub installation has no accessible repositories.", StatusCodes.Status400BadRequest);
        }

        var fullNames = repositories.Select(repository => repository.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingConnections = await dbContext.GitHubRepositoryConnections
            .Where(connection =>
                connection.WorkspaceId == workspaceId
                && (connection.InstallationId == request.InstallationId || fullNames.Contains(connection.FullName)))
            .ToListAsync(cancellationToken);
        var existingByFullName = existingConnections.ToDictionary(connection => connection.FullName, StringComparer.OrdinalIgnoreCase);

        foreach (var repository in repositories)
        {
            if (!existingByFullName.TryGetValue(repository.FullName, out var connection))
            {
                connection = new GitHubRepositoryConnection
                {
                    Id = Guid.NewGuid(),
                    WorkspaceId = workspaceId,
                    CreatedByUserId = userId,
                    FullName = repository.FullName,
                    CreatedAtUtc = DateTime.UtcNow
                };
                dbContext.GitHubRepositoryConnections.Add(connection);
            }

            connection.InstallationId = request.InstallationId;
            connection.RepositoryId = repository.RepositoryId;
            connection.Owner = repository.Owner;
            connection.Name = repository.Name;
            connection.FullName = repository.FullName;
            connection.DefaultBranch = repository.DefaultBranch;
            connection.IsEnabled = true;
            connection.PermissionStatus = "connected";
            connection.LastSyncedAtUtc = DateTime.UtcNow;
            connection.UpdatedAtUtc = DateTime.UtcNow;
        }

        var removedConnections = existingConnections
            .Where(connection => connection.InstallationId == request.InstallationId && !fullNames.Contains(connection.FullName))
            .ToArray();
        DisableConnections(removedConnections, "repository_removed");

        await dbContext.SaveChangesAsync(cancellationToken);

        var response = await dbContext.GitHubRepositoryConnections
            .AsNoTracking()
            .Where(connection => connection.WorkspaceId == workspaceId)
            .OrderBy(connection => connection.FullName)
            .Select(connection => ToResponse(connection))
            .ToListAsync(cancellationToken);

        return ApiResponse.Ok<IReadOnlyCollection<GitHubRepositoryResponse>>(response, "GitHub installation connected.");
    }

    public async Task<ApiResponse<GitHubRepositoryResponse>> ConnectRepositoryAsync(
        Guid workspaceId,
        ConnectGitHubRepositoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.GetUserId();
        if (!currentUser.IsAuthenticated() || userId == Guid.Empty)
        {
            return ApiResponse.Fail<GitHubRepositoryResponse>("Authentication is required.", StatusCodes.Status401Unauthorized);
        }

        if (!await permissionService.CanManageWorkspace(userId, workspaceId))
        {
            return ApiResponse.Fail<GitHubRepositoryResponse>("Only workspace administrators or managers can connect GitHub repositories.", StatusCodes.Status403Forbidden);
        }

        var owner = request.Owner.Trim();
        var name = request.Name.Trim();
        var fullName = $"{owner}/{name}";
        GitHubRepositoryMetadata metadata;
        try
        {
            var accessToken = await CreateInstallationAccessTokenAsync(request.InstallationId, cancellationToken);
            metadata = await FetchRepositoryMetadataAsync(fullName, accessToken.Token, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return ApiResponse.Fail<GitHubRepositoryResponse>(ex.Message, GetGitHubSetupStatusCode(ex.Message));
        }

        var repository = await dbContext.GitHubRepositoryConnections
            .FirstOrDefaultAsync(item => item.WorkspaceId == workspaceId && item.FullName == fullName, cancellationToken);

        if (repository is null)
        {
            repository = new GitHubRepositoryConnection
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspaceId,
                CreatedByUserId = userId,
                FullName = fullName,
                CreatedAtUtc = DateTime.UtcNow
            };
            dbContext.GitHubRepositoryConnections.Add(repository);
        }

        repository.InstallationId = request.InstallationId;
        repository.RepositoryId = request.RepositoryId ?? metadata.RepositoryId;
        repository.Owner = metadata.Owner;
        repository.Name = metadata.Name;
        repository.FullName = metadata.FullName;
        repository.DefaultBranch = string.IsNullOrWhiteSpace(request.DefaultBranch) ? metadata.DefaultBranch : request.DefaultBranch.Trim();
        repository.ValidationCommand = string.IsNullOrWhiteSpace(request.ValidationCommand) ? null : request.ValidationCommand.Trim();
        repository.IsEnabled = true;
        repository.PermissionStatus = "connected";
        repository.LastSyncedAtUtc = DateTime.UtcNow;
        repository.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return ApiResponse.Ok(ToResponse(repository), "GitHub repository connected.");
    }

    public async Task<ApiResponse<bool>> DeleteRepositoryAsync(
        Guid workspaceId,
        Guid repositoryId,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.GetUserId();
        if (!currentUser.IsAuthenticated() || userId == Guid.Empty)
        {
            return ApiResponse.Fail<bool>("Authentication is required.", StatusCodes.Status401Unauthorized);
        }

        if (!await permissionService.CanManageWorkspace(userId, workspaceId))
        {
            return ApiResponse.Fail<bool>("Only workspace administrators or managers can remove GitHub repositories.", StatusCodes.Status403Forbidden);
        }

        var repository = await dbContext.GitHubRepositoryConnections
            .FirstOrDefaultAsync(item => item.Id == repositoryId && item.WorkspaceId == workspaceId, cancellationToken);
        if (repository is null)
        {
            return ApiResponse.Fail<bool>("GitHub repository connection not found.", StatusCodes.Status404NotFound);
        }

        dbContext.GitHubRepositoryConnections.Remove(repository);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ApiResponse.Ok(true, "GitHub repository disconnected.");
    }

    public async Task<ApiResponse<bool>> HandleWebhookAsync(IHeaderDictionary headers, string body, CancellationToken cancellationToken = default)
    {
        var secret = configuration["GitHub:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            return ApiResponse.Fail<bool>("GitHub webhook secret is not configured.", StatusCodes.Status503ServiceUnavailable);
        }

        if (!IsValidSignature(headers["X-Hub-Signature-256"].ToString(), body, secret))
        {
            return ApiResponse.Fail<bool>("Invalid GitHub webhook signature.", StatusCodes.Status401Unauthorized);
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var action = root.TryGetProperty("action", out var actionElement) ? actionElement.GetString() : null;
        var installationId = root.TryGetProperty("installation", out var installation)
            && installation.TryGetProperty("id", out var installationIdElement)
            && installationIdElement.TryGetInt64(out var parsedInstallationId)
                ? parsedInstallationId
                : 0;

        if (installationId <= 0)
        {
            return ApiResponse.Ok(true);
        }

        if (string.Equals(action, "deleted", StringComparison.OrdinalIgnoreCase))
        {
            var connections = await dbContext.GitHubRepositoryConnections
                .Where(connection => connection.InstallationId == installationId)
                .ToListAsync(cancellationToken);
            DisableConnections(connections, "installation_removed");
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        else if (string.Equals(action, "removed", StringComparison.OrdinalIgnoreCase)
            && root.TryGetProperty("repositories_removed", out var removedRepositories)
            && removedRepositories.ValueKind == JsonValueKind.Array)
        {
            var removedFullNames = removedRepositories
                .EnumerateArray()
                .Select(repository => repository.TryGetProperty("full_name", out var fullName) ? fullName.GetString() : null)
                .Where(fullName => !string.IsNullOrWhiteSpace(fullName))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (removedFullNames.Count > 0)
            {
                var connections = await dbContext.GitHubRepositoryConnections
                    .Where(connection =>
                        connection.InstallationId == installationId
                        && removedFullNames.Contains(connection.FullName))
                    .ToListAsync(cancellationToken);
                DisableConnections(connections, "repository_removed");
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        return ApiResponse.Ok(true);
    }

    public async Task<GitHubInstallationAccessToken> CreateInstallationAccessTokenAsync(long installationId, CancellationToken cancellationToken = default)
    {
        EnsureGitHubAppConfigured();

        var client = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.github.com/app/installations/{installationId}/access_tokens");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateGitHubAppJwt());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("TaskFlow-AI-Agent");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"GitHub installation token request failed with {(int)response.StatusCode}: {Shorten(body, 400)}");
        }

        using var document = JsonDocument.Parse(body);
        var token = document.RootElement.GetProperty("token").GetString();
        var expiresAt = document.RootElement.GetProperty("expires_at").GetDateTimeOffset();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("GitHub installation token response did not include a token.");
        }

        return new GitHubInstallationAccessToken(token, expiresAt);
    }

    public async Task<GitHubPullRequestResult> CreatePullRequestAsync(
        GitHubRepositoryConnection repository,
        string headBranch,
        string title,
        string body,
        CancellationToken cancellationToken = default)
    {
        var accessToken = await CreateInstallationAccessTokenAsync(repository.InstallationId, cancellationToken);
        var client = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.github.com/repos/{repository.FullName}/pulls");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("TaskFlow-AI-Agent");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            title,
            body,
            head = headBranch,
            @base = repository.DefaultBranch,
            maintainer_can_modify = true
        }, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"GitHub pull request creation failed with {(int)response.StatusCode}: {Shorten(responseBody, 400)}");
        }

        using var document = JsonDocument.Parse(responseBody);
        return new GitHubPullRequestResult(
            document.RootElement.GetProperty("number").GetInt32(),
            document.RootElement.GetProperty("html_url").GetString() ?? string.Empty,
            headBranch);
    }

    private async Task<GitHubRepositoryMetadata> FetchRepositoryMetadataAsync(
        string fullName,
        string installationToken,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{fullName}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", installationToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("TaskFlow-AI-Agent");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"GitHub repository validation failed with {(int)response.StatusCode}: {Shorten(body, 400)}");
        }

        using var document = JsonDocument.Parse(body);
        var repositoryId = document.RootElement.GetProperty("id").GetInt64();
        var defaultBranch = document.RootElement.GetProperty("default_branch").GetString();
        var name = document.RootElement.GetProperty("name").GetString();
        var owner = document.RootElement
            .GetProperty("owner")
            .GetProperty("login")
            .GetString();
        var resolvedFullName = document.RootElement.GetProperty("full_name").GetString();
        if (string.IsNullOrWhiteSpace(defaultBranch))
        {
            throw new InvalidOperationException("GitHub repository response did not include a default branch.");
        }
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(resolvedFullName))
        {
            throw new InvalidOperationException("GitHub repository response did not include owner, name, or full_name.");
        }

        return new GitHubRepositoryMetadata(repositoryId, owner, name, resolvedFullName, defaultBranch);
    }

    private async Task<IReadOnlyCollection<GitHubRepositoryMetadata>> FetchInstallationRepositoriesAsync(
        long installationId,
        string installationToken,
        CancellationToken cancellationToken)
    {
        var repositories = new List<GitHubRepositoryMetadata>();
        var client = httpClientFactory.CreateClient();

        for (var page = 1; page <= 10; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/installation/repositories?per_page=100&page={page}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", installationToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.ParseAdd("TaskFlow-AI-Agent");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"GitHub installation repository sync failed with {(int)response.StatusCode}: {Shorten(body, 400)}");
            }

            using var document = JsonDocument.Parse(body);
            var items = document.RootElement.GetProperty("repositories").EnumerateArray().ToArray();
            foreach (var repository in items)
            {
                var repositoryId = repository.GetProperty("id").GetInt64();
                var owner = repository.GetProperty("owner").GetProperty("login").GetString();
                var name = repository.GetProperty("name").GetString();
                var resolvedFullName = repository.GetProperty("full_name").GetString();
                var defaultBranch = repository.GetProperty("default_branch").GetString();
                if (string.IsNullOrWhiteSpace(owner)
                    || string.IsNullOrWhiteSpace(name)
                    || string.IsNullOrWhiteSpace(resolvedFullName)
                    || string.IsNullOrWhiteSpace(defaultBranch))
                {
                    throw new InvalidOperationException($"GitHub installation {installationId} returned an incomplete repository payload.");
                }

                repositories.Add(new GitHubRepositoryMetadata(repositoryId, owner, name, resolvedFullName, defaultBranch));
            }

            if (items.Length < 100)
            {
                break;
            }
        }

        return repositories;
    }

    private string CreateGitHubAppJwt()
    {
        var appId = configuration["GitHub:AppId"]?.Trim();
        if (string.IsNullOrWhiteSpace(appId))
        {
            throw new InvalidOperationException("GitHub:AppId is not configured.");
        }

        using var rsa = RSA.Create();
        rsa.ImportFromPem(GetPrivateKeyPem());

        var issuedAt = DateTimeOffset.UtcNow.AddSeconds(-60).ToUnixTimeSeconds();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(9).ToUnixTimeSeconds();
        var header = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
        var payload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new { iat = issuedAt, exp = expiresAt, iss = appId }));
        var signingInput = $"{header}.{payload}";
        var signature = rsa.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{signingInput}.{Base64UrlEncode(signature)}";
    }

    private void EnsureGitHubAppConfigured()
    {
        if (string.IsNullOrWhiteSpace(configuration["GitHub:AppId"]))
        {
            throw new InvalidOperationException("GitHub:AppId is not configured.");
        }

        _ = GetPrivateKeyPem();
    }

    private string GetPrivateKeyPem()
    {
        var privateKeyPem = configuration["GitHub:PrivateKeyPem"];
        if (!string.IsNullOrWhiteSpace(privateKeyPem))
        {
            return privateKeyPem.Replace("\\n", "\n", StringComparison.Ordinal);
        }

        var privateKeyPath = configuration["GitHub:PrivateKeyPath"];
        if (!string.IsNullOrWhiteSpace(privateKeyPath) && File.Exists(privateKeyPath))
        {
            return File.ReadAllText(privateKeyPath);
        }

        throw new InvalidOperationException("GitHub private key is not configured. Set GitHub:PrivateKeyPem or GitHub:PrivateKeyPath.");
    }

    private static bool IsValidSignature(string signatureHeader, string body, string secret)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader) || !signatureHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var expected = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(signatureHeader.ToLowerInvariant()));
    }

    private static string Base64UrlEncode(byte[] value)
    {
        return Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static GitHubRepositoryResponse ToResponse(GitHubRepositoryConnection repository)
    {
        return new GitHubRepositoryResponse
        {
            Id = repository.Id,
            WorkspaceId = repository.WorkspaceId,
            InstallationId = repository.InstallationId,
            RepositoryId = repository.RepositoryId,
            Owner = repository.Owner,
            Name = repository.Name,
            FullName = repository.FullName,
            DefaultBranch = repository.DefaultBranch,
            ValidationCommand = repository.ValidationCommand,
            IsEnabled = repository.IsEnabled,
            PermissionStatus = repository.PermissionStatus,
            CreatedAtUtc = repository.CreatedAtUtc,
            UpdatedAtUtc = repository.UpdatedAtUtc,
            LastSyncedAtUtc = repository.LastSyncedAtUtc
        };
    }

    private static void DisableConnections(IReadOnlyCollection<GitHubRepositoryConnection> connections, string permissionStatus)
    {
        foreach (var connection in connections)
        {
            connection.IsEnabled = false;
            connection.PermissionStatus = permissionStatus;
            connection.LastSyncedAtUtc = DateTime.UtcNow;
            connection.UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    private static string Shorten(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static int GetGitHubSetupStatusCode(string message)
    {
        return message.Contains("not configured", StringComparison.OrdinalIgnoreCase)
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status400BadRequest;
    }

    private sealed record GitHubRepositoryMetadata(
        long RepositoryId,
        string Owner,
        string Name,
        string FullName,
        string DefaultBranch);
}
