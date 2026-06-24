using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.DTOs.Messages;
using TaskFlow.Api.Helpers;
using TaskFlow.Api.Models;
using TaskFlow.Api.Services.Interfaces;

namespace TaskFlow.Api.Services;

public class MessageService(
    AppDbContext dbContext,
    ICurrentUserService currentUser,
    IPermissionService permissionService) : IMessageService
{
    public async Task<ApiResponse<MessageResponse>> CreateMessageAsync(Guid channelId, CreateMessageRequest request)
    {
        var userId = currentUser.GetUserId();
        if (!await permissionService.CanSendMessage(userId, channelId))
        {
            return ApiResponse.Fail<MessageResponse>("Channel not found or access denied.", StatusCodes.Status404NotFound);
        }

        var message = new Message
        {
            Id = Guid.NewGuid(),
            ChannelId = channelId,
            SenderId = userId,
            Content = request.Content.Trim()
        };

        dbContext.Messages.Add(message);
        await dbContext.SaveChangesAsync();

        var savedMessage = await dbContext.Messages
            .Include(item => item.Sender)
            .FirstAsync(item => item.Id == message.Id);

        return ApiResponse.Created(savedMessage.ToResponse(), "Message sent.");
    }

    public async Task<ApiResponse<MessageResponse>> UpdateMessageAsync(Guid messageId, UpdateMessageRequest request)
    {
        var message = await dbContext.Messages
            .Include(item => item.Sender)
            .Include(item => item.Channel)
            .FirstOrDefaultAsync(item => item.Id == messageId);

        if (message is null || message.IsDeleted)
        {
            return ApiResponse.Fail<MessageResponse>("Message not found.", StatusCodes.Status404NotFound);
        }

        var userId = currentUser.GetUserId();
        var canManageWorkspace = message.Channel is not null
            && await permissionService.CanManageWorkspace(userId, message.Channel.WorkspaceId);

        if (message.SenderId != userId && !canManageWorkspace)
        {
            return ApiResponse.Fail<MessageResponse>("You do not have permission to edit this message.", StatusCodes.Status403Forbidden);
        }

        message.Content = request.Content.Trim();
        message.EditedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();

        return ApiResponse.Ok(message.ToResponse(), "Message updated.");
    }

    public async Task<ApiResponse<bool>> DeleteMessageAsync(Guid messageId)
    {
        var message = await dbContext.Messages
            .Include(item => item.Channel)
            .FirstOrDefaultAsync(item => item.Id == messageId);

        if (message is null || message.IsDeleted)
        {
            return ApiResponse.Fail<bool>("Message not found.", StatusCodes.Status404NotFound);
        }

        var userId = currentUser.GetUserId();
        var canManageWorkspace = message.Channel is not null
            && await permissionService.CanManageWorkspace(userId, message.Channel.WorkspaceId);

        if (message.SenderId != userId && !canManageWorkspace)
        {
            return ApiResponse.Fail<bool>("You do not have permission to delete this message.", StatusCodes.Status403Forbidden);
        }

        message.IsDeleted = true;
        message.Content = "[deleted]";
        message.EditedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();

        return ApiResponse.NoData("Message deleted.");
    }
}
