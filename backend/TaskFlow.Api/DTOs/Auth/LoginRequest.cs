using System.ComponentModel.DataAnnotations;
using TaskFlow.Api.Helpers;

namespace TaskFlow.Api.DTOs.Auth;

public class LoginRequest
{
    [Required, EmailAddress, NonWhiteSpace]
    public string Email { get; set; } = string.Empty;

    [Required, NonWhiteSpace]
    public string Password { get; set; } = string.Empty;
    
}
