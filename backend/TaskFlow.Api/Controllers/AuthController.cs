using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using TaskFlow.Api.Data;
using TaskFlow.Api.Data.DemoData;
using TaskFlow.Api.DTOs.Auth;
using TaskFlow.Api.Helpers;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController (AppDbContext dbContext, IConfiguration configuration, IWebHostEnvironment environment) : ControllerBase
{
    private readonly PasswordHasher<User> _passwordHasher = new();

    /// <summary> Register a new user account. </summary>
    [HttpPost("register")]
    public async Task<ActionResult> Register(RegisterRequest request)
    {
        if (await dbContext.Users.AnyAsync(u => u.Email == request.Email.Trim().ToLower()))
        {
            return BadRequest(ApiResponse.Fail<object>("Email already registered.", StatusCodes.Status400BadRequest));
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = request.Email.Trim().ToLower(),
            Name = request.Name.Trim(),
            GlobalRole = GlobalRole.Member
        };

        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        dbContext.Users.Add(user);
        var demoAccessGranted = await DemoDataAccess.GrantUserDemoDataAccessAsync(dbContext, user.Id);
        await dbContext.SaveChangesAsync();

        var message = demoAccessGranted
            ? "Registration successful. Please log in."
            : "Registration successful. Demo workspace is not available yet; run the Azure demo data seeder before showcasing shared data.";

        return Ok(ApiResponse.NoData(message));
    }

    /// <summary> Log in and receive JWT </summary>
    [HttpPost("login")]
    public async Task<ActionResult> Login(LoginRequest request)
    {
        var user = await dbContext.Users
            .FirstOrDefaultAsync(u => u.Email == request.Email.Trim().ToLower());

        if (user is null)
        {
            return Unauthorized(ApiResponse.Fail<object>(
                "Invalid email or password.", StatusCodes.Status401Unauthorized));
        }

        var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            return Unauthorized(ApiResponse.Fail<object>(
                "Invalid email or password.", StatusCodes.Status401Unauthorized));
        }

        var token = GenerateJwtToken(user);
        
        var response = new AuthResponse
        {
            Token = token,
            UserId = user.Id,
            Name = user.Name,
            Email = user.Email,
            Role = user.GlobalRole.ToString()
        };

        return Ok(ApiResponse.Ok(response, "Login successful."));
    }

    /// <summary> Return the authenticated user's profile and global role. </summary>
    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult> Me()
    {
        var claimUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue("userId");

        if (!Guid.TryParse(claimUserId, out var userId))
        {
            return Unauthorized(ApiResponse.Fail<object>("Invalid authentication token.", StatusCodes.Status401Unauthorized));
        }

        var user = await dbContext.Users.AsNoTracking().FirstOrDefaultAsync(item => item.Id == userId);
        if (user is null)
        {
            return Unauthorized(ApiResponse.Fail<object>("Invalid authentication token.", StatusCodes.Status401Unauthorized));
        }

        var response = new CurrentUserResponse
        {
            UserId = userId,
            Name = user.Name,
            Email = user.Email,
            GlobalRole = user.GlobalRole.ToString(),
            RoleSource = "Database"
        };

        return Ok(ApiResponse.Ok(response));
    }

    private string GenerateJwtToken(User user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.GlobalRole.ToString()),
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            Issuer = configuration["Jwt:Issuer"] ?? "TaskFlowConnect",
            Audience = configuration["Jwt:Audience"] ?? "TaskFlowConnectClient",
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(JwtConfiguration.GetSigningKeyBytes(configuration, environment)),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(tokenDescriptor));
    }
}
