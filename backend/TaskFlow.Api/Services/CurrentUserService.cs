using System.Security.Claims;
using TaskFlow.Api.Models;
using TaskFlow.Api.Services.Interfaces;

namespace TaskFlow.Api.Services;

public class CurrentUserService(IHttpContextAccessor httpContextAccessor, IConfiguration configuration) : ICurrentUserService
{
    public Guid GetUserId()
    {
        var principal = httpContextAccessor.HttpContext?.User;
        var claimValue = principal?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal?.FindFirstValue("sub")
            ?? principal?.FindFirstValue("userId");

        if (Guid.TryParse(claimValue, out var userId))
        {
            return userId;
        }

        if (UseDevelopmentFallback()
            && Guid.TryParse(configuration["Auth:DevelopmentUserId"], out var developmentUserId))
        {
            return developmentUserId;
        }

        return Guid.Empty;
    }

    public string? GetUserEmail()
    {
        var principal = httpContextAccessor.HttpContext?.User;
        return principal?.FindFirstValue(ClaimTypes.Email)
            ?? principal?.FindFirstValue("email")
            ?? (UseDevelopmentFallback() ? configuration["Auth:DevelopmentUserEmail"] : null);
    }

    public GlobalRole GetGlobalRole()
    {
        var principal = httpContextAccessor.HttpContext?.User;
        var roleValue = principal?.FindFirstValue(ClaimTypes.Role) ?? principal?.FindFirstValue("role");
        return Enum.TryParse<GlobalRole>(roleValue, ignoreCase: true, out var role)
            ? role
            : GlobalRole.User;
    }

    public bool IsAuthenticated()
    {
        return httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true || UseDevelopmentFallback();
    }

    private bool UseDevelopmentFallback()
    {
        // Development-only fallback; disabled by default. JWT login flow is connected.
        return configuration.GetValue("Auth:AllowDevelopmentUserFallback", false);
    }
}
