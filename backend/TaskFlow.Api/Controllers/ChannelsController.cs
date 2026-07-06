using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using TaskFlow.Api.Data;
using TaskFlow.Api.DTOs.AI;
using TaskFlow.Api.DTOs.Channels;
using TaskFlow.Api.Helpers;
using TaskFlow.Api.Hubs;
using TaskFlow.Api.Services;
using TaskFlow.Api.Services.Interfaces;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class ChannelsController(
    IChannelService channelService,
    IAiCommandService aiCommandService,
    AppDbContext dbContext,
    IPineconeVectorStore pineconeVectorStore,
    IPermissionService permissionService,
    ICurrentUserService currentUser,
    IHubContext<ChatHub> chatHubContext) : ControllerBase
{
    [HttpGet("workspaces/{workspaceId:guid}/channels")]
    public async Task<ActionResult> GetWorkspaceChannels(Guid workspaceId)
    {
        return this.ToActionResult(await channelService.GetWorkspaceChannelsAsync(workspaceId));
    }

    [HttpPost("workspaces/{workspaceId:guid}/channels")]
    public async Task<ActionResult> CreateChannel(Guid workspaceId, CreateChannelRequest request)
    {
        return this.ToActionResult(await channelService.CreateChannelAsync(workspaceId, request));
    }

    [HttpGet("channels/{channelId:guid}/messages")]
    public async Task<ActionResult> GetChannelMessages(Guid channelId)
    {
        return this.ToActionResult(await channelService.GetChannelMessagesAsync(channelId));
    }

    [HttpGet("channels/{channelId:guid}/attachments")]
    public async Task<ActionResult> GetChannelAttachments(Guid channelId, CancellationToken cancellationToken)
    {
        return this.ToActionResult(await aiCommandService.ListChannelAttachmentsAsync(channelId, cancellationToken));
    }

    [HttpPost("channels/{channelId:guid}/attachments")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult> UploadChannelAttachment(Guid channelId, IFormFile file, CancellationToken cancellationToken)
    {
        return this.ToActionResult(await aiCommandService.IndexChannelAttachmentAsync(channelId, file, cancellationToken));
    }

    [HttpGet("channels/{channelId:guid}/attachments/{attachmentId:guid}/download")]
    public async Task<ActionResult> DownloadChannelAttachment(Guid channelId, Guid attachmentId, CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId();
        if (!await permissionService.CanAccessChannel(userId, channelId))
        {
            return NotFound();
        }

        var attachment = await dbContext.ChannelAttachments
            .AsNoTracking()
            .Include(item => item.Blob)
            .FirstOrDefaultAsync(item => item.Id == attachmentId && item.ChannelId == channelId, cancellationToken);
        if (attachment?.Blob is null)
        {
            return NotFound();
        }

        return File(attachment.Blob.Content, attachment.ContentType, attachment.FileName);
    }

    [HttpPost("channels/{channelId:guid}/ai")]
    public async Task<ActionResult> RunChannelAi(Guid channelId, AiChannelCommandRequest request, CancellationToken cancellationToken)
    {
        var result = await aiCommandService.HandleChannelMentionAsync(channelId, request, cancellationToken);
        if (result.Success && result.Data is { SharedToChannel: true, MessageId: not null })
        {
            var message = await dbContext.Messages
                .AsNoTracking()
                .Include(item => item.Sender)
                .Include(item => item.Attachments)
                .FirstOrDefaultAsync(item => item.Id == result.Data.MessageId.Value, cancellationToken);
            if (message is not null)
            {
                await chatHubContext.Clients.Group($"channel:{channelId}").SendAsync("MessageReceived", message.ToResponse(), cancellationToken);
            }
        }

        return this.ToActionResult(result);
    }

    [HttpGet("channels/{channelId:guid}/members")]
    public async Task<ActionResult> GetChannelMembers(Guid channelId)
    {
        return this.ToActionResult(await channelService.GetChannelMembersAsync(channelId));
    }

    [HttpPost("channels/{channelId:guid}/members")]
    public async Task<ActionResult> AddChannelMember(Guid channelId, AddChannelMemberRequest request)
    {
        return this.ToActionResult(await channelService.AddChannelMemberAsync(channelId, request));
    }

    [HttpDelete("channels/{channelId:guid}/members/{userId:guid}")]
    public async Task<ActionResult> RemoveChannelMember(Guid channelId, Guid userId)
    {
        return this.ToActionResult(await channelService.RemoveChannelMemberAsync(channelId, userId));
    }

    [HttpDelete("channels/{channelId:guid}/attachments/{attachmentId:guid}")]
    public async Task<ActionResult> DeleteChannelAttachment(Guid channelId, Guid attachmentId, CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId();
        if (!await permissionService.CanAccessChannel(userId, channelId))
        {
            return NotFound();
        }

        var attachment = await dbContext.ChannelAttachments
            .Include(item => item.Blob)
            .FirstOrDefaultAsync(item => item.Id == attachmentId && item.ChannelId == channelId, cancellationToken);
        if (attachment is null)
        {
            return NotFound();
        }

        if (attachment.IsAiIndexed)
        {
            try
            {
                await pineconeVectorStore.DeleteByAttachmentAsync(attachment.WorkspaceId, attachment.Id, cancellationToken);
            }
            catch (PineconeVectorStoreException ex)
            {
                return StatusCode(
                    StatusCodes.Status502BadGateway,
                    ApiResponse.Fail<bool>(ex.Message, StatusCodes.Status502BadGateway));
            }
        }

        var knowledgeChunks = await dbContext.ChannelKnowledgeChunks
            .Where(chunk => chunk.AttachmentId == attachmentId)
            .ToListAsync(cancellationToken);
        dbContext.ChannelKnowledgeChunks.RemoveRange(knowledgeChunks);
        dbContext.ChannelAttachments.Remove(attachment);
        if (attachment.Blob is not null)
        {
            dbContext.ChannelAttachmentBlobs.Remove(attachment.Blob);
        }
        if (attachment.MessageId.HasValue)
        {
            var message = await dbContext.Messages.FirstOrDefaultAsync(m => m.Id == attachment.MessageId.Value, cancellationToken);
            if (message is not null)
            {
                dbContext.Messages.Remove(message);
            }
        }
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse.NoData("Attachment deleted."));
    }
}
