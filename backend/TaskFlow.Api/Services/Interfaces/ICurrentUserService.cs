using TaskFlow.Api.Models;

namespace TaskFlow.Api.Services.Interfaces;

public interface ICurrentUserService
{
    Guid GetUserId();
    string? GetUserEmail();
    GlobalRole GetGlobalRole();
    bool IsAuthenticated();
}
