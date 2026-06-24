using Microsoft.AspNetCore.SignalR;
using TaskFlow.Api.Services.Interfaces;

namespace TaskFlow.Api.Hubs;

public class NotificationHub(ICurrentUserService currentUser) : Hub
{
    public async Task JoinUserNotificationGroup()
    {
        if (!currentUser.IsAuthenticated())
        {
            throw new HubException("Authentication is required.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{currentUser.GetUserId()}");
    }
}
