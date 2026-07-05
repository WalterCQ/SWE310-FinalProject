using System.ComponentModel.DataAnnotations;

namespace TaskFlow.Api.DTOs.Channels;

public class CreateChannelRequest
{
    [Required, StringLength(120)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    public bool IsPrivate { get; set; }
}

public class ChannelResponse
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsPrivate { get; set; }
    public Guid CreatedByUserId { get; set; }
    public int MemberCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class AddChannelMemberRequest
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;
}

public class ChannelMemberResponse
{
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime JoinedAtUtc { get; set; }
}
