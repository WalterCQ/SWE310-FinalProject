using System.ComponentModel.DataAnnotations;

namespace TaskFlow.Api.DTOs.Auth;

public class RegisterRequest
{
    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(6)]
    public string Password { get; set; } = string.Empty;

}