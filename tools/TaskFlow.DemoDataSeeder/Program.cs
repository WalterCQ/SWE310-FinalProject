using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.Data.DemoData;
using TaskFlow.Api.Models;

var connectionString = Environment.GetEnvironmentVariable("TASKFLOW_AZURE_SQL_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Missing TASKFLOW_AZURE_SQL_CONNECTION_STRING. Set it to the Azure SQL connection string before running this seeder.");
    return 1;
}

try
{
    var options = new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer(connectionString)
        .Options;

    await using var dbContext = new AppDbContext(options);
    await dbContext.Database.OpenConnectionAsync();
    await dbContext.Database.CloseConnectionAsync();

    var pendingMigrations = (await dbContext.Database.GetPendingMigrationsAsync()).ToArray();
    if (pendingMigrations.Length > 0)
    {
        Console.Error.WriteLine("The database has pending migrations. Apply them before seeding demo data:");
        foreach (var migration in pendingMigrations)
        {
            Console.Error.WriteLine($"- {migration}");
        }

        return 1;
    }

    await using var transaction = await dbContext.Database.BeginTransactionAsync();

    var userIds = await SeedUsersAsync(dbContext);
    await dbContext.SaveChangesAsync();

    await SeedWorkspaceAsync(dbContext, userIds["demo@taskflow.com"]);
    await SeedChannelsAsync(dbContext, userIds["demo@taskflow.com"]);
    await SeedProjectsAsync(dbContext, userIds["demo@taskflow.com"]);
    await SeedTasksAsync(dbContext, userIds);
    await SeedMessagesAsync(dbContext, userIds);
    await SeedActivityLogsAsync(dbContext, userIds["demo@taskflow.com"]);
    await dbContext.SaveChangesAsync();

    await GrantAccessToAllUsersAsync(dbContext, userIds["demo@taskflow.com"]);
    await dbContext.SaveChangesAsync();

    await transaction.CommitAsync();

    var userCount = await dbContext.Users.CountAsync();
    Console.WriteLine($"Seeded TaskFlow demo data. Existing users with demo access: {userCount}.");
    Console.WriteLine("Demo login: demo@taskflow.com / Demo123!");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Demo data seeding failed: {ex.Message}");
    return 1;
}

static async Task<Dictionary<string, Guid>> SeedUsersAsync(AppDbContext dbContext)
{
    var users = new[]
    {
        new SeedUser("TaskFlow Demo", "demo@taskflow.com", "Demo123!", GlobalRole.User),
        new SeedUser("Oday Frontend", "oday@taskflow.com", "Demo123!", GlobalRole.User),
        new SeedUser("John Backend", "john@taskflow.com", "Demo123!", GlobalRole.User),
        new SeedUser("Sarah AI", "sarah@taskflow.com", "Demo123!", GlobalRole.User)
    };
    var passwordHasher = new PasswordHasher<User>();
    var result = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

    foreach (var seedUser in users)
    {
        var email = seedUser.Email.Trim().ToLowerInvariant();
        var user = await dbContext.Users.FirstOrDefaultAsync(item => item.Email == email);
        if (user is null)
        {
            user = new User
            {
                Id = Guid.NewGuid(),
                Name = seedUser.Name,
                Email = email,
                GlobalRole = seedUser.Role,
                CreatedAtUtc = DateTime.UtcNow.AddDays(-12)
            };
            dbContext.Users.Add(user);
        }
        else
        {
            user.Name = seedUser.Name;
            user.GlobalRole = seedUser.Role;
        }

        user.PasswordHash = passwordHasher.HashPassword(user, seedUser.Password);
        result[email] = user.Id;
    }

    return result;
}

static async Task SeedWorkspaceAsync(AppDbContext dbContext, Guid ownerUserId)
{
    var workspace = await dbContext.Workspaces.FindAsync(DemoDataIds.WorkspaceId);
    if (workspace is null)
    {
        workspace = new Workspace
        {
            Id = DemoDataIds.WorkspaceId,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-10)
        };
        dbContext.Workspaces.Add(workspace);
    }

    workspace.Name = DemoDataIds.WorkspaceName;
    workspace.Description = "Shared Azure demo workspace for the SWE310 TaskFlow final project.";
    workspace.CreatedByUserId = ownerUserId;
    workspace.UpdatedAtUtc = DateTime.UtcNow;
}

static async Task SeedChannelsAsync(AppDbContext dbContext, Guid ownerUserId)
{
    var channels = new[]
    {
        new SeedChannel(DemoDataIds.GeneralChannelId, "general", "Daily delivery notes and presentation coordination.", false, -9),
        new SeedChannel(DemoDataIds.FrontendChannelId, "frontend", "Dashboard, task board, and validation UI discussion.", false, -8),
        new SeedChannel(DemoDataIds.ApiChannelId, "api-integration", "Backend endpoint, Azure, and Swagger integration updates.", false, -7),
        new SeedChannel(DemoDataIds.ReviewChannelId, "demo-review", "Private rehearsal notes for the final recording.", true, -6)
    };

    foreach (var seedChannel in channels)
    {
        var channel = await dbContext.Channels.FindAsync(seedChannel.Id);
        if (channel is null)
        {
            channel = new Channel
            {
                Id = seedChannel.Id,
                WorkspaceId = DemoDataIds.WorkspaceId,
                CreatedAtUtc = DateTime.UtcNow.AddDays(seedChannel.CreatedDaysAgo)
            };
            dbContext.Channels.Add(channel);
        }

        channel.Name = seedChannel.Name;
        channel.Description = seedChannel.Description;
        channel.IsPrivate = seedChannel.IsPrivate;
        channel.CreatedByUserId = ownerUserId;
    }
}

static async Task SeedProjectsAsync(AppDbContext dbContext, Guid ownerUserId)
{
    var projects = new[]
    {
        new SeedProject(
            DemoDataIds.FrontendProjectId,
            "Frontend walkthrough",
            "Show live Azure data across dashboard, workspace cards, task forms, and notification states.",
            ProjectStatus.Active,
            DateTime.UtcNow.AddDays(4)),
        new SeedProject(
            DemoDataIds.BackendProjectId,
            "API and Azure integration",
            "Keep authentication, authorization, SQL Server data, and Swagger behavior stable for the final demo.",
            ProjectStatus.Active,
            DateTime.UtcNow.AddDays(2)),
        new SeedProject(
            DemoDataIds.AiProjectId,
            "AI project summary",
            "Use project, task, and channel records as grounded context for stakeholder summaries and risk analysis.",
            ProjectStatus.Planned,
            DateTime.UtcNow.AddDays(7)),
        new SeedProject(
            DemoDataIds.PresentationProjectId,
            "Final presentation package",
            "Prepare screenshots, demo script, validation checklist, and backup evidence for submission.",
            ProjectStatus.Completed,
            DateTime.UtcNow.AddDays(-1))
    };

    foreach (var seedProject in projects)
    {
        var project = await dbContext.Projects.FindAsync(seedProject.Id);
        if (project is null)
        {
            project = new Project
            {
                Id = seedProject.Id,
                WorkspaceId = DemoDataIds.WorkspaceId,
                CreatedAtUtc = DateTime.UtcNow.AddDays(-8)
            };
            dbContext.Projects.Add(project);
        }

        project.Name = seedProject.Name;
        project.Description = seedProject.Description;
        project.Status = seedProject.Status;
        project.CreatedByUserId = ownerUserId;
        project.DeadlineUtc = seedProject.DeadlineUtc;
        project.UpdatedAtUtc = DateTime.UtcNow;
    }
}

static async Task SeedTasksAsync(AppDbContext dbContext, IReadOnlyDictionary<string, Guid> userIds)
{
    var tasks = new[]
    {
        new SeedTask(
            Guid.Parse("e5752ed6-70a2-4685-aa6e-7e7a9d37b632"),
            DemoDataIds.FrontendProjectId,
            "Record dashboard walkthrough",
            "Show stats, status chart, priority chart, recent activity, and active project progress using Azure data.",
            TaskItemStatus.InProgress,
            TaskPriority.High,
            "oday@taskflow.com",
            DateTime.UtcNow.AddDays(2)),
        new SeedTask(
            Guid.Parse("914365e7-f5b8-49d7-ab3f-1bc00d943c59"),
            DemoDataIds.FrontendProjectId,
            "Verify task creation form",
            "Create one task from the Tasks page and confirm the kanban columns refresh without manual reload.",
            TaskItemStatus.Todo,
            TaskPriority.High,
            "demo@taskflow.com",
            DateTime.UtcNow.AddDays(1)),
        new SeedTask(
            Guid.Parse("99bf3629-9084-4f95-8ac4-f1536d6f30da"),
            DemoDataIds.BackendProjectId,
            "Smoke test authenticated endpoints",
            "Check workspaces, dashboard, projects, tasks, channels, notifications, and AI endpoints with a real JWT.",
            TaskItemStatus.Blocked,
            TaskPriority.Medium,
            "john@taskflow.com",
            DateTime.UtcNow.AddDays(-1)),
        new SeedTask(
            Guid.Parse("76e549e6-f432-4d95-bc18-213c0cc955c1"),
            DemoDataIds.BackendProjectId,
            "Confirm all users can access demo workspace",
            "Existing and newly registered users should receive workspace, channel, and project membership automatically.",
            TaskItemStatus.Done,
            TaskPriority.High,
            "john@taskflow.com",
            DateTime.UtcNow.AddDays(-2)),
        new SeedTask(
            Guid.Parse("ba47a50d-a65e-4479-9a87-5c07fc16f8f7"),
            DemoDataIds.AiProjectId,
            "Prepare AI risk summary prompt",
            "Use overdue tasks and completion percentage to demonstrate deterministic AI fallback output.",
            TaskItemStatus.InProgress,
            TaskPriority.Medium,
            "sarah@taskflow.com",
            DateTime.UtcNow.AddDays(3)),
        new SeedTask(
            Guid.Parse("00b6ad2f-c05b-4978-9113-9bd80aed4bbb"),
            DemoDataIds.AiProjectId,
            "Add grounded knowledge examples",
            "Keep messages and task descriptions specific enough for the workspace knowledge endpoint.",
            TaskItemStatus.Todo,
            TaskPriority.Low,
            "sarah@taskflow.com",
            DateTime.UtcNow.AddDays(6)),
        new SeedTask(
            Guid.Parse("89895b06-027f-45bb-a01a-6a0cfccf1fd4"),
            DemoDataIds.PresentationProjectId,
            "Capture backup screenshots",
            "Save dashboard, projects, tasks, channels, AI assistant, and notifications evidence before recording.",
            TaskItemStatus.Done,
            TaskPriority.Medium,
            "demo@taskflow.com",
            DateTime.UtcNow.AddDays(-3)),
        new SeedTask(
            Guid.Parse("f13f8f0e-b2d9-48d9-92b8-aa5d62f0ecb3"),
            DemoDataIds.PresentationProjectId,
            "Rehearse final route order",
            "Login, dashboard, projects, tasks, channels, notifications, then AI assistant.",
            TaskItemStatus.Done,
            TaskPriority.Low,
            "oday@taskflow.com",
            DateTime.UtcNow.AddDays(-1))
    };

    foreach (var seedTask in tasks)
    {
        var task = await dbContext.TaskItems.FindAsync(seedTask.Id);
        if (task is null)
        {
            task = new TaskItem
            {
                Id = seedTask.Id,
                CreatedAtUtc = DateTime.UtcNow.AddDays(-5)
            };
            dbContext.TaskItems.Add(task);
        }

        task.ProjectId = seedTask.ProjectId;
        task.Title = seedTask.Title;
        task.Description = seedTask.Description;
        task.Status = seedTask.Status;
        task.Priority = seedTask.Priority;
        task.CreatedByUserId = userIds["demo@taskflow.com"];
        task.AssigneeId = userIds[seedTask.AssigneeEmail];
        task.DeadlineUtc = seedTask.DeadlineUtc;
        task.UpdatedAtUtc = DateTime.UtcNow;
        task.CompletedAtUtc = seedTask.Status == TaskItemStatus.Done ? DateTime.UtcNow.AddDays(-1) : null;
    }
}

static async Task SeedMessagesAsync(AppDbContext dbContext, IReadOnlyDictionary<string, Guid> userIds)
{
    var messages = new[]
    {
        new SeedMessage(Guid.Parse("3bcb1bb4-792c-4bd2-a7d9-9f6e48eaf4e5"), DemoDataIds.GeneralChannelId, "demo@taskflow.com", "Azure demo data is loaded. Use the shared workspace for every feature screen.", -180),
        new SeedMessage(Guid.Parse("1e54589c-5e96-466c-979b-fb2a3b98e746"), DemoDataIds.GeneralChannelId, "oday@taskflow.com", "Dashboard charts now have task status, priority, overdue, and activity examples.", -150),
        new SeedMessage(Guid.Parse("90dfdbdb-120b-4c2a-b05a-3355f7ec9607"), DemoDataIds.FrontendChannelId, "oday@taskflow.com", "I will show login, dashboard, task creation, channels, notifications, and AI in that order.", -110),
        new SeedMessage(Guid.Parse("2a64a8dd-8c9c-40e7-b950-d30ccce94ffc"), DemoDataIds.ApiChannelId, "john@taskflow.com", "The seeder grants workspace, project, and channel access to all existing users.", -90),
        new SeedMessage(Guid.Parse("69f43fc7-d795-4f98-8b90-82134229ed9d"), DemoDataIds.ApiChannelId, "sarah@taskflow.com", "AI fallback can summarize project risk from the seeded completion and overdue counts.", -60),
        new SeedMessage(Guid.Parse("455e8a7a-9339-4a7e-a360-95ff2686b70a"), DemoDataIds.ReviewChannelId, "demo@taskflow.com", "Private channel access is included so reviewers can see the private chat path too.", -30)
    };

    foreach (var seedMessage in messages)
    {
        var message = await dbContext.Messages.FindAsync(seedMessage.Id);
        if (message is null)
        {
            message = new Message
            {
                Id = seedMessage.Id
            };
            dbContext.Messages.Add(message);
        }

        message.ChannelId = seedMessage.ChannelId;
        message.SenderId = userIds[seedMessage.SenderEmail];
        message.Content = seedMessage.Content;
        message.IsDeleted = false;
        message.CreatedAtUtc = DateTime.UtcNow.AddMinutes(seedMessage.MinutesAgo);
        message.EditedAtUtc = null;
    }
}

static async Task SeedActivityLogsAsync(AppDbContext dbContext, Guid ownerUserId)
{
    var activityLogs = new[]
    {
        new SeedActivityLog(Guid.Parse("96d24d41-cd05-4f8b-b113-f149277c2feb"), "Created", "Workspace", DemoDataIds.WorkspaceId, "Demo workspace prepared for all users.", -5),
        new SeedActivityLog(Guid.Parse("79a3070f-2df0-4efe-9c7c-30ce713a174d"), "Updated", "Project", DemoDataIds.BackendProjectId, "Azure API smoke-test task moved to blocked for risk evidence.", -4),
        new SeedActivityLog(Guid.Parse("7f5bec67-85e5-45d3-a8b1-c0453766375f"), "Completed", "Task", Guid.Parse("76e549e6-f432-4d95-bc18-213c0cc955c1"), "Confirmed demo workspace membership coverage.", -3),
        new SeedActivityLog(Guid.Parse("8288f8ef-c089-4574-bd55-2c52de0eda76"), "Added", "Message", DemoDataIds.GeneralChannelId, "Frontend route order confirmed for recording.", -2),
        new SeedActivityLog(Guid.Parse("25d08c2c-1785-498f-a9e6-4ce2a3c86c16"), "Generated", "AI Summary", DemoDataIds.AiProjectId, "AI context now includes messages, tasks, and project dashboard counts.", -1)
    };

    foreach (var seedActivityLog in activityLogs)
    {
        var activityLog = await dbContext.ActivityLogs.FindAsync(seedActivityLog.Id);
        if (activityLog is null)
        {
            activityLog = new ActivityLog
            {
                Id = seedActivityLog.Id,
                WorkspaceId = DemoDataIds.WorkspaceId
            };
            dbContext.ActivityLogs.Add(activityLog);
        }

        activityLog.UserId = ownerUserId;
        activityLog.Action = seedActivityLog.Action;
        activityLog.EntityType = seedActivityLog.EntityType;
        activityLog.EntityId = seedActivityLog.EntityId;
        activityLog.Details = seedActivityLog.Details;
        activityLog.CreatedAtUtc = DateTime.UtcNow.AddHours(seedActivityLog.HoursAgo);
    }
}

static async Task GrantAccessToAllUsersAsync(AppDbContext dbContext, Guid ownerUserId)
{
    var userIds = await dbContext.Users.Select(user => user.Id).ToListAsync();
    foreach (var userId in userIds)
    {
        var workspaceRole = userId == ownerUserId ? WorkspaceRole.Owner : WorkspaceRole.Member;
        var projectRole = userId == ownerUserId ? ProjectRole.ProjectManager : ProjectRole.Contributor;
        var granted = await DemoDataAccess.GrantUserDemoDataAccessAsync(dbContext, userId, workspaceRole, projectRole);
        if (!granted)
        {
            throw new InvalidOperationException("Demo workspace was not found after seeding.");
        }
    }
}

internal sealed record SeedUser(string Name, string Email, string Password, GlobalRole Role);

internal sealed record SeedChannel(
    Guid Id,
    string Name,
    string Description,
    bool IsPrivate,
    int CreatedDaysAgo);

internal sealed record SeedProject(
    Guid Id,
    string Name,
    string Description,
    ProjectStatus Status,
    DateTime DeadlineUtc);

internal sealed record SeedTask(
    Guid Id,
    Guid ProjectId,
    string Title,
    string Description,
    TaskItemStatus Status,
    TaskPriority Priority,
    string AssigneeEmail,
    DateTime DeadlineUtc);

internal sealed record SeedMessage(
    Guid Id,
    Guid ChannelId,
    string SenderEmail,
    string Content,
    int MinutesAgo);

internal sealed record SeedActivityLog(
    Guid Id,
    string Action,
    string EntityType,
    Guid EntityId,
    string Details,
    int HoursAgo);
