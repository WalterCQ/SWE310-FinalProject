using TaskFlow.Api.Models;
using TaskFlow.Api.Services.Interfaces;

namespace TaskFlow.AgentWorker;

public class WorkerUserContext : ICurrentUserService
{
    public Guid UserId { get; set; }

    public Guid GetUserId()
    {
        return UserId;
    }

    public string? GetUserEmail()
    {
        return null;
    }

    public GlobalRole GetGlobalRole()
    {
        return GlobalRole.User;
    }

    public bool IsAuthenticated()
    {
        return UserId != Guid.Empty;
    }
}
