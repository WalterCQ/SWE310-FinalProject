using TaskFlow.Api.DTOs.Channels;
using TaskFlow.Api.DTOs.Messages;
using TaskFlow.Api.Helpers;

namespace TaskFlow.Api.Services.Interfaces;

public interface IChannelService
{
    Task<ApiResponse<IEnumerable<ChannelResponse>>> GetWorkspaceChannelsAsync(Guid workspaceId);
    Task<ApiResponse<ChannelResponse>> CreateChannelAsync(Guid workspaceId, CreateChannelRequest request);
    Task<ApiResponse<IEnumerable<MessageResponse>>> GetChannelMessagesAsync(Guid channelId);
    Task<ApiResponse<IEnumerable<ChannelMemberResponse>>> GetChannelMembersAsync(Guid channelId);
    Task<ApiResponse<ChannelMemberResponse>> AddChannelMemberAsync(Guid channelId, AddChannelMemberRequest request);
    Task<ApiResponse<bool>> RemoveChannelMemberAsync(Guid channelId, Guid userId);
}
