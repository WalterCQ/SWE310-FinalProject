using System.Net.Http.Headers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

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
            var safeErrorMessage = Shorten(ex.Message, 1800);
            job.Status = AgentJobStatus.Failed;
            job.ErrorMessage = safeErrorMessage;
            job.CompletedAtUtc = DateTime.UtcNow;
            AddEvent(
                job.Id,
                job.UserId,
                AgentEventType.Error,
                $"Agent job failed: {safeErrorMessage}",
                JsonSerializer.Serialize(new { error = Shorten(ex.ToString(), 4000) }, JsonOptions));
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
            typeof(AIAgent).Assembly.GetName().Name ?? "Microsoft.Agents.AI",
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
            if (job.GitHubRepositoryConnectionId.HasValue)
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

                await AddCreatePullRequestApprovalAsync(job, step.Id, patchArtifact.Id, agentContext.ContextLines, cancellationToken);
            }
            else
            {
                try
                {
                    var codeAdviceSpec = await BuildCodeAdviceSpecAsync(job, agentContext.ContextLines, cancellationToken);
                    AddArtifact(job.Id, step.Id, AgentArtifactKind.CodePatch, "code-review-plan.md", BuildCodeAdviceMarkdown(codeAdviceSpec));
                }
                catch (Exception ex)
                {
                    AddSkillFailure(job, step.Id, AgentArtifactKind.CodePatch, "code-advice-spec", ex);
                    throw;
                }
            }
        }

        if (subAgents.Contains("DeckSubAgent"))
        {
            try
            {
                var deckSpec = await BuildDeckSpecAsync(job, agentContext.ContextLines, cancellationToken);
                var deckMarkdown = BuildDeckMarkdown(deckSpec);
                AddArtifact(job.Id, step.Id, AgentArtifactKind.Deck, "deck-subagent-source.md", deckMarkdown);
                var deck = await skillRegistry.BuildDeckFromMarkdownAsync(deckMarkdown, cancellationToken);
                AddArtifact(job.Id, step.Id, AgentArtifactKind.Deck, deck.FileName, deck.ContentType, deck.Content);
                AddEvent(
                    job.Id,
                    job.UserId,
                    AgentEventType.ArtifactCreated,
                    $"Runtime skill completed: {deck.SkillId}",
                    JsonSerializer.Serialize(new { deck.SkillId, deck.FileName, output = deck.ExecutionLog }, JsonOptions));
            }
            catch (Exception ex)
            {
                AddSkillFailure(job, step.Id, AgentArtifactKind.Deck, "ppt-master", ex);
                throw;
            }
        }

        if (subAgents.Contains("ReportSubAgent"))
        {
            try
            {
                var reportSpec = await BuildReportSpecAsync(job, agentContext.ContextLines, cancellationToken);
                var reportMarkdown = BuildReportMarkdown(reportSpec);
                AddArtifact(job.Id, step.Id, AgentArtifactKind.Report, "report-subagent-draft.md", reportMarkdown);
                var report = await skillRegistry.BuildDocxFromMarkdownAsync(reportMarkdown, cancellationToken);
                AddArtifact(job.Id, step.Id, AgentArtifactKind.Report, report.FileName, report.ContentType, report.Content);
                AddEvent(
                    job.Id,
                    job.UserId,
                    AgentEventType.ArtifactCreated,
                    $"Runtime skill completed: {report.SkillId}",
                    JsonSerializer.Serialize(new { report.SkillId, report.FileName, output = report.ExecutionLog }, JsonOptions));
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
        await File.WriteAllTextAsync(patchPath, patchContent, Utf8NoBom, cancellationToken);

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
            await RunProcessAsync("git", ["diff", "--check"], repoPath, token.Token, cancellationToken, gitEnvironment);

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
                validationResult = await RunRepositoryValidationAsync(repository.ValidationCommand, repoPath, token.Token, cancellationToken, gitEnvironment);
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
            TryDeleteDirectory(tempRoot);
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
        var intentGoal = NormalizeGoalForIntent(job.Goal);

        if (IsTaskCommand(intentGoal))
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

        if (IsReminderCommand(intentGoal))
        {
            var payload = new CreateReminderActionPayload(
                job.WorkspaceId,
                "AI reminder",
                Shorten(job.Goal, 300));
            AddApproval(job, stepId, "CreateReminder", "Create reminder dry-run", "Notification", payload);
        }
    }

    private async Task AddCreatePullRequestApprovalAsync(
        AgentJob job,
        Guid stepId,
        Guid patchArtifactId,
        IReadOnlyCollection<string> contextLines,
        CancellationToken cancellationToken)
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
            Context sources:
            {BuildPullRequestSourceSummary(contextLines)}

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

    private async Task<ReportSpec> BuildReportSpecAsync(
        AgentJob job,
        IReadOnlyCollection<string> contextLines,
        CancellationToken cancellationToken)
    {
        var sources = BuildSourceReferences(contextLines);
        var json = await RequestArtifactSpecJsonAsync(
            job,
            "report",
            $$"""
            Build a polished coursework/demo report specification from the approved TaskFlow context.

            Return exactly one JSON object with this shape:
            {
              "title": "short report title",
              "audience": "intended readers",
              "sourceIds": ["S1"],
              "confidenceNotes": "one sentence about limits or missing evidence",
              "sections": [
                {
                  "heading": "Executive Summary",
                  "purpose": "one concise paragraph",
                  "bullets": ["specific grounded point"],
                  "evidenceNeeded": ["screenshot or validation evidence to collect"],
                  "sourceIds": ["S1"]
                }
              ]
            }

            Rules:
            - Use only source ids listed below; do not invent facts.
            - Create exactly 5 body sections: Executive Summary, Requirements and Deliverables, Evidence Checklist, Risks and Acceptance Checks, Next Actions.
            - Do not create a Sources section; the renderer adds the source appendix from sourceIds.
            - Every section must have at least 2 bullets and at least 1 source id.
            - Keep headings professional and coursework/demo friendly.
            - If a fact is missing, put it in evidenceNeeded instead of inventing it.

            Goal:
            {{job.Goal}}

            Sources:
            {{BuildSourcePromptBlock(sources)}}
            """,
            cancellationToken);

        return ValidateReportSpec(DeserializeSpec<ReportSpec>(json, "report"), sources);
    }

    private async Task<DeckSpec> BuildDeckSpecAsync(
        AgentJob job,
        IReadOnlyCollection<string> contextLines,
        CancellationToken cancellationToken)
    {
        var sources = BuildSourceReferences(contextLines);
        var json = await RequestArtifactSpecJsonAsync(
            job,
            "deck",
            $$"""
            Build a professional editable presentation specification for a TaskFlow coursework/demo walkthrough.

            Return exactly one JSON object with this shape:
            {
              "title": "short deck title",
              "audience": "intended viewers",
              "sourceIds": ["S1"],
              "confidenceNotes": "one sentence about limits or missing evidence",
              "slides": [
                {
                  "title": "unique slide title",
                  "bullets": ["short bullet, maximum 12 words"],
                  "speakerNotes": "one sentence presenter note",
                  "sourceIds": ["S1"]
                }
              ]
            }

            Rules:
            - Use only source ids listed below; do not invent facts.
            - Create 6 to 8 slides in this order: title, agenda, assignment summary, deliverables, evidence plan, risks/quality checks, next actions, sources.
            - Each slide title must be unique.
            - Each slide must have 2 to 4 bullets and at least 1 source id.
            - Bullets must be short, concrete, and presentation-ready.

            Goal:
            {{job.Goal}}

            Sources:
            {{BuildSourcePromptBlock(sources)}}
            """,
            cancellationToken);

        return ValidateDeckSpec(DeserializeSpec<DeckSpec>(json, "deck"), sources);
    }

    private async Task<CodeAdviceSpec> BuildCodeAdviceSpecAsync(
        AgentJob job,
        IReadOnlyCollection<string> contextLines,
        CancellationToken cancellationToken)
    {
        var sources = BuildSourceReferences(contextLines);
        var json = await RequestArtifactSpecJsonAsync(
            job,
            "code advice",
            $$"""
            Build a code review plan from approved TaskFlow context. No GitHub repository is connected, so do not produce a patch.

            Return exactly one JSON object with this shape:
            {
              "title": "short code advice title",
              "audience": "intended readers",
              "sourceIds": ["S1"],
              "confidenceNotes": "one sentence about limits or missing repository evidence",
              "findings": [
                {
                  "title": "specific finding",
                  "rationale": "why it matters",
                  "recommendation": "practical next step",
                  "sourceIds": ["S1"]
                }
              ],
              "validationPlan": ["command or manual check to run"]
            }

            Rules:
            - Use only source ids listed below; do not invent repository files.
            - Make it clear this is a review plan, not an applied patch.
            - Include 3 to 6 findings and at least 2 validation steps.
            - Every finding must cite at least 1 source id.

            Goal:
            {{job.Goal}}

            Sources:
            {{BuildSourcePromptBlock(sources)}}
            """,
            cancellationToken);

        return ValidateCodeAdviceSpec(DeserializeSpec<CodeAdviceSpec>(json, "code advice"), sources);
    }

    private async Task<string> RequestArtifactSpecJsonAsync(
        AgentJob job,
        string artifactKind,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        var provider = await ResolveJobProviderRuntimeAsync(job, cancellationToken);
        var client = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{provider.BaseUrl.TrimEnd('/')}/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["model"] = provider.Model,
            ["temperature"] = 0.1,
            ["max_tokens"] = 7000,
            ["response_format"] = new { type = "json_object" },
            ["messages"] = new object[]
            {
                new
                {
                    role = "system",
                    content = $"You are TaskFlow ArtifactSpecAgent. Return only valid JSON for a {artifactKind} artifact. Do not include Markdown fences or commentary."
                },
                new { role = "user", content = userPrompt }
            }
        }, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Provider {artifactKind} spec call failed with {(int)response.StatusCode}: {Shorten(RedactSecret(body, provider.ApiKey), 400)}");
        }

        using var document = JsonDocument.Parse(body);
        var content = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException($"Provider returned an empty {artifactKind} spec.");
        }

        JsonDocument specDocument;
        try
        {
            specDocument = JsonDocument.Parse(content);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Provider returned invalid {artifactKind} spec JSON: {ex.Message}. Content excerpt: {Shorten(content, 600)}",
                ex);
        }

        using (specDocument)
        {
            if (specDocument.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"Provider returned a non-object {artifactKind} spec.");
            }

            return specDocument.RootElement.GetRawText();
        }
    }

    private static TSpec DeserializeSpec<TSpec>(string json, string artifactKind)
    {
        try
        {
            return JsonSerializer.Deserialize<TSpec>(json, JsonOptions)
                ?? throw new InvalidOperationException($"Provider returned an empty {artifactKind} spec.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Provider returned invalid {artifactKind} spec JSON: {ex.Message}", ex);
        }
    }

    private static ReportSpec ValidateReportSpec(ReportSpec spec, IReadOnlyCollection<ArtifactSource> sources)
    {
        var sourceIds = ValidateSourceIds(spec.SourceIds, sources, "report");
        var sections = spec.Sections?.ToArray() ?? [];
        if (sections.Length < 5)
        {
            throw new InvalidOperationException("Report spec must include at least 5 sections.");
        }

        var normalizedSections = sections.Select((section, index) =>
        {
            var bullets = NormalizeList(section.Bullets, $"report section {index + 1} bullets", minimum: 2);
            var evidence = NormalizeList(section.EvidenceNeeded, $"report section {index + 1} evidence", minimum: 1);
            return new ReportSectionSpec(
                RequiredText(section.Heading, $"report section {index + 1} heading"),
                RequiredText(section.Purpose, $"report section {index + 1} purpose"),
                bullets,
                evidence,
                ValidateSourceIds(section.SourceIds, sources, $"report section {index + 1}"));
        }).ToArray();

        return new ReportSpec(
            RequiredText(spec.Title, "report title"),
            RequiredText(spec.Audience, "report audience"),
            sourceIds,
            RequiredText(spec.ConfidenceNotes, "report confidence notes"),
            normalizedSections);
    }

    private static DeckSpec ValidateDeckSpec(DeckSpec spec, IReadOnlyCollection<ArtifactSource> sources)
    {
        var sourceIds = ValidateSourceIds(spec.SourceIds, sources, "deck");
        var slides = spec.Slides?.ToArray() ?? [];
        if (slides.Length < 5)
        {
            throw new InvalidOperationException("Deck spec must include at least 5 slides.");
        }

        var seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedSlides = slides.Select((slide, index) =>
        {
            var title = RequiredText(slide.Title, $"deck slide {index + 1} title");
            if (!seenTitles.Add(title))
            {
                throw new InvalidOperationException($"Deck slide title is duplicated: {title}");
            }

            var bullets = NormalizeDeckBullets(slide.Bullets, $"deck slide {index + 1} bullets");
            return new DeckSlideSpec(
                title,
                bullets,
                RequiredText(slide.SpeakerNotes, $"deck slide {index + 1} speaker notes"),
                ValidateSourceIds(slide.SourceIds, sources, $"deck slide {index + 1}"));
        }).ToArray();

        return new DeckSpec(
            RequiredText(spec.Title, "deck title"),
            RequiredText(spec.Audience, "deck audience"),
            sourceIds,
            RequiredText(spec.ConfidenceNotes, "deck confidence notes"),
            normalizedSlides);
    }

    private static CodeAdviceSpec ValidateCodeAdviceSpec(CodeAdviceSpec spec, IReadOnlyCollection<ArtifactSource> sources)
    {
        var sourceIds = ValidateSourceIds(spec.SourceIds, sources, "code advice");
        var findings = spec.Findings?.ToArray() ?? [];
        if (findings.Length < 3)
        {
            throw new InvalidOperationException("Code advice spec must include at least 3 findings.");
        }

        var normalizedFindings = findings.Select((finding, index) => new CodeFindingSpec(
            RequiredText(finding.Title, $"code finding {index + 1} title"),
            RequiredText(finding.Rationale, $"code finding {index + 1} rationale"),
            RequiredText(finding.Recommendation, $"code finding {index + 1} recommendation"),
            ValidateSourceIds(finding.SourceIds, sources, $"code finding {index + 1}"))).ToArray();

        return new CodeAdviceSpec(
            RequiredText(spec.Title, "code advice title"),
            RequiredText(spec.Audience, "code advice audience"),
            sourceIds,
            RequiredText(spec.ConfidenceNotes, "code advice confidence notes"),
            normalizedFindings,
            NormalizeList(spec.ValidationPlan, "code validation plan", minimum: 2));
    }

    private static IReadOnlyCollection<ArtifactSource> BuildSourceReferences(IReadOnlyCollection<string> contextLines)
    {
        var sourceLines = contextLines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Take(12)
            .ToArray();

        if (sourceLines.Length == 0)
        {
            throw new InvalidOperationException("No indexed channel, task, or attachment context was available for artifact generation.");
        }

        return sourceLines
            .Select((line, index) =>
            {
                var labelEnd = line.IndexOf(':', StringComparison.Ordinal);
                var label = labelEnd > 0 ? line[..labelEnd].Trim() : $"Source {index + 1}";
                return new ArtifactSource($"S{index + 1}", label, Shorten(line.Replace(Environment.NewLine, " ", StringComparison.Ordinal), 900));
            })
            .ToArray();
    }

    private static string BuildSourcePromptBlock(IReadOnlyCollection<ArtifactSource> sources)
    {
        return string.Join(Environment.NewLine, sources.Select(source => $"- {source.Id} ({source.Label}): {source.Content}"));
    }

    private static string BuildPullRequestSourceSummary(IReadOnlyCollection<string> contextLines)
    {
        var sources = BuildSourceReferences(contextLines)
            .Take(8)
            .Select(source => $"- {source.Id} {source.Label}: {Shorten(source.Content, 220)}")
            .ToArray();

        return sources.Length == 0 ? "- No indexed context sources were available." : string.Join(Environment.NewLine, sources);
    }

    private static IReadOnlyCollection<string> ValidateSourceIds(
        IReadOnlyCollection<string>? sourceIds,
        IReadOnlyCollection<ArtifactSource> sources,
        string owner)
    {
        var allowed = sources.Select(source => source.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalized = (sourceIds ?? [])
            .Select(sourceId => sourceId.Trim())
            .Where(sourceId => !string.IsNullOrWhiteSpace(sourceId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalized.Length == 0)
        {
            throw new InvalidOperationException($"{owner} must cite at least one source id.");
        }

        var invalid = normalized.Where(sourceId => !allowed.Contains(sourceId)).ToArray();
        if (invalid.Length > 0)
        {
            throw new InvalidOperationException($"{owner} cited unknown source ids: {string.Join(", ", invalid)}.");
        }

        return normalized;
    }

    private static IReadOnlyCollection<string> NormalizeList(
        IReadOnlyCollection<string>? values,
        string owner,
        int minimum,
        int? maximum = null)
    {
        var normalized = (values ?? [])
            .Select(value => value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        if (normalized.Length < minimum)
        {
            throw new InvalidOperationException($"{owner} must include at least {minimum} item(s).");
        }

        if (maximum.HasValue && normalized.Length > maximum.Value)
        {
            throw new InvalidOperationException($"{owner} must include no more than {maximum.Value} item(s).");
        }

        return normalized;
    }

    private static IReadOnlyCollection<string> NormalizeDeckBullets(
        IReadOnlyCollection<string>? values,
        string owner)
    {
        var normalized = (values ?? [])
            .Select(value => value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        if (normalized.Length < 2)
        {
            throw new InvalidOperationException($"{owner} must include at least 2 item(s).");
        }

        return normalized.Take(4).ToArray();
    }

    private static string RequiredText(string? value, string owner)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException($"{owner} is required.");
        }

        return normalized;
    }

    private static string BuildReportMarkdown(ReportSpec spec)
    {
        var title = spec.Title ?? string.Empty;
        var audience = spec.Audience ?? string.Empty;
        var confidenceNotes = spec.ConfidenceNotes ?? string.Empty;
        var lines = new List<string>
        {
            "---",
            $"title: {EscapeYaml(title)}",
            $"date: {DateTime.UtcNow:yyyy-MM-dd}",
            "version: 1.0",
            $"audience: {EscapeYaml(audience)}",
            "---",
            "",
            $"# {title}",
            "",
            $"Audience: {audience}",
            "",
            $"Confidence note: {confidenceNotes}",
            ""
        };

        foreach (var section in spec.Sections ?? [])
        {
            lines.Add($"## {section.Heading}");
            lines.Add("");
            lines.Add(section.Purpose ?? string.Empty);
            lines.Add("");
            foreach (var bullet in section.Bullets ?? [])
            {
                lines.Add($"- {bullet}");
            }

            lines.Add("");
            lines.Add("| Evidence Needed | Sources |");
            lines.Add("| --- | --- |");
            foreach (var evidence in section.EvidenceNeeded ?? [])
            {
                lines.Add($"| {EscapeTableCell(evidence)} | {EscapeTableCell(string.Join(", ", section.SourceIds ?? []))} |");
            }
            lines.Add("");
        }

        lines.Add("## Source Appendix");
        lines.Add("");
        foreach (var sourceId in spec.SourceIds ?? [])
        {
            lines.Add($"- {sourceId}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildDeckMarkdown(DeckSpec spec)
    {
        var lines = new List<string>();
        foreach (var slide in spec.Slides ?? [])
        {
            lines.Add($"# {slide.Title}");
            lines.Add("");
            foreach (var bullet in slide.Bullets ?? [])
            {
                lines.Add($"- {bullet}");
            }
            lines.Add($"- Sources: {string.Join(", ", slide.SourceIds ?? [])}");
            lines.Add($"Notes: {slide.SpeakerNotes}");
            lines.Add("");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildCodeAdviceMarkdown(CodeAdviceSpec spec)
    {
        var lines = new List<string>
        {
            $"# {spec.Title}",
            "",
            $"Audience: {spec.Audience}",
            "",
            $"Confidence note: {spec.ConfidenceNotes}",
            "",
            "This artifact is a review plan because no GitHub repository connection is attached to the AgentJob. It is not a generated patch.",
            "",
            "## Findings"
        };

        foreach (var finding in spec.Findings ?? [])
        {
            lines.Add("");
            lines.Add($"### {finding.Title}");
            lines.Add("");
            lines.Add($"Rationale: {finding.Rationale}");
            lines.Add("");
            lines.Add($"Recommendation: {finding.Recommendation}");
            lines.Add("");
            lines.Add($"Sources: {string.Join(", ", finding.SourceIds ?? [])}");
        }

        lines.Add("");
        lines.Add("## Validation Plan");
        foreach (var item in spec.ValidationPlan ?? [])
        {
            lines.Add($"- {item}");
        }

        lines.Add("");
        lines.Add("## Source Appendix");
        foreach (var sourceId in spec.SourceIds ?? [])
        {
            lines.Add($"- {sourceId}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string EscapeYaml(string value)
    {
        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static string EscapeTableCell(string value)
    {
        return value.Replace("|", "\\|", StringComparison.Ordinal).Replace(Environment.NewLine, " ", StringComparison.Ordinal);
    }

    private async Task<AgentRuntimeContext> LoadAgentContextAsync(AgentJob job, CancellationToken cancellationToken)
    {
        if (job.ChannelId.HasValue)
        {
            var attachmentIds = ResolveJobAttachmentIds(job);
            var contextResult = await aiContextService.BuildChannelContextAsync(
                job.ChannelId.Value,
                job.Goal,
                attachmentIds,
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

        var attachmentIds = ResolveJobAttachmentIds(job);
        if (attachmentIds is { Count: > 0 })
        {
            query = query.Where(chunk => chunk.AttachmentId.HasValue && attachmentIds.Contains(chunk.AttachmentId.Value));
        }
        else if (job.AttachmentId.HasValue)
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

    private static IReadOnlyCollection<Guid>? ResolveJobAttachmentIds(AgentJob job)
    {
        if (!string.IsNullOrWhiteSpace(job.ContextAttachmentIdsJson))
        {
            return JsonSerializer.Deserialize<Guid[]>(job.ContextAttachmentIdsJson, JsonOptions)?
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToArray() ?? [];
        }

        return job.AttachmentId.HasValue ? [job.AttachmentId.Value] : null;
    }

    private async Task<CodePatchBuildResult> BuildCodePatchAsync(
        AgentJob job,
        Guid stepId,
        IReadOnlyCollection<string> contextLines,
        CancellationToken cancellationToken)
    {
        if (!job.GitHubRepositoryConnectionId.HasValue)
        {
            throw new InvalidOperationException("A GitHub repository connection is required before generating a patch artifact.");
        }

        var repository = await dbContext.GitHubRepositoryConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(item =>
                item.Id == job.GitHubRepositoryConnectionId.Value
                && item.WorkspaceId == job.WorkspaceId
                && item.IsEnabled,
                cancellationToken)
            ?? throw new InvalidOperationException("GitHub repository connection was not found or is disabled.");

        var provider = await ResolveJobProviderRuntimeAsync(job, cancellationToken);
        var tempRoot = Path.Combine(Path.GetTempPath(), "taskflow-agent-code", job.Id.ToString("N"), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var repoPath = Path.Combine(tempRoot, "repo");
        var patchPath = Path.Combine(tempRoot, "agent.patch");

        try
        {
            var token = await gitHubRepositoryService.CreateInstallationAccessTokenAsync(repository.InstallationId, cancellationToken);
            var gitEnvironment = CreateGitAuthEnvironment(tempRoot, token.Token);
            var remoteUrl = $"https://github.com/{repository.FullName}.git";
            await RunProcessAsync("git", ["clone", "--depth", "1", "--branch", repository.DefaultBranch, remoteUrl, repoPath], tempRoot, token.Token, cancellationToken, gitEnvironment);

            var repositoryContext = await BuildRepositoryContextAsync(repoPath, token.Token, gitEnvironment, contextLines, cancellationToken);
            var rawPatch = await RequestRepositoryPatchAsync(provider, job, repository, repositoryContext, contextLines, repoPath, token.Token, gitEnvironment, cancellationToken);
            var patch = NormalizeUnifiedDiff(rawPatch);
            await File.WriteAllTextAsync(patchPath, patch, Utf8NoBom, cancellationToken);
            await RunProcessAsync("git", ["apply", "--check", patchPath], repoPath, token.Token, cancellationToken, gitEnvironment);
            await RunProcessAsync("git", ["apply", patchPath], repoPath, token.Token, cancellationToken, gitEnvironment);

            var diffSummary = await RunProcessAsync("git", ["diff", "--stat", "HEAD"], repoPath, token.Token, cancellationToken, gitEnvironment);
            if (string.IsNullOrWhiteSpace(diffSummary))
            {
                throw new InvalidOperationException("Generated patch applied but produced no repository diff.");
            }

            var validationResult = string.IsNullOrWhiteSpace(repository.ValidationCommand)
                ? "Validation skipped: no validation command configured for this repository."
                : await RunRepositoryValidationAsync(repository.ValidationCommand, repoPath, token.Token, cancellationToken, gitEnvironment);

            AddEvent(
                job.Id,
                job.UserId,
                AgentEventType.StepCompleted,
                "CodeSubAgent generated and validated a repository patch.",
                JsonSerializer.Serialize(new
                {
                    repository = repository.FullName,
                    validation = validationResult,
                    diffSummary
                }, JsonOptions));
            return new CodePatchBuildResult(patch, validationResult, diffSummary);
        }
        catch (Exception ex)
        {
            AddArtifact(
                job.Id,
                stepId,
                AgentArtifactKind.CodePatch,
                "code-patch-validation-error.md",
                $"""
                # Code Patch Failed

                Repository: {repository.FullName}
                Goal: {job.Goal}

                The generated patch was not accepted, so no GitHub PR approval was created.

                ## Error

                {ex.Message}
                """);
            AddEvent(job.Id, job.UserId, AgentEventType.Error, "CodeSubAgent failed to generate a valid repository patch.", JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions));
            throw;
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private async Task<string> BuildRepositoryContextAsync(
        string repoPath,
        string gitSecret,
        IReadOnlyDictionary<string, string> gitEnvironment,
        IReadOnlyCollection<string> contextLines,
        CancellationToken cancellationToken)
    {
        var fileListOutput = await RunProcessAsync("git", ["ls-files"], repoPath, gitSecret, cancellationToken, gitEnvironment);
        var tokens = Tokenize(string.Join(' ', contextLines)).ToArray();
        var availableFiles = fileListOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(IsRelevantRepositoryFile)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
            var priorityFiles = new[]
            {
                "backend/TaskFlow.Api/Services/AiCommandService.cs",
                "backend/TaskFlow.AgentWorker/AgentJobProcessor.cs",
                "backend/TaskFlow.Api/Controllers/TasksController.cs",
                "frontend/src/pages/Channels.jsx",
                "frontend/src/api/taskflowApi.js",
                "frontend/src/i18n/messages/channel.js"
            }
            .Where(path => availableFiles.Contains(path, StringComparer.Ordinal))
            .ToArray();
        var candidateFiles = priorityFiles
            .Concat(availableFiles
            .Select(path => new { Path = path, Score = ScoreRepositoryPath(path, tokens) })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Path)
            .Select(item => item.Path))
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .ToArray();

        var sections = new List<string>
        {
            "# Repository Context",
            "",
            "## Selected files",
            string.Join(Environment.NewLine, candidateFiles.Select(path => $"- {path}")),
            ""
        };

        foreach (var relativePath in candidateFiles)
        {
            var fullPath = Path.GetFullPath(Path.Combine(repoPath, relativePath));
            if (!fullPath.StartsWith(Path.GetFullPath(repoPath), StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                var fileInfo = new FileInfo(fullPath);
                if (!fileInfo.Exists || fileInfo.Length > 160_000)
                {
                    continue;
                }

                var content = await File.ReadAllTextAsync(fullPath, cancellationToken);
                sections.Add($"## File: {relativePath}");
                sections.Add("```");
                sections.Add(BuildCodePatchFileExcerpt(content, tokens, maxLength: 2200));
                sections.Add("```");
                sections.Add("");
            }
            catch (Exception ex)
            {
                sections.Add($"## File: {relativePath}");
                sections.Add($"Skipped: {ex.Message}");
                sections.Add("");
            }
        }

        return string.Join(Environment.NewLine, sections);
    }

    private async Task<string> BuildDiffFromPatchInstructionAsync(
        RepositoryPatchInstruction instruction,
        string repoPath,
        string gitSecret,
        IReadOnlyDictionary<string, string> gitEnvironment,
        CancellationToken cancellationToken)
    {
        var filePath = RequiredText(instruction.FilePath, "code edit file path").Replace('\\', '/');
        if (Path.IsPathRooted(filePath) || filePath.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Provider selected an unsafe repository path: {filePath}");
        }

        var find = RequiredText(instruction.Find, "code edit find text");
        var replace = instruction.Replace ?? string.Empty;
        ValidatePatchInstructionText(filePath, find, replace);
        if (string.Equals(find, replace, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Provider code edit did not change the selected text.");
        }

        var repoRoot = Path.GetFullPath(repoPath);
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, filePath));
        var repoRootWithSeparator = repoRoot.EndsWith(Path.DirectorySeparatorChar)
            ? repoRoot
            : repoRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(repoRootWithSeparator, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Provider selected an unsafe repository path: {filePath}");
        }

        if (!File.Exists(fullPath))
        {
            throw new InvalidOperationException($"Provider selected a repository file that does not exist: {filePath}");
        }

        var content = await File.ReadAllTextAsync(fullPath, cancellationToken);
        var firstIndex = content.IndexOf(find, StringComparison.Ordinal);
        if (firstIndex < 0)
        {
            throw new InvalidOperationException($"Provider find text was not found exactly in {filePath}.");
        }

        if (content.IndexOf(find, firstIndex + find.Length, StringComparison.Ordinal) >= 0)
        {
            throw new InvalidOperationException($"Provider find text was ambiguous in {filePath}.");
        }

        var updated = content[..firstIndex] + replace + content[(firstIndex + find.Length)..];
        await File.WriteAllTextAsync(fullPath, updated, Utf8NoBom, cancellationToken);
        try
        {
            await RunProcessAsync("git", ["diff", "--check"], repoPath, gitSecret, cancellationToken, gitEnvironment);
            return await RunProcessAsync("git", ["diff", "--no-ext-diff", "--", filePath], repoPath, gitSecret, cancellationToken, gitEnvironment, preserveOutput: true);
        }
        finally
        {
            await RunProcessAsync("git", ["restore", "--source=HEAD", "--", filePath], repoPath, gitSecret, cancellationToken, gitEnvironment);
        }
    }

    private static void ValidatePatchInstructionText(string filePath, string find, string replace)
    {
        if (find.Contains('\uFEFF', StringComparison.Ordinal) || replace.Contains('\uFEFF', StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Provider code edit for {filePath} included a UTF-8 BOM marker.");
        }

        var findIndent = FirstNonEmptyLineIndent(find);
        var replaceIndent = FirstNonEmptyLineIndent(replace);
        if (findIndent is not null
            && replaceIndent is not null
            && !string.Equals(findIndent, replaceIndent, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Provider code edit for {filePath} changed the leading indentation of the selected block.");
        }
    }

    private static string? FirstNonEmptyLineIndent(string value)
    {
        foreach (var line in value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var index = 0;
            while (index < line.Length && char.IsWhiteSpace(line[index]))
            {
                index++;
            }

            return line[..index];
        }

        return null;
    }

    private static string BuildCodePatchFileExcerpt(string content, IReadOnlyCollection<string> tokens, int maxLength)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        var lines = normalized.Split('\n');
        var scoringTokens = tokens
            .Concat(["agent", "approval", "artifact", "channel", "github", "source", "taskflow"])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var windows = lines
            .Select((line, index) => new
            {
                Index = index,
                Score = scoringTokens.Count(token => line.Contains(token, StringComparison.OrdinalIgnoreCase))
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Index)
            .Take(4)
            .SelectMany(item => Enumerable.Range(Math.Max(0, item.Index - 5), Math.Min(lines.Length - Math.Max(0, item.Index - 5), 12)))
            .Distinct()
            .OrderBy(index => index)
            .ToArray();

        if (windows.Length == 0)
        {
            return Shorten(normalized, maxLength);
        }

        var builder = new StringBuilder();
        var previous = -2;
        foreach (var index in windows)
        {
            if (index != previous + 1)
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine("...");
                }

                builder.AppendLine($"// lines {index + 1}-{Math.Min(lines.Length, index + 12)}");
            }

            builder.AppendLine(lines[index]);
            previous = index;
            if (builder.Length >= maxLength)
            {
                break;
            }
        }

        return Shorten(builder.ToString().TrimEnd(), maxLength);
    }

    private async Task<string> RequestRepositoryPatchAsync(
        AiProviderRuntime provider,
        AgentJob job,
        GitHubRepositoryConnection repository,
        string repositoryContext,
        IReadOnlyCollection<string> contextLines,
        string repoPath,
        string gitSecret,
        IReadOnlyDictionary<string, string> gitEnvironment,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        var compactContext = contextLines
            .Take(6)
            .Select(line => Shorten(line.Replace(Environment.NewLine, " ", StringComparison.Ordinal), 500));
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{provider.BaseUrl.TrimEnd('/')}/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            model = provider.Model,
            temperature = 0,
            max_tokens = 2000,
            response_format = new { type = "json_object" },
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = """
                    You are TaskFlow CodeSubAgent. Return only a JSON object.
                    Shape:
                    {"status":"edit","filePath":"provided/file/path","find":"exact existing text","replace":"replacement text","reason":"short reason"}
                    or
                    {"status":"no_patch","reason":"short reason"}
                    The edit must be small, grounded in the provided repository files and TaskFlow context.
                    Change exactly one provided file.
                    The find value must be one short contiguous block copied exactly from the repository context excerpts.
                    The find value must not include ellipsis markers or line-marker comments.
                    Preserve the original file encoding and indentation.
                    Do not add a UTF-8 BOM or invisible prefix characters.
                    Do not change frontend API paths unless the matching backend route appears in the provided repository context.
                    Do not rename existing request/response fields unless the matching backend DTO or controller contract appears in the provided repository context.
                    Prefer a small UI copy, validation, or source/approval clarity improvement over changing API contracts.
                    Do not invent repository paths outside the provided file list.
                    """
                },
                new
                {
                    role = "user",
                    content = $"""
                    Repository: {repository.FullName}
                    Default branch: {repository.DefaultBranch}
                    Goal:
                    {job.Goal}

                    TaskFlow context:
                    {string.Join(Environment.NewLine, compactContext)}

                    Repository context:
                    {repositoryContext}
                    """
                }
            }
        }, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Provider code patch call failed with {(int)response.StatusCode}: {Shorten(RedactSecret(body, provider.ApiKey), 400)}");
        }

        using var document = JsonDocument.Parse(body);
        var content = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("Provider returned an empty code patch.");
        }

        try
        {
            using var patchDocument = JsonDocument.Parse(content);
            var root = patchDocument.RootElement;
            var status = root.TryGetProperty("status", out var statusElement)
                ? statusElement.GetString()
                : null;
            if (string.Equals(status, "no_patch", StringComparison.OrdinalIgnoreCase))
            {
                var reason = root.TryGetProperty("reason", out var reasonElement)
                    ? reasonElement.GetString()
                    : "provider returned no_patch without a reason";
                throw new InvalidOperationException($"NO_PATCH: {reason}");
            }

            if (!string.Equals(status, "edit", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Provider code patch JSON did not include status=edit. Content excerpt: {Shorten(content, 600)}");
            }

            var instruction = new RepositoryPatchInstruction(
                root.TryGetProperty("filePath", out var filePathElement) ? filePathElement.GetString() : null,
                root.TryGetProperty("find", out var findElement) ? findElement.GetString() : null,
                root.TryGetProperty("replace", out var replaceElement) ? replaceElement.GetString() : null,
                root.TryGetProperty("reason", out var editReasonElement) ? editReasonElement.GetString() : null);
            var diff = await BuildDiffFromPatchInstructionAsync(instruction, repoPath, gitSecret, gitEnvironment, cancellationToken);
            if (string.IsNullOrWhiteSpace(diff))
            {
                throw new InvalidOperationException("Provider code edit produced an empty diff.");
            }

            return diff;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Provider returned invalid code patch JSON: {ex.Message}. Content excerpt: {Shorten(content, 600)}", ex);
        }
    }

    private async Task<string?> TryAskProviderForPlanNotesAsync(AgentJob job, IReadOnlyCollection<string> subAgents, CancellationToken cancellationToken)
    {
        var runtime = await ResolveJobProviderRuntimeAsync(job, cancellationToken);
        return await RequestPlanNotesAsync(runtime, job, subAgents, cancellationToken);
    }

    private async Task<AiProviderRuntime> ResolveJobProviderRuntimeAsync(AgentJob job, CancellationToken cancellationToken)
    {
        if (!job.ProviderCredentialId.HasValue)
        {
            var workspaceProviderResult = await aiProviderService.ResolveWorkspaceProviderAsync(job.WorkspaceId, cancellationToken);
            if (!workspaceProviderResult.Success || workspaceProviderResult.Data is null)
            {
                throw new InvalidOperationException(workspaceProviderResult.Message);
            }

            return workspaceProviderResult.Data;
        }

        var credential = await dbContext.AiProviderCredentials
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == job.ProviderCredentialId.Value && item.UserId == job.UserId, cancellationToken);
        if (credential is null)
        {
            throw new InvalidOperationException("Configured provider was not found for this user.");
        }

        return new AiProviderRuntime
        {
            ProviderName = credential.ProviderName,
            BaseUrl = string.IsNullOrWhiteSpace(credential.BaseUrl)
                ? "https://api.openai.com/v1"
                : credential.BaseUrl.TrimEnd('/'),
            Model = credential.Model,
            ApiKey = AiProviderService.Unprotect(dataProtectionProvider, credential.EncryptedApiKey),
            SupportsToolCalls = credential.SupportsToolCalls
        };
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
            Details = Shorten(details, 1000)
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
            Message = Shorten(message, 1000),
            DataJson = dataJson is null ? null : Shorten(dataJson, 4000)
        });
    }

    private void AddSkillFailure(AgentJob job, Guid stepId, AgentArtifactKind kind, string skillId, Exception exception)
    {
        AddArtifact(
            job.Id,
            stepId,
            kind,
            $"{skillId}-error.md",
            $"""
            # Runtime Skill Failed

            Skill: {skillId}
            Job: {job.Id}

            ## Error

            {exception.Message}
            """);
        AddEvent(
            job.Id,
            job.UserId,
            AgentEventType.Error,
            $"Runtime skill failed: {skillId}",
            JsonSerializer.Serialize(new { skillId, error = exception.Message }, JsonOptions));
    }

    private static IReadOnlyCollection<string> InferSubAgents(string goal, string? artifactTarget)
    {
        var intentGoal = NormalizeGoalForIntent(goal);
        var subAgents = new List<string> { "SummarySubAgent" };

        if (ContainsAny(intentGoal, "code", "代码", "implement", "bug", "fix")
            || artifactTarget is "patch" or "pull-request")
        {
            subAgents.Add("CodeSubAgent");
        }

        if (ContainsAny(intentGoal, "ppt", "deck", "slides", "presentation", "幻灯片")
            || artifactTarget == "pptx")
        {
            subAgents.Add("DeckSubAgent");
        }

        if (ContainsAny(intentGoal, "report", "报告", "document", "文档")
            || artifactTarget == "docx")
        {
            subAgents.Add("ReportSubAgent");
        }

        if (IsTaskCommand(intentGoal) || IsReminderCommand(intentGoal))
        {
            subAgents.Add("TaskFlowActionSubAgent");
        }

        return subAgents.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyCollection<ToolIntent> BuildToolIntents(AgentJob job, IReadOnlyCollection<string> subAgents)
    {
        var intents = new List<ToolIntent>
        {
            new("SummarySubAgent", "collect_context", "AiContextService", false)
        };

        if (subAgents.Contains("CodeSubAgent"))
        {
            intents.Add(new("CodeSubAgent", "generate_unified_diff", "GitHub repository clone or patch artifact", false));
            if (job.GitHubRepositoryConnectionId.HasValue)
            {
                intents.Add(new("CodeSubAgent", "create_pull_request", "GitHubRepositoryConnection", true));
            }
        }

        if (subAgents.Contains("DeckSubAgent"))
        {
            intents.Add(new("DeckSubAgent", "build_pptx", "ppt-master runtime skill", false));
        }

        if (subAgents.Contains("ReportSubAgent"))
        {
            intents.Add(new("ReportSubAgent", "build_docx", "md-to-docx/openxml-docx runtime skill", false));
        }

        if (subAgents.Contains("TaskFlowActionSubAgent"))
        {
            intents.Add(new("TaskFlowActionSubAgent", "preview_taskflow_write", "TaskItem/Notification", true));
        }

        return intents;
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

    private static string NormalizeGoalForIntent(string goal)
    {
        return Regex
            .Replace(goal, @"@?\s*TaskFlow\s+AI\b\s*[:,：-]?", string.Empty, RegexOptions.IgnoreCase)
            .Trim();
    }

    private static bool IsTaskCommand(string goal)
    {
        return Regex.IsMatch(goal, @"\b(tasks?|todos?|to-dos?)\b", RegexOptions.IgnoreCase)
            || goal.Contains("任务", StringComparison.OrdinalIgnoreCase)
            || goal.Contains("待办", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReminderCommand(string goal)
    {
        return Regex.IsMatch(goal, @"\b(reminders?|notifications?|notify)\b", RegexOptions.IgnoreCase)
            || goal.Contains("提醒", StringComparison.OrdinalIgnoreCase)
            || goal.Contains("通知", StringComparison.OrdinalIgnoreCase);
    }

    private static string Shorten(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        if (maxLength <= 3)
        {
            return value[..maxLength];
        }

        return value[..(maxLength - 3)].TrimEnd() + "...";
    }

    private static string RedactSecret(string value, string secret)
    {
        return string.IsNullOrWhiteSpace(secret)
            ? value
            : value.Replace(secret, "[redacted]", StringComparison.Ordinal);
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        return text
            .Split([' ', '\t', '\r', '\n', '.', ',', ':', ';', '/', '\\', '-', '_', '(', ')', '[', ']', '{', '}', '"', '\''], StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim().ToLowerInvariant())
            .Where(token => token.Length >= 3)
            .Distinct()
            .Take(32);
    }

    private static bool IsRelevantRepositoryFile(string path)
    {
        if (path.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("node_modules/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/dist/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/build/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("Migrations/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension is ".cs" or ".csproj" or ".sln" or ".jsx" or ".js" or ".ts" or ".tsx" or ".css" or ".json" or ".md" or ".yml" or ".yaml";
    }

    private static int ScoreRepositoryPath(string path, IReadOnlyCollection<string> tokens)
    {
        var normalized = path.ToLowerInvariant();
        var score = tokens.Count(token => normalized.Contains(token, StringComparison.OrdinalIgnoreCase)) * 10;
        if (normalized.EndsWith("readme.md", StringComparison.OrdinalIgnoreCase))
        {
            score += 80;
        }

        if (normalized.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("package.json", StringComparison.OrdinalIgnoreCase))
        {
            score += 70;
        }

        if (normalized.Contains("src/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("backend/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("frontend/", StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
        }

        return score;
    }

    private static string NormalizeUnifiedDiff(string content)
    {
        var normalized = content;
        if (normalized.StartsWith("```", StringComparison.Ordinal))
        {
            normalized = normalized
                .Replace("```diff", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("```patch", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("```", string.Empty, StringComparison.Ordinal)
                .Trim();
        }

        if (normalized.StartsWith("NO_PATCH:", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(normalized);
        }

        if (!normalized.Contains("diff --git ", StringComparison.Ordinal)
            || !normalized.Contains("--- ", StringComparison.Ordinal)
            || !normalized.Contains("+++ ", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Provider did not return a valid unified diff.");
        }

        return normalized.EndsWith('\n') ? normalized : normalized + Environment.NewLine;
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

    private async Task<string> RunRepositoryValidationAsync(
        string validationCommand,
        string repoPath,
        string secret,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string> environment)
    {
        await PrepareRepositoryValidationAsync(validationCommand, repoPath, secret, cancellationToken, environment);
        return await RunProcessAsync("/bin/bash", ["-lc", validationCommand], repoPath, secret, cancellationToken, environment);
    }

    private async Task PrepareRepositoryValidationAsync(
        string validationCommand,
        string repoPath,
        string secret,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string> environment)
    {
        if (!validationCommand.Contains("npm", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (File.Exists(Path.Combine(repoPath, "frontend", "package-lock.json")))
        {
            await RunProcessAsync("npm", ["--prefix", "frontend", "ci", "--include=dev", "--no-audit", "--no-fund"], repoPath, secret, cancellationToken, environment);
            return;
        }

        if (File.Exists(Path.Combine(repoPath, "frontend", "package.json")))
        {
            await RunProcessAsync("npm", ["--prefix", "frontend", "install", "--include=dev", "--no-audit", "--no-fund"], repoPath, secret, cancellationToken, environment);
            return;
        }

        if (File.Exists(Path.Combine(repoPath, "package-lock.json")))
        {
            await RunProcessAsync("npm", ["ci", "--include=dev", "--no-audit", "--no-fund"], repoPath, secret, cancellationToken, environment);
            return;
        }

        if (File.Exists(Path.Combine(repoPath, "package.json")))
        {
            await RunProcessAsync("npm", ["install", "--include=dev", "--no-audit", "--no-fund"], repoPath, secret, cancellationToken, environment);
        }
    }

    private static async Task<string> RunProcessAsync(
        string fileName,
        IReadOnlyCollection<string> arguments,
        string workingDirectory,
        string secret,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null,
        bool preserveOutput = false)
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

        if (preserveOutput)
        {
            return string.IsNullOrWhiteSpace(output) ? error : output;
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

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Temporary agent work directories are best-effort cleanup.
        }
    }

    private sealed record AgentPlan(
        string Summary,
        string ManagerAgentName,
        string Runtime,
        IReadOnlyCollection<string> SubAgents,
        IReadOnlyCollection<ToolIntent> ToolIntents,
        IReadOnlyCollection<string> RuntimeSkills,
        string? ProviderNotes,
        IReadOnlyCollection<string> SafetyRules);

    private sealed record AgentRuntimeContext(
        IReadOnlyCollection<string> ContextLines,
        IReadOnlyCollection<string> Sources);

    private sealed record ArtifactSource(
        string Id,
        string Label,
        string Content);

    private sealed record ReportSpec(
        string? Title,
        string? Audience,
        IReadOnlyCollection<string>? SourceIds,
        string? ConfidenceNotes,
        IReadOnlyCollection<ReportSectionSpec>? Sections);

    private sealed record ReportSectionSpec(
        string? Heading,
        string? Purpose,
        IReadOnlyCollection<string>? Bullets,
        IReadOnlyCollection<string>? EvidenceNeeded,
        IReadOnlyCollection<string>? SourceIds);

    private sealed record DeckSpec(
        string? Title,
        string? Audience,
        IReadOnlyCollection<string>? SourceIds,
        string? ConfidenceNotes,
        IReadOnlyCollection<DeckSlideSpec>? Slides);

    private sealed record DeckSlideSpec(
        string? Title,
        IReadOnlyCollection<string>? Bullets,
        string? SpeakerNotes,
        IReadOnlyCollection<string>? SourceIds);

    private sealed record CodeAdviceSpec(
        string? Title,
        string? Audience,
        IReadOnlyCollection<string>? SourceIds,
        string? ConfidenceNotes,
        IReadOnlyCollection<CodeFindingSpec>? Findings,
        IReadOnlyCollection<string>? ValidationPlan);

    private sealed record CodeFindingSpec(
        string? Title,
        string? Rationale,
        string? Recommendation,
        IReadOnlyCollection<string>? SourceIds);

    private sealed record CodePatchBuildResult(
        string Patch,
        string ValidationResult,
        string DiffSummary);

    private sealed record RepositoryPatchInstruction(
        string? FilePath,
        string? Find,
        string? Replace,
        string? Reason);

    private sealed record ToolIntent(
        string SubAgentName,
        string Action,
        string Target,
        bool RequiresApproval);

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
