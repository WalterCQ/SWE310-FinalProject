using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using TaskFlow.Api.DTOs.Messages;
using TaskFlow.Api.Helpers;
using TaskFlow.Api.Hubs;
using TaskFlow.Api.Services.Interfaces;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class MessagesController(
    IMessageService messageService,
    IHubContext<ChatHub> chatHubContext) : ControllerBase
{
    [HttpPost("channels/{channelId:guid}/messages")]
    public async Task<ActionResult> CreateMessage(Guid channelId, CreateMessageRequest request)
    {
        return this.ToActionResult(await messageService.CreateMessageAsync(channelId, request));
    }

    [HttpPost("channels/{channelId:guid}/messages/attachments")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult> CreateAttachmentMessage(Guid channelId, [FromForm] CreateMessageAttachmentRequest request)
    {
        var result = await messageService.CreateAttachmentMessageAsync(channelId, request);
        if (result.Success && result.Data is not null)
        {
            await chatHubContext.Clients.Group($"channel:{channelId}").SendAsync("MessageReceived", result.Data);
        }

        return this.ToActionResult(result);
    }

    [HttpPut("messages/{messageId:guid}")]
    public async Task<ActionResult> UpdateMessage(Guid messageId, UpdateMessageRequest request)
    {
        return this.ToActionResult(await messageService.UpdateMessageAsync(messageId, request));
    }

    [HttpDelete("messages/{messageId:guid}")]
    public async Task<ActionResult> DeleteMessage(Guid messageId)
    {
        return this.ToActionResult(await messageService.DeleteMessageAsync(messageId));
    }
}
