using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using TaskFlow.Api.DTOs.Messages;
using TaskFlow.Api.Helpers;
using TaskFlow.Api.Services.Interfaces;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class MessagesController(IMessageService messageService) : ControllerBase
{
    [HttpPost("channels/{channelId:guid}/messages")]
    public async Task<ActionResult> CreateMessage(Guid channelId, CreateMessageRequest request)
    {
        return this.ToActionResult(await messageService.CreateMessageAsync(channelId, request));
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
