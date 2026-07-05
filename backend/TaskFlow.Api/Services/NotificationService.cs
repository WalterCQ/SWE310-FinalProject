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
        await PruneDuplicateNotificationsAsync(userId);

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

    public async Task<ApiResponse<int>> MarkAllAsReadAsync()
    {
        var userId = currentUser.GetUserId();
        var notifications = await dbContext.Notifications
            .Where(notification => notification.UserId == userId && !notification.IsRead)
            .ToListAsync();

        foreach (var notification in notifications)
        {
            notification.IsRead = true;
        }

        if (notifications.Count > 0)
        {
            await dbContext.SaveChangesAsync();
        }

        return ApiResponse.Ok(notifications.Count, "All notifications marked as read.");
    }

    public async Task<ApiResponse<NotificationResponse>> CreateNotificationAsync(CreateNotificationRequest request)
    {
        var title = request.Title.Trim();
        var message = request.Message.Trim();
        var duplicateCutoff = DateTime.UtcNow.AddHours(-24);

        var duplicate = await dbContext.Notifications
            .Where(notification =>
                notification.UserId == request.UserId
                && notification.WorkspaceId == request.WorkspaceId
                && notification.Type == request.Type
                && notification.Title == title
                && notification.Message == message
                && notification.CreatedAtUtc >= duplicateCutoff)
            .OrderByDescending(notification => notification.CreatedAtUtc)
            .FirstOrDefaultAsync();

        if (duplicate is not null)
        {
            return ApiResponse.Ok(duplicate.ToResponse(), "Notification already exists.");
        }

        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId,
            WorkspaceId = request.WorkspaceId,
            Title = title,
            Message = message,
            Type = request.Type
        };

        dbContext.Notifications.Add(notification);
        await dbContext.SaveChangesAsync();

        return ApiResponse.Created(notification.ToResponse(), "Notification created.");
    }

    private async Task PruneDuplicateNotificationsAsync(Guid userId)
    {
        var notifications = await dbContext.Notifications
            .Where(notification => notification.UserId == userId)
            .OrderByDescending(notification => notification.CreatedAtUtc)
            .ToListAsync();
        var duplicateGroups = notifications
            .GroupBy(notification => BuildDuplicateKey(notification))
            .Where(group => group.Count() > 1)
            .ToList();

        if (duplicateGroups.Count == 0)
        {
            return;
        }

        foreach (var group in duplicateGroups)
        {
            var keep = group.First();
            keep.IsRead = group.All(notification => notification.IsRead);
            dbContext.Notifications.RemoveRange(group.Skip(1));
        }

        await dbContext.SaveChangesAsync();
    }

    private static string BuildDuplicateKey(Notification notification)
    {
        return string.Join("|", [
            notification.UserId.ToString(),
            notification.WorkspaceId?.ToString() ?? "",
            notification.Type.ToString(),
            notification.Title,
            notification.Message
        ]);
    }
}
