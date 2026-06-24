using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.DTOs.Channels;
using TaskFlow.Api.DTOs.Messages;
using TaskFlow.Api.Helpers;
using TaskFlow.Api.Models;
using TaskFlow.Api.Services.Interfaces;

namespace TaskFlow.Api.Services;

public class ChannelService(
    AppDbContext dbContext,
    ICurrentUserService currentUser,
    IPermissionService permissionService) : IChannelService
{
    public async Task<ApiResponse<IEnumerable<ChannelResponse>>> GetWorkspaceChannelsAsync(Guid workspaceId)
    {
        var userId = currentUser.GetUserId();
        if (!await permissionService.CanAccessWorkspace(userId, workspaceId))
        {
            return ApiResponse.Fail<IEnumerable<ChannelResponse>>("Workspace not found or access denied.", StatusCodes.Status404NotFound);
        }

        var channels = await dbContext.Channels
            .Include(channel => channel.Members)
            .Where(channel => channel.WorkspaceId == workspaceId
                && (!channel.IsPrivate || channel.Members.Any(member => member.UserId == userId)))
            .OrderBy(channel => channel.Name)
            .ToListAsync();

        return ApiResponse.Ok(channels.Select(channel => channel.ToResponse()));
    }

    public async Task<ApiResponse<ChannelResponse>> CreateChannelAsync(Guid workspaceId, CreateChannelRequest request)
    {
        var userId = currentUser.GetUserId();
        if (!await permissionService.CanManageWorkspace(userId, workspaceId))
        {
            return ApiResponse.Fail<ChannelResponse>("You do not have permission to create channels in this workspace.", StatusCodes.Status403Forbidden);
        }

        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            IsPrivate = request.IsPrivate,
            CreatedByUserId = userId
        };

        channel.Members.Add(new ChannelMember
        {
            Id = Guid.NewGuid(),
            ChannelId = channel.Id,
            UserId = userId
        });

        dbContext.Channels.Add(channel);
        await dbContext.SaveChangesAsync();

        return ApiResponse.Created(channel.ToResponse(), "Channel created.");
    }

    public async Task<ApiResponse<IEnumerable<MessageResponse>>> GetChannelMessagesAsync(Guid channelId)
    {
        var userId = currentUser.GetUserId();
        if (!await permissionService.CanAccessChannel(userId, channelId))
        {
            return ApiResponse.Fail<IEnumerable<MessageResponse>>("Channel not found or access denied.", StatusCodes.Status404NotFound);
        }

        var messages = await dbContext.Messages
            .Include(message => message.Sender)
            .Where(message => message.ChannelId == channelId)
            .OrderBy(message => message.CreatedAtUtc)
            .Take(200)
            .ToListAsync();

        return ApiResponse.Ok(messages.Select(message => message.ToResponse()));
    }
}
