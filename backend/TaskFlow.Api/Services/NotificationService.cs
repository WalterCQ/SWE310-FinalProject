using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.DTOs.Notifications;
using TaskFlow.Api.Helpers;
using TaskFlow.Api.Models;
using TaskFlow.Api.Services.Interfaces;

namespace TaskFlow.Api.Services;

public class NotificationService(
    AppDbContext dbContext,
    ICurrentUserService currentUser) : INotificationService
{
    public async Task<ApiResponse<IEnumerable<NotificationResponse>>> GetMyNotificationsAsync()
    {
        if (!currentUser.IsAuthenticated())
        {
            return ApiResponse.Fail<IEnumerable<NotificationResponse>>("Authentication is required.", StatusCodes.Status401Unauthorized);
        }

        var userId = currentUser.GetUserId();
        var notifications = await dbContext.Notifications
            .Where(notification => notification.UserId == userId)
            .OrderByDescending(notification => notification.CreatedAtUtc)
            .Take(100)
            .ToListAsync();

        return ApiResponse.Ok(notifications.Select(notification => notification.ToResponse()));
    }

    public async Task<ApiResponse<NotificationResponse>> MarkAsReadAsync(Guid notificationId)
    {
        var userId = currentUser.GetUserId();
        var notification = await dbContext.Notifications
            .FirstOrDefaultAsync(item => item.Id == notificationId && item.UserId == userId);

        if (notification is null)
        {
            return ApiResponse.Fail<NotificationResponse>("Notification not found.", StatusCodes.Status404NotFound);
        }

        notification.IsRead = true;
        await dbContext.SaveChangesAsync();

        return ApiResponse.Ok(notification.ToResponse(), "Notification marked as read.");
    }

    public async Task<ApiResponse<NotificationResponse>> CreateNotificationAsync(CreateNotificationRequest request)
    {
        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId,
            WorkspaceId = request.WorkspaceId,
            Title = request.Title.Trim(),
            Message = request.Message.Trim(),
            Type = request.Type
        };

        dbContext.Notifications.Add(notification);
        await dbContext.SaveChangesAsync();

        return ApiResponse.Created(notification.ToResponse(), "Notification created.");
    }
}
