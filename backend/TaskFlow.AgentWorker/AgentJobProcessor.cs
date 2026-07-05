using System.Net.Http.Headers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.DTOs.GitHub;
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
    IAiContextService aiContextService,
    IGitHubRepositoryService gitHubRepositoryService,
    IDataProtectionProvider dataProtectionProvider,
    IHttpClientFactory httpClientFactory,
    AgentSkillRegistry skillRegistry,
    ILogger<AgentJobProcessor> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(AgentJsonUtilities.DefaultOptions)
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
        var step = AddStep(job.Id, "Build execution plan", "ManagerAgent", 10);
        job.CurrentSubAgent = "ManagerAgent";
        step.Status = AgentStepStatus.Running;
        step.StartedAtUtc = DateTime.UtcNow;
        AddEvent(job.Id, job.UserId, AgentEventType.Planning, "Manager Agent is building the execution plan.");

        var subAgents = InferSubAgents(job.Goal, job.ArtifactTarget);
        var toolIntents = BuildToolIntents(job, subAgents);
        var providerNotes = await TryAskProviderForPlanNotesAsync(job, subAgents, cancellationToken);
        var plan = new AgentPlan(
            "Microsoft Agent Framework manager workflow will plan typed tool intents, then TaskFlow services execute them with approval gates.",
            "ManagerAgent",
            "Microsoft.Agents.AI",
            subAgents,
            toolIntents,
            skillRegistry.RegisteredSkillIds,
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
            .AnyAsync(step => step.AgentJobId == job.Id && step.Name == "Run manager workflow", cancellationToken);
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
        var step = AddStep(job.Id, "Run manager workflow", "ManagerAgent", 20);
        job.CurrentSubAgent = "ManagerAgent";
        step.Status = AgentStepStatus.Running;
        step.StartedAtUtc = DateTime.UtcNow;
        AddEvent(job.Id, job.UserId, AgentEventType.StepStarted, "Manager Agent started typed tool orchestration.");

        var subAgents = InferSubAgents(job.Goal, job.ArtifactTarget);
        var toolIntents = BuildToolIntents(job, subAgents);
        var agentContext = await LoadAgentContextAsync(job, cancellationToken);
        step.InputJson = JsonSerializer.Serialize(new
        {
            job.Goal,
            job.ArtifactTarget,
            subAgents,
            toolIntents,
            contextSources = agentContext.Sources,
            registeredSkills = skillRegistry.RegisteredSkillIds
        }, JsonOptions);
        AddEvent(
            job.Id,
            job.UserId,
            AgentEventType.StepStarted,
            "Manager Agent loaded approved context sources.",
            JsonSerializer.Serialize(new { sources = agentContext.Sources, lineCount = agentContext.ContextLines.Count }, JsonOptions));

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
                ResultJson = JsonSerializer.Serialize(new
                {
                    subAgent,
                    result = "Completed manager tool intent pass.",
                    toolIntents = toolIntents.Where(intent => intent.SubAgentName == subAgent).ToArray(),
                    contextSources = agentContext.Sources
                }, JsonOptions)
            });
            job.CurrentSubAgent = subAgent;
        }

        if (subAgents.Contains("SummarySubAgent"))
        {
            await AddSummaryArtifactAsync(job, step.Id, agentContext.ContextLines);
        }

        if (subAgents.Contains("CodeSubAgent"))
        {
            var codePatchResult = await BuildCodePatchAsync(job, step.Id, agentContext.ContextLines, cancellationToken);
            var patchArtifact = AddArtifact(
                job.Id,
                step.Id,
                AgentArtifactKind.CodePatch,
                "code-subagent.patch",
                "text/x-patch",
                codePatchResult.Patch,
                storeAsBlob: true);

            if (!string.IsNullOrWhiteSpace(codePatchResult.ValidationResult))
            {
                AddArtifact(
                    job.Id,
                    step.Id,
                    AgentArtifactKind.CodePatch,
                    "code-validation-result.md",
                    $"""
                    # Code Validation Result

                    {codePatchResult.ValidationResult}

                    ## Diff Summary

                    {codePatchResult.DiffSummary}
                    """);
            }

            if (job.GitHubRepositoryConnectionId.HasValue)
            {
                await AddCreatePullRequestApprovalAsync(job, step.Id, patchArtifact.Id, cancellationToken);
            }
        }

        if (subAgents.Contains("DeckSubAgent"))
        {
            AddArtifact(job.Id, step.Id, AgentArtifactKind.Deck, "deck-subagent-source.md", skillRegistry.BuildMarkdown("TaskFlow AI Deck", job.Goal, agentContext.ContextLines));
            try
            {
                var deck = await skillRegistry.BuildDeckAsync(job.Goal, agentContext.ContextLines, cancellationToken);
                AddArtifact(job.Id, step.Id, AgentArtifactKind.Deck, deck.FileName, deck.ContentType, deck.Content);
                AddEvent(
                    job.Id,
                    job.UserId,
                    AgentEventType.ArtifactCreated,
                    $"Runtime skill completed: {deck.SkillId}",
                    JsonSerializer.Serialize(new { deck.SkillId, deck.FileName, output = deck.OutputLog }, JsonOptions));
            }
            catch (Exception ex)
            {
                AddSkillFailure(job, step.Id, AgentArtifactKind.Deck, "ppt-master", ex);
                throw;
            }
        }

        if (subAgents.Contains("ReportSubAgent"))
        {
            AddArtifact(job.Id, step.Id, AgentArtifactKind.Report, "report-subagent-draft.md", skillRegistry.BuildMarkdown("TaskFlow AI Report", job.Goal, agentContext.ContextLines));
            try
            {
                var report = await skillRegistry.BuildDocxAsync(job.Goal, agentContext.ContextLines, cancellationToken);
                AddArtifact(job.Id, step.Id, AgentArtifactKind.Report, report.FileName, report.ContentType, report.Content);
                AddEvent(
                    job.Id,
                    job.UserId,
                    AgentEventType.ArtifactCreated,
                    $"Runtime skill completed: {report.SkillId}",
                    JsonSerializer.Serialize(new { report.SkillId, report.FileName, output = report.OutputLog }, JsonOptions));
            }
            catch (Exception ex)
            {
                AddSkillFailure(job, step.Id, AgentArtifactKind.Report, "docx", ex);
                throw;
            }
        }

        if (subAgents.Contains("TaskFlowActionSubAgent"))
        {
            await AddTaskFlowActionApprovalsAsync(job, step.Id, cancellationToken);
        }

        step.Status = AgentStepStatus.Completed;
        step.CompletedAtUtc = DateTime.UtcNow;
        step.OutputJson = JsonSerializer.Serialize(new
        {
            subAgents,
            toolIntents,
            contextSources = agentContext.Sources,
            registeredSkills = skillRegistry.RegisteredSkillIds
        }, JsonOptions);
        AddEvent(job.Id, job.UserId, AgentEventType.StepCompleted, "Manager Agent workflow completed.");
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
            else if (approval.ActionName == "CreatePullRequest")
            {
                await ExecuteCreatePullRequestAsync(job, approval, cancellationToken);
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

    private async Task ExecuteCreatePullRequestAsync(AgentJob job, AgentApproval approval, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<CreatePullRequestActionPayload>(approval.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException("CreatePullRequest payload is invalid.");

        var repository = await dbContext.GitHubRepositoryConnections
            .FirstOrDefaultAsync(item =>
                item.Id == payload.RepositoryId
                && item.WorkspaceId == job.WorkspaceId
                && item.IsEnabled,
                cancellationToken)
            ?? throw new InvalidOperationException("GitHub repository connection was not found or is disabled.");

        if (!await permissionService.CanAccessWorkspace(job.UserId, job.WorkspaceId))
        {
            throw new InvalidOperationException("Workspace access was denied during GitHub PR approval execution.");
        }

        var patchArtifact = await dbContext.AgentArtifacts
            .Include(item => item.Blob)
            .FirstOrDefaultAsync(item => item.Id == payload.PatchArtifactId && item.AgentJobId == job.Id, cancellationToken)
            ?? throw new InvalidOperationException("Patch artifact was not found.");
        var patchContent = patchArtifact.Content
            ?? (patchArtifact.Blob is null ? null : Encoding.UTF8.GetString(patchArtifact.Blob.Content));
        if (string.IsNullOrWhiteSpace(patchContent))
        {
            throw new InvalidOperationException("Patch artifact has no content.");
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "taskflow-agent", job.Id.ToString("N"));
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }

        Directory.CreateDirectory(tempRoot);
        var repoPath = Path.Combine(tempRoot, "repo");
        var patchPath = Path.Combine(tempRoot, "agent.patch");
        await File.WriteAllTextAsync(patchPath, patchContent, Encoding.UTF8, cancellationToken);

        string validationResult;
        string diffSummary;
        GitHubPullRequestResult pullRequest;

        try
        {
            var token = await gitHubRepositoryService.CreateInstallationAccessTokenAsync(repository.InstallationId, cancellationToken);
            var gitEnvironment = CreateGitAuthEnvironment(tempRoot, token.Token);
            var remoteUrl = $"https://github.com/{repository.FullName}.git";
            await RunProcessAsync("git", ["clone", "--depth", "1", "--branch", repository.DefaultBranch, remoteUrl, repoPath], tempRoot, token.Token, cancellationToken, gitEnvironment);
            await RunProcessAsync("git", ["checkout", "-b", payload.HeadBranch], repoPath, token.Token, cancellationToken, gitEnvironment);
            await RunProcessAsync("git", ["apply", "--check", patchPath], repoPath, token.Token, cancellationToken, gitEnvironment);
            await RunProcessAsync("git", ["apply", patchPath], repoPath, token.Token, cancellationToken, gitEnvironment);

            diffSummary = await RunProcessAsync("git", ["diff", "--stat", "HEAD"], repoPath, token.Token, cancellationToken, gitEnvironment);
            if (string.IsNullOrWhiteSpace(diffSummary))
            {
                throw new InvalidOperationException("Patch applied but produced no repository diff.");
            }

            if (string.IsNullOrWhiteSpace(repository.ValidationCommand))
            {
                validationResult = "Validation skipped: no validation command configured for this repository.";
            }
            else
            {
                validationResult = await RunProcessAsync("/bin/bash", ["-lc", repository.ValidationCommand], repoPath, token.Token, cancellationToken, gitEnvironment);
            }

            await RunProcessAsync("git", ["config", "user.email", "taskflow-ai@users.noreply.github.com"], repoPath, token.Token, cancellationToken, gitEnvironment);
            await RunProcessAsync("git", ["config", "user.name", "TaskFlow AI"], repoPath, token.Token, cancellationToken, gitEnvironment);
            await RunProcessAsync("git", ["add", "."], repoPath, token.Token, cancellationToken, gitEnvironment);
            await RunProcessAsync("git", ["commit", "-m", payload.CommitTitle], repoPath, token.Token, cancellationToken, gitEnvironment);
            await RunProcessAsync("git", ["push", remoteUrl, $"HEAD:{payload.HeadBranch}"], repoPath, token.Token, cancellationToken, gitEnvironment);

            pullRequest = await gitHubRepositoryService.CreatePullRequestAsync(repository, payload.HeadBranch, payload.PullRequestTitle, payload.PullRequestBody, cancellationToken);
        }
        catch (Exception ex)
        {
            AddArtifact(job.Id, approval.AgentStepId, AgentArtifactKind.CodePatch, "github-pr-error.md", $"GitHub PR creation failed.\n\n{ex.Message}");
            throw;
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        AddArtifact(
            job.Id,
            approval.AgentStepId,
            AgentArtifactKind.CodePatch,
            "github-pr-result.md",
            $"""
            # GitHub PR Result

            Repository: {repository.FullName}
            Branch: {payload.HeadBranch}
            Pull request: {pullRequest.Url}

            ## Diff summary

            {diffSummary}

            ## Validation

            {validationResult}
            """);

        approval.TargetEntityId = repository.Id;
        approval.ExecutedAtUtc = DateTime.UtcNow;
        approval.ExecutionResultJson = JsonSerializer.Serialize(new
        {
            repository = repository.FullName,
            branch = payload.HeadBranch,
            pullRequest.Number,
            pullRequest.Url,
            validationResult,
            diffSummary
        }, JsonOptions);

        AddActivity(job, "Agent.CreatePullRequest", "GitHubRepositoryConnection", repository.Id, $"Agent job {job.Id} created GitHub PR {pullRequest.Url} after user approval.");
        AddEvent(job.Id, job.UserId, AgentEventType.ActionExecuted, $"GitHub pull request created: {pullRequest.Url}");
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

    private async Task AddCreatePullRequestApprovalAsync(AgentJob job, Guid stepId, Guid patchArtifactId, CancellationToken cancellationToken)
    {
        var repository = await dbContext.GitHubRepositoryConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(item =>
                item.Id == job.GitHubRepositoryConnectionId
                && item.WorkspaceId == job.WorkspaceId
                && item.IsEnabled,
                cancellationToken);
        if (repository is null)
        {
            AddArtifact(job.Id, stepId, AgentArtifactKind.CodePatch, "github-pr-skipped.md", "GitHub repository connection was not found or is disabled.");
            return;
        }

        var headBranch = $"taskflow-ai/{job.Id:N}";
        var payload = new CreatePullRequestActionPayload(
            repository.Id,
            patchArtifactId,
            repository.DefaultBranch,
            headBranch,
            $"TaskFlow AI: {Shorten(job.Goal, 80)}",
            $"TaskFlow AI update for job {job.Id}",
            $"""
            Generated by TaskFlow AI.

            Job: {job.Id}
            Goal: {job.Goal}
            Sources: workspace {job.WorkspaceId}{(job.ChannelId.HasValue ? $", channel {job.ChannelId}" : string.Empty)}{(job.AttachmentId.HasValue ? $", attachment {job.AttachmentId}" : string.Empty)}

            Validation: {(string.IsNullOrWhiteSpace(repository.ValidationCommand) ? "skipped, no validation command configured" : repository.ValidationCommand)}
            """);

        AddApproval(job, stepId, "CreatePullRequest", "Create GitHub pull request", "GitHubRepositoryConnection", payload, approvalType: "GitHubWrite");
    }

    private async Task AddSummaryArtifactAsync(AgentJob job, Guid stepId, IReadOnlyCollection<string> contextLines)
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
            $"Workspace projects considered: {projects.Count}",
            "",
            "## Context sources"
        };

        lines.AddRange(contextLines.Count == 0
            ? ["- No indexed channel or attachment context was provided."]
            : contextLines.Take(12).Select(context => $"- {context}"));
        lines.Add("");
        lines.Add("## Project snapshot");

        foreach (var project in projects)
        {
            var dashboard = await dashboardService.GetProjectDashboardAsync(project.Id);
            lines.Add(dashboard.Success && dashboard.Data is not null
                ? $"- {project.Name}: {dashboard.Data.CompletedTaskCount}/{dashboard.Data.TaskCount} tasks completed, {dashboard.Data.OverdueTaskCount} overdue."
                : $"- {project.Name}: dashboard unavailable.");
        }

        AddArtifact(job.Id, stepId, AgentArtifactKind.Summary, "summary-subagent.md", string.Join(Environment.NewLine, lines));
    }

    private async Task<AgentRuntimeContext> LoadAgentContextAsync(AgentJob job, CancellationToken cancellationToken)
    {
        if (job.ChannelId.HasValue)
        {
            var contextResult = await aiContextService.BuildChannelContextAsync(
                job.ChannelId.Value,
                job.Goal,
                job.AttachmentId,
                cancellationToken);
            if (!contextResult.Success || contextResult.Data is null)
            {
                throw new InvalidOperationException(contextResult.Message);
            }

            return new AgentRuntimeContext(contextResult.Data.ContextLines, contextResult.Data.Sources);
        }

        var ragContext = await LoadRagContextAsync(job, cancellationToken);
        return new AgentRuntimeContext(
            ragContext,
            ragContext.Count == 0 ? [] : ["Indexed workspace context"]);
    }

    private async Task<IReadOnlyCollection<string>> LoadRagContextAsync(AgentJob job, CancellationToken cancellationToken)
    {
        var query = dbContext.ChannelKnowledgeChunks
            .AsNoTracking()
            .Where(chunk => chunk.WorkspaceId == job.WorkspaceId);

        if (job.AttachmentId.HasValue)
        {
            query = query.Where(chunk => chunk.AttachmentId == job.AttachmentId.Value);
        }
        else if (job.ChannelId.HasValue)
        {
            query = query.Where(chunk => chunk.ChannelId == job.ChannelId.Value);
        }
        else
        {
            return [];
        }

        return await query
            .OrderBy(chunk => chunk.CreatedAtUtc)
            .Take(8)
            .Select(chunk => $"{chunk.SourceLabel}: {Shorten(chunk.Content, 800)}")
            .ToListAsync(cancellationToken);
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

    private void AddApproval<TPayload>(
        AgentJob job,
        Guid stepId,
        string actionName,
        string title,
        string targetEntityType,
        TPayload payload,
        string approvalType = "TaskFlowWrite")
    {
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        dbContext.AgentApprovals.Add(new AgentApproval
        {
            Id = Guid.NewGuid(),
            AgentJobId = job.Id,
            AgentStepId = stepId,
            ApprovalType = approvalType,
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

    private AgentArtifact AddArtifact(Guid jobId, Guid? stepId, AgentArtifactKind kind, string name, string content)
    {
        return AddArtifact(jobId, stepId, kind, name, "text/markdown", content, storeAsBlob: false);
    }

    private AgentArtifact AddArtifact(Guid jobId, Guid? stepId, AgentArtifactKind kind, string name, string content, bool storeAsBlob)
    {
        return AddArtifact(jobId, stepId, kind, name, "text/markdown", content, storeAsBlob);
    }

    private AgentArtifact AddArtifact(Guid jobId, Guid? stepId, AgentArtifactKind kind, string name, string contentType, string content, bool storeAsBlob)
    {
        var artifact = new AgentArtifact
        {
            Id = Guid.NewGuid(),
            AgentJobId = jobId,
            AgentStepId = stepId,
            Kind = kind,
            Name = name,
            ContentType = contentType,
            Content = content,
            SizeBytes = Encoding.UTF8.GetByteCount(content)
        };
        dbContext.AgentArtifacts.Add(artifact);
        if (storeAsBlob)
        {
            dbContext.AgentArtifactBlobs.Add(new AgentArtifactBlob
            {
                AgentArtifactId = artifact.Id,
                Content = Encoding.UTF8.GetBytes(content)
            });
        }

        AddEvent(jobId, null, AgentEventType.ArtifactCreated, $"Artifact created: {name}");
        return artifact;
    }

    private AgentArtifact AddArtifact(Guid jobId, Guid? stepId, AgentArtifactKind kind, string name, string contentType, byte[] content)
    {
        var artifact = new AgentArtifact
        {
            Id = Guid.NewGuid(),
            AgentJobId = jobId,
            AgentStepId = stepId,
            Kind = kind,
            Name = name,
            ContentType = contentType,
            SizeBytes = content.LongLength
        };
        dbContext.AgentArtifacts.Add(artifact);
        dbContext.AgentArtifactBlobs.Add(new AgentArtifactBlob
        {
            AgentArtifactId = artifact.Id,
            Content = content
        });
        AddEvent(jobId, null, AgentEventType.ArtifactCreated, $"Artifact created: {name}");
        return artifact;
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

    private static IReadOnlyCollection<string> InferSubAgents(string goal, string? artifactTarget)
    {
        var subAgents = new List<string> { "SummarySubAgent" };

        if (ContainsAny(goal, "code", "代码", "implement", "bug", "fix")
            || artifactTarget is "patch" or "pull-request")
        {
            subAgents.Add("CodeSubAgent");
        }

        if (ContainsAny(goal, "ppt", "deck", "slides", "presentation", "幻灯片")
            || artifactTarget == "pptx")
        {
            subAgents.Add("DeckSubAgent");
        }

        if (ContainsAny(goal, "report", "报告", "document", "文档")
            || artifactTarget == "docx")
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

    private static string BuildCodePatchArtifact(AgentJob job, IReadOnlyCollection<string> ragContext)
    {
        var path = $".taskflow-ai/taskflow-ai-{job.Id:N}.md";
        var fileContent = new List<string>
        {
            "# TaskFlow AI Code Request",
            "",
            $"Job: {job.Id}",
            $"Workspace: {job.WorkspaceId}",
            $"Goal: {job.Goal}",
            "",
            "## Context",
        };

        if (ragContext.Count == 0)
        {
            fileContent.Add("No indexed channel or attachment context was provided to this job.");
        }
        else
        {
            fileContent.AddRange(ragContext.Select(context => $"- {context.Replace("\n", " ", StringComparison.Ordinal)}"));
        }

        fileContent.AddRange([
            "",
            "## Review Notes",
            "This patch is generated as a reviewable artifact and must be applied through the TaskFlow approval workflow before any GitHub PR is created."
        ]);

        var patch = new StringBuilder();
        patch.AppendLine($"diff --git a/{path} b/{path}");
        patch.AppendLine("new file mode 100644");
        patch.AppendLine("index 0000000..1111111");
        patch.AppendLine("--- /dev/null");
        patch.AppendLine($"+++ b/{path}");
        patch.AppendLine($"@@ -0,0 +1,{fileContent.Count} @@");
        foreach (var line in fileContent)
        {
            patch.Append('+').AppendLine(line);
        }

        return patch.ToString();
    }

    private static string BuildDeckArtifact(string goal, IReadOnlyCollection<string> ragContext)
    {
        var slides = BuildDeckSlides(goal, ragContext);
        var lines = new List<string> { "# DeckSubAgent Outline", "" };
        var index = 1;
        foreach (var slide in slides)
        {
            lines.Add($"{index}. {slide.Title}");
            lines.Add($"   {slide.Body.Replace("\n", " ", StringComparison.Ordinal)}");
            index++;
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildReportArtifact(string goal, IReadOnlyCollection<string> ragContext)
    {
        return string.Join(Environment.NewLine, new[]
        {
            "# ReportSubAgent Draft",
            "",
            $"Goal: {goal}",
            "",
            "The TaskFlow Agent processed the requested goal with a plan-first workflow, explicit approval gates, and audit logging for write actions.",
            "",
            "Context used:",
            ragContext.Count == 0 ? "- No indexed attachment or channel context was provided." : string.Join(Environment.NewLine, ragContext.Select(item => $"- {item}"))
        });
    }

    private static IReadOnlyCollection<string> BuildReportSections(string goal, IReadOnlyCollection<string> ragContext)
    {
        var sections = new List<string>
        {
            "Executive Summary",
            "This report was generated from the TaskFlow Agent workflow. It uses indexed channel or attachment context when a channel or attachment was selected.",
            "Requested Outcome",
            goal,
            "Source Context"
        };

        if (ragContext.Count == 0)
        {
            sections.Add("No indexed attachment or channel context was provided.");
        }
        else
        {
            sections.AddRange(ragContext);
        }
        sections.AddRange([
            "Recommended Next Steps",
            "Review the generated artifact, approve any write actions explicitly, and keep GitHub changes in a pull request for human review."
        ]);
        return sections;
    }

    private static IReadOnlyCollection<(string Title, string Body)> BuildDeckSlides(string goal, IReadOnlyCollection<string> ragContext)
    {
        return [
            ("Objective", goal),
            ("Source Context", ragContext.Count == 0 ? "No indexed attachment or channel context was provided." : string.Join("\n", ragContext.Take(3))),
            ("Agent Workflow", "Read context\nGenerate artifacts\nRequest approval before external writes\nRecord activity history"),
            ("Next Steps", "Download the artifacts\nReview pending approvals\nCreate a GitHub PR only after approval")
        ];
    }

    private static async Task<string> RunProcessAsync(
        string fileName,
        IReadOnlyCollection<string> arguments,
        string workingDirectory,
        string secret,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach (var item in environment)
            {
                startInfo.Environment[item.Key] = item.Value;
            }
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to start process: {fileName}");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = RedactSecret(await outputTask, secret);
        var error = RedactSecret(await errorTask, secret);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} failed with exit code {process.ExitCode}.\n{Shorten(output + error, 2000)}");
        }

        return string.IsNullOrWhiteSpace(output) ? error.Trim() : output.Trim();
    }

    private static IReadOnlyDictionary<string, string> CreateGitAuthEnvironment(string tempRoot, string token)
    {
        var askPassPath = Path.Combine(tempRoot, "github-askpass.sh");
        File.WriteAllText(askPassPath, """
            #!/bin/sh
            case "$1" in
              *Username*) echo "x-access-token" ;;
              *) echo "$GITHUB_TOKEN" ;;
            esac
            """);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(askPassPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return new Dictionary<string, string>
        {
            ["GIT_ASKPASS"] = askPassPath,
            ["GITHUB_TOKEN"] = token,
            ["GIT_TERMINAL_PROMPT"] = "0"
        };
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

    private sealed record CreatePullRequestActionPayload(
        Guid RepositoryId,
        Guid PatchArtifactId,
        string BaseBranch,
        string HeadBranch,
        string CommitTitle,
        string PullRequestTitle,
        string PullRequestBody);
}
