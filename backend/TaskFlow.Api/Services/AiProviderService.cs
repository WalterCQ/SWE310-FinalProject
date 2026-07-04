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
    IDataProtectionProvider dataProtectionProvider) : IAiProviderService
{
    private const string ProtectorPurpose = "TaskFlow.AiProviderCredential.v1";

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
            SupportsToolCalls = ResolveToolCallSupport(request),
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

    private string Protect(string apiKey)
    {
        return dataProtectionProvider.CreateProtector(ProtectorPurpose).Protect(apiKey);
    }

    public static string Unprotect(IDataProtectionProvider dataProtectionProvider, string encryptedApiKey)
    {
        return dataProtectionProvider.CreateProtector(ProtectorPurpose).Unprotect(encryptedApiKey);
    }

    private static bool ResolveToolCallSupport(SaveAiProviderRequest request)
    {
        if (request.SupportsToolCalls.HasValue)
        {
            return request.SupportsToolCalls.Value;
        }

        var providerName = request.ProviderName.Trim();
        var baseUrl = request.BaseUrl?.Trim();
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
}
