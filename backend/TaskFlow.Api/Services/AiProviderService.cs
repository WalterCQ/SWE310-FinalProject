using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.DTOs.Agent;
using TaskFlow.Api.Helpers;
using TaskFlow.Api.Models;
using TaskFlow.Api.Services.Interfaces;

namespace TaskFlow.Api.Services;

public class AiProviderService(
    AppDbContext dbContext,
    ICurrentUserService currentUser,
    IPermissionService permissionService,
    IDataProtectionProvider dataProtectionProvider,
    IConfiguration configuration,
    IHostEnvironment environment) : IAiProviderService
{
    private const string ProtectorPurpose = "TaskFlow.AiProviderCredential.v1";
    private const string WorkspaceProtectorPurpose = "TaskFlow.WorkspaceAiProviderCredential.v1";
    private static readonly string[] DefaultAllowedBaseUrls =
    [
        "https://api.openai.com/v1",
        "https://api.siliconflow.cn/v1",
        "https://generativelanguage.googleapis.com/v1beta/openai",
        "https://models.github.ai/inference"
    ];

    public async Task<ApiResponse<AiProviderResponse>> SaveProviderAsync(SaveAiProviderRequest request, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated())
        {
            return ApiResponse.Fail<AiProviderResponse>("Authentication is required.", StatusCodes.Status401Unauthorized);
        }

        var userId = currentUser.GetUserId();
        if (userId == Guid.Empty)
        {
            return ApiResponse.Fail<AiProviderResponse>("Authenticated user id is missing.", StatusCodes.Status401Unauthorized);
        }

        if (request.IsDefault)
        {
            var existingDefaults = await dbContext.AiProviderCredentials
                .Where(credential => credential.UserId == userId && credential.IsDefault)
                .ToListAsync(cancellationToken);
            foreach (var existingDefault in existingDefaults)
            {
                existingDefault.IsDefault = false;
                existingDefault.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        var credential = new AiProviderCredential
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProviderName = request.ProviderName.Trim(),
            BaseUrl = string.IsNullOrWhiteSpace(request.BaseUrl) ? null : request.BaseUrl.Trim().TrimEnd('/'),
            Model = request.Model.Trim(),
            EncryptedApiKey = Protect(request.ApiKey.Trim()),
            SupportsToolCalls = ResolveToolCallSupport(request.ProviderName, request.BaseUrl, request.SupportsToolCalls),
            IsDefault = request.IsDefault
        };

        dbContext.AiProviderCredentials.Add(credential);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ApiResponse.Created(ToResponse(credential), "AI provider saved.");
    }

    public async Task<ApiResponse<IReadOnlyCollection<AiProviderResponse>>> GetProvidersAsync(CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated())
        {
            return ApiResponse.Fail<IReadOnlyCollection<AiProviderResponse>>("Authentication is required.", StatusCodes.Status401Unauthorized);
        }

        var userId = currentUser.GetUserId();
        var providers = await dbContext.AiProviderCredentials
            .AsNoTracking()
            .Where(credential => credential.UserId == userId)
            .OrderByDescending(credential => credential.IsDefault)
            .ThenByDescending(credential => credential.UpdatedAtUtc)
            .ToListAsync(cancellationToken);

        IReadOnlyCollection<AiProviderResponse> response = providers.Select(ToResponse).ToArray();
        return ApiResponse.Ok(response);
    }

    public async Task<ApiResponse<WorkspaceAiProviderResponse>> GetWorkspaceProviderAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var access = await EnsureCanManageWorkspace(workspaceId, cancellationToken);
        if (!access.Success)
        {
            return ApiResponse.Fail<WorkspaceAiProviderResponse>(access.Message, access.StatusCode, access.Errors);
        }

        var credential = await dbContext.WorkspaceAiProviderCredentials
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.WorkspaceId == workspaceId, cancellationToken);
        if (credential is null)
        {
            return ApiResponse.Fail<WorkspaceAiProviderResponse>("Workspace AI provider is not configured.", StatusCodes.Status404NotFound);
        }

        return ApiResponse.Ok(ToWorkspaceResponse(credential));
    }

    public async Task<ApiResponse<WorkspaceAiProviderResponse>> SaveWorkspaceProviderAsync(
        Guid workspaceId,
        SaveWorkspaceAiProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        var access = await EnsureCanManageWorkspace(workspaceId, cancellationToken);
        if (!access.Success)
        {
            return ApiResponse.Fail<WorkspaceAiProviderResponse>(access.Message, access.StatusCode, access.Errors);
        }

        var baseUrlResult = ResolveWorkspaceBaseUrl(request.ProviderName, request.BaseUrl);
        if (!baseUrlResult.Success || baseUrlResult.Data is null)
        {
            return ApiResponse.Fail<WorkspaceAiProviderResponse>(baseUrlResult.Message, baseUrlResult.StatusCode, baseUrlResult.Errors);
        }

        var userId = currentUser.GetUserId();
        var now = DateTime.UtcNow;
        var credential = await dbContext.WorkspaceAiProviderCredentials
            .FirstOrDefaultAsync(item => item.WorkspaceId == workspaceId, cancellationToken);
        var isCreate = credential is null;
        if (credential is null)
        {
            if (string.IsNullOrWhiteSpace(request.ApiKey))
            {
                return ApiResponse.Fail<WorkspaceAiProviderResponse>("API key is required when creating a workspace AI provider.");
            }

            credential = new WorkspaceAiProviderCredential
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspaceId,
                CreatedByUserId = userId,
                CreatedAtUtc = now
            };
            dbContext.WorkspaceAiProviderCredentials.Add(credential);
        }

        credential.ProviderName = request.ProviderName.Trim();
        credential.BaseUrl = baseUrlResult.Data;
        credential.Model = request.Model.Trim();
        credential.SupportsToolCalls = ResolveToolCallSupport(request.ProviderName, baseUrlResult.Data, request.SupportsToolCalls);
        credential.UpdatedByUserId = userId;
        credential.UpdatedAtUtc = now;

        if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            credential.EncryptedApiKey = ProtectWorkspace(request.ApiKey.Trim());
            credential.KeyLastUpdatedAtUtc = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return isCreate
            ? ApiResponse.Created(ToWorkspaceResponse(credential), "Workspace AI provider saved.")
            : ApiResponse.Ok(ToWorkspaceResponse(credential), "Workspace AI provider updated.");
    }

    public async Task<ApiResponse<bool>> DeleteWorkspaceProviderAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var access = await EnsureCanManageWorkspace(workspaceId, cancellationToken);
        if (!access.Success)
        {
            return ApiResponse.Fail<bool>(access.Message, access.StatusCode, access.Errors);
        }

        var credential = await dbContext.WorkspaceAiProviderCredentials
            .FirstOrDefaultAsync(item => item.WorkspaceId == workspaceId, cancellationToken);
        if (credential is null)
        {
            return ApiResponse.Fail<bool>("Workspace AI provider is not configured.", StatusCodes.Status404NotFound);
        }

        dbContext.WorkspaceAiProviderCredentials.Remove(credential);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ApiResponse.NoData("Workspace AI provider deleted.");
    }

    public async Task<ApiResponse<AiProviderRuntime>> ResolveWorkspaceProviderAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var credential = await dbContext.WorkspaceAiProviderCredentials
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.WorkspaceId == workspaceId, cancellationToken);
        if (credential is null)
        {
            return ApiResponse.Fail<AiProviderRuntime>("Workspace AI provider is not configured.", StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(credential.EncryptedApiKey))
        {
            return ApiResponse.Fail<AiProviderRuntime>("Workspace AI provider API key is not configured.", StatusCodes.Status400BadRequest);
        }

        try
        {
            var apiKey = UnprotectWorkspace(dataProtectionProvider, credential.EncryptedApiKey);
            return ApiResponse.Ok(new AiProviderRuntime
            {
                ProviderName = credential.ProviderName,
                BaseUrl = string.IsNullOrWhiteSpace(credential.BaseUrl) ? ResolveDefaultBaseUrl(credential.ProviderName) : credential.BaseUrl,
                Model = credential.Model,
                ApiKey = apiKey,
                SupportsToolCalls = credential.SupportsToolCalls
            });
        }
        catch
        {
            return ApiResponse.Fail<AiProviderRuntime>(
                "Workspace AI provider secret could not be decrypted. Re-save the workspace AI provider API key.",
                StatusCodes.Status500InternalServerError);
        }
    }

    private string Protect(string apiKey)
    {
        return dataProtectionProvider.CreateProtector(ProtectorPurpose).Protect(apiKey);
    }

    private string ProtectWorkspace(string apiKey)
    {
        return dataProtectionProvider.CreateProtector(WorkspaceProtectorPurpose).Protect(apiKey);
    }

    public static string Unprotect(IDataProtectionProvider dataProtectionProvider, string encryptedApiKey)
    {
        return dataProtectionProvider.CreateProtector(ProtectorPurpose).Unprotect(encryptedApiKey);
    }

    private static string UnprotectWorkspace(IDataProtectionProvider dataProtectionProvider, string encryptedApiKey)
    {
        return dataProtectionProvider.CreateProtector(WorkspaceProtectorPurpose).Unprotect(encryptedApiKey);
    }

    private async Task<ApiResponse<bool>> EnsureCanManageWorkspace(Guid workspaceId, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated())
        {
            return ApiResponse.Fail<bool>("Authentication is required.", StatusCodes.Status401Unauthorized);
        }

        var userId = currentUser.GetUserId();
        if (userId == Guid.Empty)
        {
            return ApiResponse.Fail<bool>("Authenticated user id is missing.", StatusCodes.Status401Unauthorized);
        }

        var workspaceExists = await dbContext.Workspaces
            .AsNoTracking()
            .AnyAsync(workspace => workspace.Id == workspaceId, cancellationToken);
        if (!workspaceExists)
        {
            return ApiResponse.Fail<bool>("Workspace not found.", StatusCodes.Status404NotFound);
        }

        if (!await permissionService.CanManageWorkspace(userId, workspaceId))
        {
            return ApiResponse.Fail<bool>("Only workspace administrators, workspace managers, or global administrators can manage the workspace AI provider.", StatusCodes.Status403Forbidden);
        }

        return ApiResponse.Ok(true);
    }

    private ApiResponse<string> ResolveWorkspaceBaseUrl(string providerName, string? requestedBaseUrl)
    {
        var baseUrl = string.IsNullOrWhiteSpace(requestedBaseUrl)
            ? ResolveDefaultBaseUrl(providerName)
            : requestedBaseUrl.Trim();
        baseUrl = baseUrl.TrimEnd('/');

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            return ApiResponse.Fail<string>("AI provider BaseUrl must be an absolute URL.");
        }

        var isDevelopmentLoopback = environment.IsDevelopment()
            && (uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase));
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) && !isDevelopmentLoopback)
        {
            return ApiResponse.Fail<string>("AI provider BaseUrl must use HTTPS outside Development.");
        }

        if (!isDevelopmentLoopback && !AllowedBaseUrls().Contains(baseUrl, StringComparer.OrdinalIgnoreCase))
        {
            return ApiResponse.Fail<string>("AI provider BaseUrl is not allowed for this backend.", StatusCodes.Status400BadRequest);
        }

        return ApiResponse.Ok(baseUrl);
    }

    private string ResolveDefaultBaseUrl(string providerName)
    {
        if (providerName.Contains("OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            return "https://api.openai.com/v1";
        }

        if (providerName.Contains("Silicon", StringComparison.OrdinalIgnoreCase))
        {
            return "https://api.siliconflow.cn/v1";
        }

        if (providerName.Contains("Google", StringComparison.OrdinalIgnoreCase)
            || providerName.Contains("Gemini", StringComparison.OrdinalIgnoreCase))
        {
            return "https://generativelanguage.googleapis.com/v1beta/openai";
        }

        if (providerName.Contains("GitHub", StringComparison.OrdinalIgnoreCase))
        {
            return "https://models.github.ai/inference";
        }

        var configuredUrl = configuration["AI:BaseUrl"];
        if (!string.IsNullOrWhiteSpace(configuredUrl))
        {
            return configuredUrl;
        }

        configuredUrl = configuration["AI:Endpoint"];
        if (!string.IsNullOrWhiteSpace(configuredUrl))
        {
            return configuredUrl;
        }

        return "https://api.openai.com/v1";
    }

    private IReadOnlyCollection<string> AllowedBaseUrls()
    {
        var configured = configuration.GetSection("AI:AllowedBaseUrls").Get<string[]>() ?? [];
        return configured
            .Concat(DefaultAllowedBaseUrls)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim().TrimEnd('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool ResolveToolCallSupport(string providerName, string? baseUrl, bool? supportsToolCalls)
    {
        if (supportsToolCalls.HasValue)
        {
            return supportsToolCalls.Value;
        }

        providerName = providerName.Trim();
        baseUrl = baseUrl?.Trim();
        return providerName.Equals("OpenAI", StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(baseUrl)
                || baseUrl.Contains("api.openai.com", StringComparison.OrdinalIgnoreCase));
    }

    private static AiProviderResponse ToResponse(AiProviderCredential credential)
    {
        return new AiProviderResponse
        {
            Id = credential.Id,
            ProviderName = credential.ProviderName,
            BaseUrl = credential.BaseUrl,
            Model = credential.Model,
            SupportsToolCalls = credential.SupportsToolCalls,
            IsDefault = credential.IsDefault,
            HasApiKey = !string.IsNullOrWhiteSpace(credential.EncryptedApiKey),
            CreatedAtUtc = credential.CreatedAtUtc,
            UpdatedAtUtc = credential.UpdatedAtUtc
        };
    }

    private static WorkspaceAiProviderResponse ToWorkspaceResponse(WorkspaceAiProviderCredential credential)
    {
        return new WorkspaceAiProviderResponse
        {
            WorkspaceId = credential.WorkspaceId,
            ProviderName = credential.ProviderName,
            BaseUrl = credential.BaseUrl,
            Model = credential.Model,
            SupportsToolCalls = credential.SupportsToolCalls,
            HasApiKey = !string.IsNullOrWhiteSpace(credential.EncryptedApiKey),
            CreatedAtUtc = credential.CreatedAtUtc,
            UpdatedAtUtc = credential.UpdatedAtUtc,
            KeyLastUpdatedAtUtc = credential.KeyLastUpdatedAtUtc
        };
    }
}
