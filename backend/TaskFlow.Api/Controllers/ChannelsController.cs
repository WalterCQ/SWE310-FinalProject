using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using TaskFlow.Api.DTOs.Channels;
using TaskFlow.Api.Helpers;
using TaskFlow.Api.Services.Interfaces;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class ChannelsController(IChannelService channelService) : ControllerBase
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
}
