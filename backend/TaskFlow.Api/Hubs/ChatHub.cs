using Microsoft.AspNetCore.SignalR;
using TaskFlow.Api.DTOs.Messages;
using TaskFlow.Api.Services.Interfaces;

namespace TaskFlow.Api.Hubs;

public class ChatHub(
    ICurrentUserService currentUser,
    IPermissionService permissionService,
    IMessageService messageService) : Hub
{
    public async Task JoinWorkspace(Guid workspaceId)
    {
        var userId = currentUser.GetUserId();
        if (!await permissionService.CanAccessWorkspace(userId, workspaceId))
        {
            throw new HubException("Workspace access denied.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"workspace:{workspaceId}");
    }

    public async Task JoinChannel(Guid channelId)
    {
        var userId = currentUser.GetUserId();
        if (!await permissionService.CanAccessChannel(userId, channelId))
        {
            throw new HubException("Channel access denied.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"channel:{channelId}");
    }

    public Task LeaveChannel(Guid channelId)
    {
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, $"channel:{channelId}");
    }

    public async Task SendMessage(Guid channelId, string content)
    {
        var result = await messageService.CreateMessageAsync(channelId, new CreateMessageRequest { Content = content });
        if (!result.Success || result.Data is null)
        {
            throw new HubException(result.Message);
        }

        await Clients.Group($"channel:{channelId}").SendAsync("MessageReceived", result.Data);
    }

    public async Task Typing(Guid channelId)
    {
        var userId = currentUser.GetUserId();
        if (!await permissionService.CanAccessChannel(userId, channelId))
        {
            throw new HubException("Channel access denied.");
        }

        await Clients.OthersInGroup($"channel:{channelId}").SendAsync("UserTyping", new { ChannelId = channelId, UserId = userId });
    }

    public async Task StopTyping(Guid channelId)
    {
        var userId = currentUser.GetUserId();
        if (!await permissionService.CanAccessChannel(userId, channelId))
        {
            throw new HubException("Channel access denied.");
        }

        await Clients.OthersInGroup($"channel:{channelId}").SendAsync("UserStoppedTyping", new { ChannelId = channelId, UserId = userId });
    }
}
