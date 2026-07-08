using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using TaskFlow.AgentWorker;
using TaskFlow.Api.Data;
using TaskFlow.Api.Services;
using TaskFlow.Api.Services.Interfaces;

var builder = Host.CreateApplicationBuilder(args);

var dataProtectionBuilder = builder.Services.AddDataProtection()
    .SetApplicationName("TaskFlow");
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    dataProtectionBuilder.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
}
builder.Services.AddHttpClient();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<WorkerUserContext>();
builder.Services.AddScoped<ICurrentUserService>(provider => provider.GetRequiredService<WorkerUserContext>());
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IAiProviderService, AiProviderService>();
builder.Services.AddScoped<IAiContextService, AiContextService>();
builder.Services.AddScoped<IPineconeVectorStore, PineconeVectorStore>();
builder.Services.AddScoped<IGitHubRepositoryService, GitHubRepositoryService>();
builder.Services.AddScoped<AgentSkillRegistry>();
builder.Services.AddScoped<AgentJobProcessor>();
builder.Services.AddHostedService<AgentWorkerService>();

await builder.Build().RunAsync();
