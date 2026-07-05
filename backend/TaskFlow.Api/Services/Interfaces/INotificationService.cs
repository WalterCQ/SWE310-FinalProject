using TaskFlow.Api.DTOs.Notifications;
using TaskFlow.Api.Helpers;

namespace TaskFlow.Api.Services.Interfaces;

public interface INotificationService
{
    Task<ApiResponse<IEnumerable<NotificationResponse>>> GetMyNotificationsAsync();
    Task<ApiResponse<NotificationResponse>> MarkAsReadAsync(Guid notificationId);
    Task<ApiResponse<int>> MarkAllAsReadAsync();
    Task<ApiResponse<NotificationResponse>> CreateNotificationAsync(CreateNotificationRequest request);
}
