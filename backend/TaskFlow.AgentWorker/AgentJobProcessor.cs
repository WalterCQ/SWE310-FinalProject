using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.DTOs.Notifications;
using TaskFlow.Api.DTOs.Tasks;
using TaskFlow.Api.Models;
using TaskFlow.Api.Services;
using TaskFlow.Api.Services.Interfaces;

namespace TaskFlow.AgentWorker;

public class AgentJobProcessor(
    AppDbContext dbContext,
    WorkerUserContext workerUserContext,
    IPermissionService permissionService,
    ITaskService taskService,
    INotificationService notificationService,
    IDashboardService dashboardService,
    IAiProviderService aiProviderService,
    IDataProtectionProvider dataProtectionProvider,
    IHttpClientFactory httpClientFactory,
    ILogger<AgentJobProcessor> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<bool> ProcessNextAsync(string workerId, CancellationToken cancellationToken)
    {
        var job = await LockNextJobAsync(workerId, cancellationToken);
        if (job is null)
        {
            return false;
        }

        workerUserContext.UserId = job.UserId;

        try
        {
            if (job.Status == AgentJobStatus.Planning)
            {
                await BuildPlanAsync(job, cancellationToken);
            }
            else if (job.Status == AgentJobStatus.Running)
            {
                await RunApprovedWorkAsync(job, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Agent job {JobId} failed.", job.Id);
            job.Status = AgentJobStatus.Failed;
            job.ErrorMessage = ex.Message;
            job.CompletedAtUtc = DateTime.UtcNow;
            AddEvent(job.Id, job.UserId, AgentEventType.Error, $"Agent job failed: {ex.Message}");
        }
        finally
        {
            job.LockedBy = null;
            job.LockedAtUtc = null;
            job.UpdatedAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    private async Task<AgentJob?> LockNextJobAsync(string workerId, CancellationToken cancellationToken)
    {
        var staleLockCutoff = DateTime.UtcNow.AddMinutes(-5);
        var job = await dbContext.AgentJobs
            .Where(item =>
                (item.Status == AgentJobStatus.Planning || item.Status == AgentJobStatus.Running)
                && (item.LockedAtUtc == null || item.LockedAtUtc < staleLockCutoff))
            .OrderBy(item => item.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (job is null)
        {
            return null;
        }

        job.LockedBy = workerId;
        job.LockedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return job;
    }

    private async Task BuildPlanAsync(AgentJob job, CancellationToken cancellationToken)
    {
        var step = AddStep(job.Id, "Build execution plan", "MainAgent", 10);
        job.CurrentSubAgent = "MainAgent";
        step.Status = AgentStepStatus.Running;
        step.StartedAtUtc = DateTime.UtcNow;
        AddEvent(job.Id, job.UserId, AgentEventType.Planning, "Main Agent is building the execution plan.");

        var subAgents = InferSubAgents(job.Goal);
        var providerNotes = await TryAskProviderForPlanNotesAsync(job, subAgents, cancellationToken);
        var plan = new AgentPlan(
            "Main Agent will coordinate subagents, then request explicit approval before every TaskFlow write.",
            subAgents,
            providerNotes,
            [
                "All write actions are dry-run previews first.",
                "Approvals are decided by the job owner or workspace manager.",
                "Worker calls TaskFlow services; the LLM never writes to the database directly."
            ]);

        job.PlanJson = JsonSerializer.Serialize(plan, JsonOptions);
        step.OutputJson = job.PlanJson;
        step.Status = AgentStepStatus.WaitingForApproval;
        step.CompletedAtUtc = DateTime.UtcNow;

        dbContext.AgentApprovals.Add(new AgentApproval
        {
            Id = Guid.NewGuid(),
            AgentJobId = job.Id,
            AgentStepId = step.Id,
            ApprovalType = "Plan",
            Title = "Approve Agent execution plan",
            PreviewJson = job.PlanJson,
            PayloadJson = job.PlanJson,
            RequestedByUserId = job.UserId
        });

        job.Status = AgentJobStatus.AwaitingApproval;
        job.UpdatedAtUtc = DateTime.UtcNow;
        AddEvent(job.Id, job.UserId, AgentEventType.ApprovalRequested, "Plan approval is required before execution.");
    }

    private async Task RunApprovedWorkAsync(AgentJob job, CancellationToken cancellationToken)
    {
        await ExecuteApprovedActionsAsync(job, cancellationToken);

        var pendingApprovalExists = await dbContext.AgentApprovals
            .AnyAsync(approval => approval.AgentJobId == job.Id && approval.Status == AgentApprovalStatus.Pending, cancellationToken);
        if (pendingApprovalExists)
        {
            job.Status = AgentJobStatus.NeedsApproval;
            job.UpdatedAtUtc = DateTime.UtcNow;
            return;
        }

        var alreadyOrchestrated = await dbContext.AgentSteps
            .AnyAsync(step => step.AgentJobId == job.Id && step.Name == "Run subagents", cancellationToken);
        if (alreadyOrchestrated)
        {
            CompleteJob(job);
            return;
        }

        await RunSubagentsAsync(job, cancellationToken);

        pendingApprovalExists = await dbContext.AgentApprovals
            .AnyAsync(approval => approval.AgentJobId == job.Id && approval.Status == AgentApprovalStatus.Pending, cancellationToken);
        if (pendingApprovalExists)
        {
            job.Status = AgentJobStatus.NeedsApproval;
            AddEvent(job.Id, job.UserId, AgentEventType.ApprovalRequested, "Dry-run write approvals are waiting for user confirmation.");
        }
        else
        {
            CompleteJob(job);
        }

        job.UpdatedAtUtc = DateTime.UtcNow;
    }

    private async Task RunSubagentsAsync(AgentJob job, CancellationToken cancellationToken)
    {
        var step = AddStep(job.Id, "Run subagents", "MainAgent", 20);
        job.CurrentSubAgent = "MainAgent";
        step.Status = AgentStepStatus.Running;
        step.StartedAtUtc = DateTime.UtcNow;
        AddEvent(job.Id, job.UserId, AgentEventType.StepStarted, "Main Agent started subagent orchestration.");

        var subAgents = InferSubAgents(job.Goal);
        foreach (var subAgent in subAgents)
        {
            dbContext.AgentSubJobs.Add(new AgentSubJob
            {
                Id = Guid.NewGuid(),
                AgentJobId = job.Id,
                SubAgentName = subAgent,
                Goal = job.Goal,
                Status = AgentSubJobStatus.Completed,
                StartedAtUtc = DateTime.UtcNow,
                CompletedAtUtc = DateTime.UtcNow,
                ResultJson = JsonSerializer.Serialize(new { subAgent, result = "Completed deterministic MVP pass." }, JsonOptions)
            });
            job.CurrentSubAgent = subAgent;
        }

        if (subAgents.Contains("SummarySubAgent"))
        {
            await AddSummaryArtifactAsync(job, step.Id);
        }

        if (subAgents.Contains("CodeSubAgent"))
        {
            AddArtifact(job.Id, step.Id, AgentArtifactKind.CodePatch, "code-subagent.patch.md", BuildCodePatchArtifact(job.Goal));
        }

        if (subAgents.Contains("DeckSubAgent"))
        {
            AddArtifact(job.Id, step.Id, AgentArtifactKind.Deck, "deck-subagent-outline.md", BuildDeckArtifact(job.Goal));
        }

        if (subAgents.Contains("ReportSubAgent"))
        {
            AddArtifact(job.Id, step.Id, AgentArtifactKind.Report, "report-subagent-draft.md", BuildReportArtifact(job.Goal));
        }

        if (subAgents.Contains("TaskFlowActionSubAgent"))
        {
            await AddTaskFlowActionApprovalsAsync(job, step.Id, cancellationToken);
        }

        step.Status = AgentStepStatus.Completed;
        step.CompletedAtUtc = DateTime.UtcNow;
        step.OutputJson = JsonSerializer.Serialize(new { subAgents }, JsonOptions);
        AddEvent(job.Id, job.UserId, AgentEventType.StepCompleted, "Subagent orchestration completed.");
    }

    private async Task ExecuteApprovedActionsAsync(AgentJob job, CancellationToken cancellationToken)
    {
        var approvals = await dbContext.AgentApprovals
            .Where(approval =>
                approval.AgentJobId == job.Id
                && approval.Status == AgentApprovalStatus.Approved
                && approval.ExecutedAtUtc == null
                && approval.ActionName != null)
            .OrderBy(approval => approval.DecidedAtUtc)
            .ToListAsync(cancellationToken);

        foreach (var approval in approvals)
        {
            if (approval.ActionName == "CreateTask")
            {
                await ExecuteCreateTaskAsync(job, approval, cancellationToken);
            }
            else if (approval.ActionName == "CreateReminder")
            {
                await ExecuteCreateReminderAsync(job, approval, cancellationToken);
            }
        }
    }

    private async Task ExecuteCreateTaskAsync(AgentJob job, AgentApproval approval, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<CreateTaskActionPayload>(approval.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException("CreateTask payload is invalid.");

        if (!await permissionService.CanCreateTask(job.UserId, payload.ProjectId))
        {
            throw new InvalidOperationException("Task creation permission was denied during approval execution.");
        }

        var result = await taskService.CreateTaskAsync(payload.ProjectId, new CreateTaskRequest
        {
            Title = payload.Title,
            Description = payload.Description,
            Priority = payload.Priority,
            AssigneeId = payload.AssigneeId,
            DeadlineUtc = payload.DeadlineUtc
        });

        if (!result.Success || result.Data is null)
        {
            throw new InvalidOperationException(result.Message);
        }

        approval.TargetEntityId = result.Data.Id;
        approval.ExecutedAtUtc = DateTime.UtcNow;
        approval.ExecutionResultJson = JsonSerializer.Serialize(result.Data, JsonOptions);
        AddActivity(job, "Agent.CreateTask", "TaskItem", result.Data.Id, $"Agent job {job.Id} created task after user approval.");
        AddEvent(job.Id, job.UserId, AgentEventType.ActionExecuted, $"Task created: {result.Data.Title}");
    }

    private async Task ExecuteCreateReminderAsync(AgentJob job, AgentApproval approval, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<CreateReminderActionPayload>(approval.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException("CreateReminder payload is invalid.");

        if (!await permissionService.CanAccessWorkspace(job.UserId, payload.WorkspaceId))
        {
            throw new InvalidOperationException("Reminder creation permission was denied during approval execution.");
        }

        var result = await notificationService.CreateNotificationAsync(new CreateNotificationRequest
        {
            UserId = job.UserId,
            WorkspaceId = payload.WorkspaceId,
            Title = payload.Title,
            Message = payload.Message,
            Type = NotificationType.Reminder
        });

        if (!result.Success || result.Data is null)
        {
            throw new InvalidOperationException(result.Message);
        }

        approval.TargetEntityId = result.Data.Id;
        approval.ExecutedAtUtc = DateTime.UtcNow;
        approval.ExecutionResultJson = JsonSerializer.Serialize(result.Data, JsonOptions);
        AddActivity(job, "Agent.CreateReminder", "Notification", result.Data.Id, $"Agent job {job.Id} created reminder after user approval.");
        AddEvent(job.Id, job.UserId, AgentEventType.ActionExecuted, $"Reminder created: {result.Data.Title}");
    }

    private async Task AddTaskFlowActionApprovalsAsync(AgentJob job, Guid stepId, CancellationToken cancellationToken)
    {
        if (ContainsAny(job.Goal, "task", "任务", "todo", "to-do"))
        {
            var project = await dbContext.Projects
                .AsNoTracking()
                .Where(item => item.WorkspaceId == job.WorkspaceId)
                .OrderByDescending(item => item.UpdatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (project is null)
            {
                AddArtifact(job.Id, stepId, AgentArtifactKind.TaskFlowAction, "task-action-skipped.md", "No project exists in this workspace, so TaskFlowActionSubAgent cannot preview a task creation.");
            }
            else
            {
                var payload = new CreateTaskActionPayload(
                    project.Id,
                    "AI follow-up: " + Shorten(job.Goal, 120),
                    "Created from an approved TaskFlow Agent dry-run preview.",
                    TaskPriority.Medium,
                    null,
                    null);
                AddApproval(job, stepId, "CreateTask", "Create task dry-run", "TaskItem", payload);
            }
        }

        if (ContainsAny(job.Goal, "reminder", "提醒", "notify", "notification"))
        {
            var payload = new CreateReminderActionPayload(
                job.WorkspaceId,
                "AI reminder",
                Shorten(job.Goal, 300));
            AddApproval(job, stepId, "CreateReminder", "Create reminder dry-run", "Notification", payload);
        }
    }

    private async Task AddSummaryArtifactAsync(AgentJob job, Guid stepId)
    {
        var projects = await dbContext.Projects
            .AsNoTracking()
            .Where(project => project.WorkspaceId == job.WorkspaceId)
            .OrderBy(project => project.Name)
            .Take(5)
            .ToListAsync();

        var lines = new List<string>
        {
            "# TaskFlow Summary",
            "",
            $"Goal: {job.Goal}",
            "",
            $"Workspace projects considered: {projects.Count}"
        };

        foreach (var project in projects)
        {
            var dashboard = await dashboardService.GetProjectDashboardAsync(project.Id);
            lines.Add(dashboard.Success && dashboard.Data is not null
                ? $"- {project.Name}: {dashboard.Data.CompletedTaskCount}/{dashboard.Data.TaskCount} tasks completed, {dashboard.Data.OverdueTaskCount} overdue."
                : $"- {project.Name}: dashboard unavailable.");
        }

        AddArtifact(job.Id, stepId, AgentArtifactKind.Summary, "summary-subagent.md", string.Join(Environment.NewLine, lines));
    }

    private async Task<string?> TryAskProviderForPlanNotesAsync(AgentJob job, IReadOnlyCollection<string> subAgents, CancellationToken cancellationToken)
    {
        if (!job.ProviderCredentialId.HasValue)
        {
            var workspaceProviderResult = await aiProviderService.ResolveWorkspaceProviderAsync(job.WorkspaceId, cancellationToken);
            if (!workspaceProviderResult.Success || workspaceProviderResult.Data is null)
            {
                throw new InvalidOperationException(workspaceProviderResult.Message);
            }

            return await RequestPlanNotesAsync(workspaceProviderResult.Data, job, subAgents, cancellationToken);
        }

        var credential = await dbContext.AiProviderCredentials
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == job.ProviderCredentialId.Value && item.UserId == job.UserId, cancellationToken);
        if (credential is null)
        {
            throw new InvalidOperationException("Configured provider was not found for this user.");
        }

        var runtime = new AiProviderRuntime
        {
            ProviderName = credential.ProviderName,
            BaseUrl = string.IsNullOrWhiteSpace(credential.BaseUrl)
                ? "https://api.openai.com/v1"
                : credential.BaseUrl.TrimEnd('/'),
            Model = credential.Model,
            ApiKey = AiProviderService.Unprotect(dataProtectionProvider, credential.EncryptedApiKey),
            SupportsToolCalls = credential.SupportsToolCalls
        };
        return await RequestPlanNotesAsync(runtime, job, subAgents, cancellationToken);
    }

    private async Task<string?> RequestPlanNotesAsync(
        AiProviderRuntime provider,
        AgentJob job,
        IReadOnlyCollection<string> subAgents,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{provider.BaseUrl.TrimEnd('/')}/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            model = provider.Model,
            messages = new[]
            {
                new { role = "system", content = "You are planning a safe TaskFlow Agent run. Return concise implementation notes only. Do not request direct database access." },
                new { role = "user", content = $"Goal: {job.Goal}\nSubagents: {string.Join(", ", subAgents)}\nSupportsToolCalls: {provider.SupportsToolCalls}" }
            }
        }, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Provider planning call failed with {(int)response.StatusCode}: {Shorten(RedactSecret(body, provider.ApiKey), 400)}");
        }

        using var document = JsonDocument.Parse(body);
        var content = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("Provider returned an empty planning note.");
        }

        return content;
    }

    private AgentStep AddStep(Guid jobId, string name, string subAgentName, int sequence)
    {
        var step = new AgentStep
        {
            Id = Guid.NewGuid(),
            AgentJobId = jobId,
            Name = name,
            SubAgentName = subAgentName,
            Sequence = sequence
        };
        dbContext.AgentSteps.Add(step);
        return step;
    }

    private void AddApproval<TPayload>(AgentJob job, Guid stepId, string actionName, string title, string targetEntityType, TPayload payload)
    {
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        dbContext.AgentApprovals.Add(new AgentApproval
        {
            Id = Guid.NewGuid(),
            AgentJobId = job.Id,
            AgentStepId = stepId,
            ApprovalType = "TaskFlowWrite",
            Title = title,
            ActionName = actionName,
            PreviewJson = JsonSerializer.Serialize(new
            {
                actionName,
                targetEntityType,
                dryRun = true,
                payload
            }, JsonOptions),
            PayloadJson = payloadJson,
            TargetEntityType = targetEntityType,
            RequestedByUserId = job.UserId
        });
    }

    private void AddArtifact(Guid jobId, Guid stepId, AgentArtifactKind kind, string name, string content)
    {
        dbContext.AgentArtifacts.Add(new AgentArtifact
        {
            Id = Guid.NewGuid(),
            AgentJobId = jobId,
            AgentStepId = stepId,
            Kind = kind,
            Name = name,
            ContentType = "text/markdown",
            Content = content
        });
        AddEvent(jobId, null, AgentEventType.ArtifactCreated, $"Artifact created: {name}");
    }

    private void AddActivity(AgentJob job, string action, string entityType, Guid entityId, string details)
    {
        dbContext.ActivityLogs.Add(new ActivityLog
        {
            Id = Guid.NewGuid(),
            WorkspaceId = job.WorkspaceId,
            UserId = job.UserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details
        });
    }

    private void AddEvent(Guid jobId, Guid? actorUserId, AgentEventType eventType, string message, string? dataJson = null)
    {
        dbContext.AgentEvents.Add(new AgentEvent
        {
            Id = Guid.NewGuid(),
            AgentJobId = jobId,
            ActorUserId = actorUserId,
            EventType = eventType,
            Message = message,
            DataJson = dataJson
        });
    }

    private static IReadOnlyCollection<string> InferSubAgents(string goal)
    {
        var subAgents = new List<string> { "SummarySubAgent" };

        if (ContainsAny(goal, "code", "代码", "implement", "bug", "fix"))
        {
            subAgents.Add("CodeSubAgent");
        }

        if (ContainsAny(goal, "ppt", "deck", "slides", "presentation", "幻灯片"))
        {
            subAgents.Add("DeckSubAgent");
        }

        if (ContainsAny(goal, "report", "报告", "document", "文档"))
        {
            subAgents.Add("ReportSubAgent");
        }

        if (ContainsAny(goal, "task", "任务", "reminder", "提醒", "notify", "notification"))
        {
            subAgents.Add("TaskFlowActionSubAgent");
        }

        return subAgents.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private void CompleteJob(AgentJob job)
    {
        job.Status = AgentJobStatus.Completed;
        job.CurrentSubAgent = null;
        job.CompletedAtUtc = DateTime.UtcNow;
        job.UpdatedAtUtc = DateTime.UtcNow;
        AddEvent(job.Id, job.UserId, AgentEventType.StatusChanged, "Agent job completed.");
    }

    private static bool ContainsAny(string text, params string[] needles)
    {
        return needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static string Shorten(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength].TrimEnd() + "...";
    }

    private static string RedactSecret(string value, string secret)
    {
        return string.IsNullOrWhiteSpace(secret)
            ? value
            : value.Replace(secret, "[redacted]", StringComparison.Ordinal);
    }

    private static string BuildCodePatchArtifact(string goal)
    {
        return $"""
            # CodeSubAgent Patch Plan

            Goal: {goal}

            This MVP does not mutate repository files from the Worker. It prepares a human-reviewable patch plan artifact so code changes can be reviewed before application.
            """;
    }

    private static string BuildDeckArtifact(string goal)
    {
        return $"""
            # DeckSubAgent Outline

            1. Problem and current TaskFlow context
            2. Proposed AI Agent workflow
            3. Dry-run approval and audit trail
            4. Expected outcome

            Goal: {goal}
            """;
    }

    private static string BuildReportArtifact(string goal)
    {
        return $"""
            # ReportSubAgent Draft

            The TaskFlow Agent processed the requested goal with a plan-first workflow, explicit approval gates, and audit logging for write actions.

            Goal: {goal}
            """;
    }

    private sealed record AgentPlan(
        string Summary,
        IReadOnlyCollection<string> SubAgents,
        string? ProviderNotes,
        IReadOnlyCollection<string> SafetyRules);

    private sealed record CreateTaskActionPayload(
        Guid ProjectId,
        string Title,
        string? Description,
        TaskPriority Priority,
        Guid? AssigneeId,
        DateTime? DeadlineUtc);

    private sealed record CreateReminderActionPayload(
        Guid WorkspaceId,
        string Title,
        string Message);
}
