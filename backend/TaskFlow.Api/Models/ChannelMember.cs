namespace TaskFlow.Api.Models;

public class ChannelMember
{
    public Guid Id { get; set; }
    public Guid ChannelId { get; set; }
    public Guid UserId { get; set; }
    public DateTime JoinedAtUtc { get; set; } = DateTime.UtcNow;

    public Channel? Channel { get; set; }
    public User? User { get; set; }
}
