using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using TaskFlow.Api.DTOs.AI;
using TaskFlow.Api.DTOs.Channels;
using TaskFlow.Api.Helpers;
using TaskFlow.Api.Services.Interfaces;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class ChannelsController(
    IChannelService channelService,
    IAiCommandService aiCommandService) : ControllerBase
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

    [HttpPost("channels/{channelId:guid}/ai")]
    public async Task<ActionResult> RunChannelAi(Guid channelId, AiChannelCommandRequest request, CancellationToken cancellationToken)
    {
        return this.ToActionResult(await aiCommandService.HandleChannelMentionAsync(channelId, request, cancellationToken));
    }
}
