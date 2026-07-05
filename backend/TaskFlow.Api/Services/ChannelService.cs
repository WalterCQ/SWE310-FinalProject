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

    public async Task<ApiResponse<IEnumerable<ChannelMemberResponse>>> GetChannelMembersAsync(Guid channelId)
    {
        var userId = currentUser.GetUserId();
        if (!await permissionService.CanAccessChannel(userId, channelId))
        {
            return ApiResponse.Fail<IEnumerable<ChannelMemberResponse>>("Channel not found or access denied.", StatusCodes.Status404NotFound);
        }

        var members = await dbContext.ChannelMembers
            .Include(member => member.User)
            .Where(member => member.ChannelId == channelId)
            .OrderBy(member => member.User!.Name)
            .ToListAsync();

        return ApiResponse.Ok(members.Select(ToMemberResponse));
    }

    public async Task<ApiResponse<ChannelMemberResponse>> AddChannelMemberAsync(Guid channelId, AddChannelMemberRequest request)
    {
        var currentUserId = currentUser.GetUserId();
        var channel = await dbContext.Channels.AsNoTracking().FirstOrDefaultAsync(item => item.Id == channelId);
        if (channel is null)
        {
            return ApiResponse.Fail<ChannelMemberResponse>("Channel not found.", StatusCodes.Status404NotFound);
        }

        if (!await permissionService.CanManageWorkspace(currentUserId, channel.WorkspaceId))
        {
            return ApiResponse.Fail<ChannelMemberResponse>("You do not have permission to add channel members.", StatusCodes.Status403Forbidden);
        }

        var user = await FindUserByEmail(request.Email);
        if (user is null)
        {
            return ApiResponse.Fail<ChannelMemberResponse>("Registered user not found.", StatusCodes.Status404NotFound);
        }

        var isWorkspaceMember = await dbContext.WorkspaceMembers.AnyAsync(member =>
            member.WorkspaceId == channel.WorkspaceId && member.UserId == user.Id);
        if (!isWorkspaceMember)
        {
            return ApiResponse.Fail<ChannelMemberResponse>("User must be a workspace member before joining this channel.");
        }

        var existingMember = await dbContext.ChannelMembers
            .Include(member => member.User)
            .FirstOrDefaultAsync(member => member.ChannelId == channelId && member.UserId == user.Id);
        if (existingMember is not null)
        {
            return ApiResponse.Fail<ChannelMemberResponse>("User is already a channel member.");
        }

        var member = new ChannelMember
        {
            Id = Guid.NewGuid(),
            ChannelId = channelId,
            UserId = user.Id,
            User = user
        };

        dbContext.ChannelMembers.Add(member);
        await dbContext.SaveChangesAsync();

        return ApiResponse.Created(ToMemberResponse(member), "Channel member added.");
    }

    public async Task<ApiResponse<bool>> RemoveChannelMemberAsync(Guid channelId, Guid userId)
    {
        var channel = await dbContext.Channels.AsNoTracking().FirstOrDefaultAsync(item => item.Id == channelId);
        if (channel is null)
        {
            return ApiResponse.Fail<bool>("Channel not found.", StatusCodes.Status404NotFound);
        }

        var currentUserId = currentUser.GetUserId();
        if (!await permissionService.CanManageWorkspace(currentUserId, channel.WorkspaceId))
        {
            return ApiResponse.Fail<bool>("You do not have permission to remove channel members.", StatusCodes.Status403Forbidden);
        }

        var member = await dbContext.ChannelMembers
            .FirstOrDefaultAsync(item => item.ChannelId == channelId && item.UserId == userId);
        if (member is null)
        {
            return ApiResponse.Fail<bool>("Channel member not found.", StatusCodes.Status404NotFound);
        }

        dbContext.ChannelMembers.Remove(member);
        await dbContext.SaveChangesAsync();

        return ApiResponse.NoData("Channel member removed.");
    }

    private async Task<User?> FindUserByEmail(string email)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        return await dbContext.Users.FirstOrDefaultAsync(user => user.Email == normalizedEmail);
    }

    private static ChannelMemberResponse ToMemberResponse(ChannelMember member)
    {
        return new ChannelMemberResponse
        {
            UserId = member.UserId,
            Name = member.User?.Name ?? string.Empty,
            Email = member.User?.Email ?? string.Empty,
            JoinedAtUtc = member.JoinedAtUtc
        };
    }
}
