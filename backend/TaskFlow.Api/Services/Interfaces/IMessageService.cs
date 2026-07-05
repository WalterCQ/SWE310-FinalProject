using TaskFlow.Api.DTOs.Messages;
using TaskFlow.Api.Helpers;

namespace TaskFlow.Api.Services.Interfaces;

public interface IMessageService
{
    Task<ApiResponse<MessageResponse>> CreateMessageAsync(Guid channelId, CreateMessageRequest request);
    Task<ApiResponse<MessageResponse>> CreateAttachmentMessageAsync(Guid channelId, CreateMessageAttachmentRequest request);
    Task<ApiResponse<MessageResponse>> UpdateMessageAsync(Guid messageId, UpdateMessageRequest request);
    Task<ApiResponse<bool>> DeleteMessageAsync(Guid messageId);
}
