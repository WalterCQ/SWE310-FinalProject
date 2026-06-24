using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.Helpers;
using TaskFlow.Api.Services.Interfaces;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api/notifications")]
// TODO: Add [Authorize] when the teammate's JWT module is connected.
public class NotificationsController(INotificationService notificationService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> GetNotifications()
    {
        return this.ToActionResult(await notificationService.GetMyNotificationsAsync());
    }

    [HttpPut("{notificationId:guid}/read")]
    public async Task<ActionResult> MarkAsRead(Guid notificationId)
    {
        return this.ToActionResult(await notificationService.MarkAsReadAsync(notificationId));
    }
}
