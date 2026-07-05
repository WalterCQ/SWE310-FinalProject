using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.DTOs.Admin;
using TaskFlow.Api.Helpers;
using TaskFlow.Api.Models;
using TaskFlow.Api.Services.Interfaces;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = nameof(GlobalRole.Administrator))]
public class AdminController(AppDbContext dbContext, ICurrentUserService currentUser) : ControllerBase
{
    private readonly PasswordHasher<User> _passwordHasher = new();

    [HttpGet("overview")]
    public async Task<ActionResult> GetOverview(CancellationToken cancellationToken)
    {
        if (!await IsCurrentDatabaseAdministrator(cancellationToken))
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Fail<object>(
                "Administrator role is required.",
                StatusCodes.Status403Forbidden));
        }

        var userCount = await dbContext.Users.AsNoTracking().CountAsync(cancellationToken);
        var adminCount = await dbContext.Users.AsNoTracking()
            .CountAsync(user => user.GlobalRole == GlobalRole.Administrator, cancellationToken);
        var workspaceCount = await dbContext.Workspaces.AsNoTracking().CountAsync(cancellationToken);
        var projectCount = await dbContext.Projects.AsNoTracking().CountAsync(cancellationToken);
        var taskCount = await dbContext.TaskItems.AsNoTracking().CountAsync(cancellationToken);
        var channelCount = await dbContext.Channels.AsNoTracking().CountAsync(cancellationToken);
        var aiProviderCount = await dbContext.WorkspaceAiProviderCredentials.AsNoTracking().CountAsync(cancellationToken);

        var response = new AdminOverviewResponse
        {
            Metrics =
            [
                new AdminMetricResponse { Label = "Users", Value = userCount, HelpText = $"{adminCount} global administrators" },
                new AdminMetricResponse { Label = "Workspaces", Value = workspaceCount, HelpText = "Relational workspace records" },
                new AdminMetricResponse { Label = "Projects", Value = projectCount, HelpText = "Workspace-linked projects" },
                new AdminMetricResponse { Label = "Tasks", Value = taskCount, HelpText = "Project task records" },
                new AdminMetricResponse { Label = "Channels", Value = channelCount, HelpText = "Team communication channels" },
                new AdminMetricResponse { Label = "AI providers", Value = aiProviderCount, HelpText = "Workspace-scoped LLM credentials" }
            ],
            SecurityEvidence =
            [
                new AdminSecurityEvidenceResponse
                {
                    Area = "API role restriction",
                    Evidence = "This endpoint requires a Bearer JWT and a current database Administrator role."
                },
                new AdminSecurityEvidenceResponse
                {
                    Area = "Frontend route restriction",
                    Evidence = "The /admin route is hidden from standard members and blocked by ProtectedShell."
                },
                new AdminSecurityEvidenceResponse
                {
                    Area = "Permission model",
                    Evidence = "Workspace, project, channel, task, and AI operations also check relationship-based permissions per request."
                }
            ]
        };

        return Ok(ApiResponse.Ok(response));
    }

    [HttpGet("users")]
    public async Task<ActionResult> GetUsers(CancellationToken cancellationToken)
    {
        if (!await IsCurrentDatabaseAdministrator(cancellationToken))
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Fail<object>(
                "Administrator role is required.",
                StatusCodes.Status403Forbidden));
        }

        var users = await BuildUserResponseQuery()
            .OrderBy(user => user.Name)
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse.Ok(users));
    }

    [HttpPut("users/{userId:guid}")]
    public async Task<ActionResult> UpdateUser(Guid userId, AdminUpdateUserRequest request, CancellationToken cancellationToken)
    {
        if (!await IsCurrentDatabaseAdministrator(cancellationToken))
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Fail<object>(
                "Administrator role is required.",
                StatusCodes.Status403Forbidden));
        }

        var user = await dbContext.Users.FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);

        if (user is null)
        {
            return NotFound(ApiResponse.Fail<object>("User not found.", StatusCodes.Status404NotFound));
        }

        var nextRole = request.GlobalRole!.Value;

        if (userId == currentUser.GetUserId() && user.GlobalRole != nextRole)
        {
            return BadRequest(ApiResponse.Fail<object>(
                "Administrators cannot change their own global role.",
                StatusCodes.Status400BadRequest));
        }

        if (user.GlobalRole == GlobalRole.Administrator
            && nextRole != GlobalRole.Administrator
            && !await HasAnotherAdministrator(user.Id, cancellationToken))
        {
            return BadRequest(ApiResponse.Fail<object>(
                "At least one global administrator must remain.",
                StatusCodes.Status400BadRequest));
        }

        user.Name = request.Name.Trim();
        user.GlobalRole = nextRole;

        await dbContext.SaveChangesAsync(cancellationToken);

        var response = await GetUserResponse(user.Id, cancellationToken);
        return Ok(ApiResponse.Ok(response));
    }

    [HttpPut("users/{userId:guid}/password")]
    public async Task<ActionResult> ResetUserPassword(Guid userId, AdminResetUserPasswordRequest request, CancellationToken cancellationToken)
    {
        if (!await IsCurrentDatabaseAdministrator(cancellationToken))
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Fail<object>(
                "Administrator role is required.",
                StatusCodes.Status403Forbidden));
        }

        var user = await dbContext.Users.FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);

        if (user is null)
        {
            return NotFound(ApiResponse.Fail<object>("User not found.", StatusCodes.Status404NotFound));
        }

        user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse.NoData("Password reset successfully."));
    }

    private IQueryable<AdminUserResponse> BuildUserResponseQuery()
    {
        return dbContext.Users
            .AsNoTracking()
            .Select(user => new AdminUserResponse
            {
                UserId = user.Id,
                Name = user.Name,
                Email = user.Email,
                GlobalRole = user.GlobalRole.ToString(),
                CreatedAtUtc = user.CreatedAtUtc,
                WorkspaceCount = user.WorkspaceMemberships.Count,
                ProjectCount = user.ProjectMemberships.Count,
                ChannelCount = user.ChannelMemberships.Count
            });
    }

    private Task<AdminUserResponse?> GetUserResponse(Guid userId, CancellationToken cancellationToken)
    {
        return BuildUserResponseQuery()
            .FirstOrDefaultAsync(user => user.UserId == userId, cancellationToken);
    }

    private Task<bool> HasAnotherAdministrator(Guid userId, CancellationToken cancellationToken)
    {
        return dbContext.Users.AnyAsync(
            user => user.Id != userId && user.GlobalRole == GlobalRole.Administrator,
            cancellationToken);
    }

    private Task<bool> IsCurrentDatabaseAdministrator(CancellationToken cancellationToken)
    {
        var currentUserId = currentUser.GetUserId();

        return currentUserId != Guid.Empty
            ? dbContext.Users.AsNoTracking().AnyAsync(
                user => user.Id == currentUserId && user.GlobalRole == GlobalRole.Administrator,
                cancellationToken)
            : Task.FromResult(false);
    }
}
