using System.ComponentModel.DataAnnotations;
using TaskFlow.Api.Helpers;

namespace TaskFlow.Api.DTOs.Auth;

public class RegisterRequest
{
    [Required, MaxLength(120), NonWhiteSpace]
    public string Name { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(256), NonWhiteSpace]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(6), NonWhiteSpace]
    public string Password { get; set; } = string.Empty;

}
