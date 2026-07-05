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
    private const int MaxUploadBytes = 20_000_000;

    public async Task<ApiResponse<MessageResponse>> CreateMessageAsync(Guid channelId, CreateMessageRequest request)
    {
        var userId = currentUser.GetUserId();
        if (!await permissionService.CanSendMessage(userId, channelId))
        {
            return ApiResponse.Fail<MessageResponse>("Channel not found or access denied.", StatusCodes.Status404NotFound);
        }

        var content = request.Content.Trim();
        if (string.IsNullOrWhiteSpace(content))
        {
            return ApiResponse.Fail<MessageResponse>("Message content is required.");
        }

        var message = new Message
        {
            Id = Guid.NewGuid(),
            ChannelId = channelId,
            SenderId = userId,
            Content = content
        };

        dbContext.Messages.Add(message);
        await dbContext.SaveChangesAsync();

        var savedMessage = await dbContext.Messages
            .Include(item => item.Sender)
            .Include(item => item.Attachments)
            .FirstAsync(item => item.Id == message.Id);

        return ApiResponse.Created(savedMessage.ToResponse(), "Message sent.");
    }

    public async Task<ApiResponse<MessageResponse>> CreateAttachmentMessageAsync(Guid channelId, CreateMessageAttachmentRequest request)
    {
        var userId = currentUser.GetUserId();
        var channel = await dbContext.Channels
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == channelId);
        if (channel is null || !await permissionService.CanSendMessage(userId, channelId))
        {
            return ApiResponse.Fail<MessageResponse>("Channel not found or access denied.", StatusCodes.Status404NotFound);
        }

        var file = request.File;
        if (file is null || file.Length == 0)
        {
            return ApiResponse.Fail<MessageResponse>("Attachment file is required.");
        }

        if (file.Length > MaxUploadBytes)
        {
            return ApiResponse.Fail<MessageResponse>("Attachment exceeds the 20 MB upload limit.", StatusCodes.Status413PayloadTooLarge);
        }

        var content = request.Content?.Trim() ?? string.Empty;
        if (content.Length > 4000)
        {
            return ApiResponse.Fail<MessageResponse>("Message content cannot exceed 4000 characters.");
        }

        await using var stream = file.OpenReadStream();
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream);

        var message = new Message
        {
            Id = Guid.NewGuid(),
            ChannelId = channelId,
            SenderId = userId,
            Content = content
        };
        var attachment = new ChannelAttachment
        {
            Id = Guid.NewGuid(),
            WorkspaceId = channel.WorkspaceId,
            ChannelId = channelId,
            MessageId = message.Id,
            UploadedByUserId = userId,
            FileName = ResolveFileName(file.FileName),
            ContentType = ResolveContentType(file),
            SizeBytes = file.Length,
            Summary = string.Empty,
            ExtractedText = string.Empty,
            IsAiIndexed = false
        };
        var blob = new ChannelAttachmentBlob
        {
            AttachmentId = attachment.Id,
            Content = memoryStream.ToArray()
        };

        dbContext.Messages.Add(message);
        dbContext.ChannelAttachments.Add(attachment);
        dbContext.ChannelAttachmentBlobs.Add(blob);
        await dbContext.SaveChangesAsync();

        var savedMessage = await dbContext.Messages
            .Include(item => item.Sender)
            .Include(item => item.Attachments)
            .FirstAsync(item => item.Id == message.Id);

        return ApiResponse.Created(savedMessage.ToResponse(), "Attachment sent.");
    }

    public async Task<ApiResponse<MessageResponse>> UpdateMessageAsync(Guid messageId, UpdateMessageRequest request)
    {
        var message = await dbContext.Messages
            .Include(item => item.Sender)
            .Include(item => item.Channel)
            .Include(item => item.Attachments)
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

    private static string ResolveContentType(IFormFile file)
    {
        return string.IsNullOrWhiteSpace(file.ContentType)
            ? "application/octet-stream"
            : file.ContentType;
    }

    private static string ResolveFileName(string fileName)
    {
        var safeName = Path.GetFileName(fileName);
        return string.IsNullOrWhiteSpace(safeName) ? "attachment" : safeName;
    }
}
