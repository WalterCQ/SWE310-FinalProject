using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using TaskFlow.Api.DTOs.Admin;
using TaskFlow.Api.DTOs.AI;
using TaskFlow.Api.DTOs.Agent;
using TaskFlow.Api.DTOs.Auth;
using TaskFlow.Api.DTOs.Channels;
using TaskFlow.Api.DTOs.Dashboard;
using TaskFlow.Api.DTOs.Messages;
using TaskFlow.Api.DTOs.Notifications;
using TaskFlow.Api.DTOs.Projects;
using TaskFlow.Api.DTOs.Tasks;
using TaskFlow.Api.DTOs.Workspaces;
using TaskFlow.Api.Helpers;

namespace TaskFlow.Api.Swagger;

public class ApiDocumentationOperationFilter : IOperationFilter
{
    private static readonly Type ErrorResponseType = typeof(ApiResponse<object>);

    private static readonly IReadOnlyDictionary<string, EndpointDoc> Docs =
        new Dictionary<string, EndpointDoc>(StringComparer.OrdinalIgnoreCase)
        {
            ["Admin.GetOverview"] = new(
                "Get administrator-only system overview",
                "Returns aggregate system counts and security evidence for the coursework demo. This endpoint requires an Administrator JWT role claim and re-checks the current database role.",
                StatusCodes.Status200OK,
                typeof(AdminOverviewResponse)),
            ["Admin.GetUsers"] = new(
                "List user accounts for administrators",
                "Returns registered users, global roles, account creation date, and membership counts. This endpoint requires an Administrator JWT role claim and re-checks the current database role.",
                StatusCodes.Status200OK,
                typeof(IReadOnlyCollection<AdminUserResponse>)),
            ["Admin.UpdateUser"] = new(
                "Update a user account",
                "Allows a global Administrator to update a user's display name and global role. The endpoint prevents changing the current administrator's own global role and prevents removing the final global Administrator.",
                StatusCodes.Status200OK,
                typeof(AdminUserResponse)),
            ["Admin.ResetUserPassword"] = new(
                "Reset a user password",
                "Allows a global Administrator to replace a user's password hash with a new password that passes backend validation. Raw passwords are never returned.",
                StatusCodes.Status200OK,
                typeof(object)),

            ["Auth.Register"] = new(
                "Register a new user",
                "Creates a user account after validating name, email, and password. Duplicate email addresses return 400. Newly registered users are granted access to the shared demo workspace when seeded demo data is available.",
                StatusCodes.Status200OK,
                typeof(object)),
            ["Auth.Login"] = new(
                "Log in and receive a JWT",
                "Authenticates an email and password against the stored password hash. A valid login returns a short-lived Bearer token plus the user's id, name, email, and global role.",
                StatusCodes.Status200OK,
                typeof(AuthResponse)),
            ["Auth.Me"] = new(
                "Get the current user profile",
                "Reads the authenticated Bearer token, verifies that the user still exists, and returns the current user's profile and global role from the database.",
                StatusCodes.Status200OK,
                typeof(CurrentUserResponse)),

            ["Ai.ExecuteCommand"] = new(
                "Execute an AI collaboration command",
                "Runs a natural-language command inside a workspace. The Semantic Kernel plugin can read or modify real collaboration data only when the current user has workspace access. Requires a workspace AI provider configured by a workspace administrator or manager.",
                StatusCodes.Status200OK,
                typeof(AiResponse)),
            ["Ai.SummarizeChannel"] = new(
                "Summarize recent channel messages",
                "Reads the latest channel messages that the current user can access and asks the workspace AI provider to summarize them.",
                StatusCodes.Status200OK,
                typeof(AiResponse)),
            ["Ai.SummarizeProject"] = new(
                "Summarize project progress",
                "Uses the project dashboard data, including task counts, overdue tasks, and completion rate, to produce a stakeholder summary through the workspace AI provider.",
                StatusCodes.Status200OK,
                typeof(AiResponse)),
            ["Ai.AnalyzeProjectRisk"] = new(
                "Analyze project delivery risk",
                "Uses project dashboard metrics and the workspace AI provider to identify overdue-task and completion-rate risks.",
                StatusCodes.Status200OK,
                typeof(AiResponse)),
            ["Ai.GenerateTasksFromMessage"] = new(
                "Generate task suggestions from a message",
                "Turns message content into concise task suggestions for a project through the workspace AI provider. The current user must be allowed to create tasks in the project.",
                StatusCodes.Status200OK,
                typeof(AiResponse)),
            ["Ai.AskWorkspaceKnowledge"] = new(
                "Ask a retrieval-grounded workspace question",
                "Retrieves relevant workspace records from projects, tasks, and accessible channel messages, adds them as grounded context, then asks the workspace AI provider to answer only from that context. This is a lightweight RAG-style endpoint that avoids extra vector database dependencies while keeping private channel messages permission-filtered.",
                StatusCodes.Status200OK,
                typeof(AiResponse)),

            ["AiProviders.GetProviders"] = new(
                "List configured AI providers",
                "Returns the current user's saved AI provider credentials with provider name, model, base URL, tool-call support, default-provider flag, and HasApiKey. Secret API key values are never returned.",
                StatusCodes.Status200OK,
                typeof(IReadOnlyCollection<AiProviderResponse>)),
            ["AiProviders.SaveProvider"] = new(
                "Save an AI provider credential",
                "Creates or updates an AI provider credential after validating provider name, model, and API key. The API key is stored server-side and the response only reports whether a key exists.",
                StatusCodes.Status200OK,
                typeof(AiProviderResponse)),
            ["AiProviders.GetWorkspaceProvider"] = new(
                "Get workspace AI provider metadata",
                "Returns provider metadata for a workspace. Only workspace administrators, workspace managers, and global administrators can call this endpoint. Secret API key values are never returned.",
                StatusCodes.Status200OK,
                typeof(WorkspaceAiProviderResponse)),
            ["AiProviders.SaveWorkspaceProvider"] = new(
                "Save a workspace AI provider",
                "Creates or updates the workspace-level AI provider after validating the allowed HTTPS base URL. The API key is encrypted server-side and never returned.",
                StatusCodes.Status200OK,
                typeof(WorkspaceAiProviderResponse)),
            ["AiProviders.DeleteWorkspaceProvider"] = new(
                "Delete a workspace AI provider",
                "Deletes the workspace-level AI provider credential. Only workspace administrators, workspace managers, and global administrators can remove it.",
                StatusCodes.Status200OK,
                typeof(bool)),

            ["Agent.CreateJob"] = new(
                "Create an agent job",
                "Starts a multi-step AI agent job for a workspace goal. The current user must have workspace access. Agent planning uses the workspace AI provider unless a legacy user provider credential id is supplied explicitly.",
                StatusCodes.Status200OK,
                typeof(AgentJobResponse)),
            ["Agent.GetJob"] = new(
                "Get an agent job",
                "Returns the current state of an agent job, including steps, approval requests, generated artifacts, status timestamps, and any error message.",
                StatusCodes.Status200OK,
                typeof(AgentJobResponse)),
            ["Agent.GetJobEvents"] = new(
                "List agent job events",
                "Returns timeline events for an agent job. The optional sinceUtc query parameter filters events created after the supplied UTC timestamp.",
                StatusCodes.Status200OK,
                typeof(IReadOnlyCollection<AgentEventResponse>)),
            ["Agent.CancelJob"] = new(
                "Cancel an agent job",
                "Requests cancellation for a queued or running agent job and returns the updated job state. Completed jobs remain unchanged.",
                StatusCodes.Status200OK,
                typeof(AgentJobResponse)),
            ["Agent.ApproveApproval"] = new(
                "Approve an agent action",
                "Approves a pending agent approval request, optionally recording a short decision note. The server then allows the approved action to proceed.",
                StatusCodes.Status200OK,
                typeof(AgentApprovalResponse)),
            ["Agent.RejectApproval"] = new(
                "Reject an agent action",
                "Rejects a pending agent approval request, optionally recording a short decision note. The rejected action is not executed.",
                StatusCodes.Status200OK,
                typeof(AgentApprovalResponse)),

            ["Workspaces.GetWorkspaces"] = new(
                "List accessible workspaces",
                "Returns workspaces visible to the current user. Global administrators can see all workspaces; standard members only see workspaces where they are members.",
                StatusCodes.Status200OK,
                typeof(IEnumerable<WorkspaceResponse>)),
            ["Workspaces.CreateWorkspace"] = new(
                "Create a workspace",
                "Creates a workspace after validating the required name and optional description length. Requires the current user to hold the workspace Manager role in at least one existing workspace. The current user is automatically added as the new workspace administrator.",
                StatusCodes.Status201Created,
                typeof(WorkspaceResponse)),
            ["Workspaces.GetWorkspace"] = new(
                "Get workspace details",
                "Returns one workspace with member, channel, and project counts. A missing workspace or inaccessible workspace is returned as 404 to avoid leaking private IDs.",
                StatusCodes.Status200OK,
                typeof(WorkspaceResponse)),
            ["Workspaces.UpdateWorkspace"] = new(
                "Update workspace details",
                "Updates workspace name and description after backend validation. Only workspace administrators, workspace managers, or global administrators can update it.",
                StatusCodes.Status200OK,
                typeof(WorkspaceResponse)),
            ["Workspaces.DeleteWorkspace"] = new(
                "Delete a workspace",
                "Deletes a workspace and its related projects, channels, tasks, memberships, and workspace-scoped AI context. Requires the current user to hold the workspace Manager role in that workspace; workspace Administrator alone and global Administrator alone cannot delete it.",
                StatusCodes.Status200OK,
                typeof(bool)),
            ["Workspaces.GetWorkspaceMembers"] = new(
                "List workspace members",
                "Returns the users who belong to a workspace, including each user's workspace role and join timestamp. Requires workspace access.",
                StatusCodes.Status200OK,
                typeof(IEnumerable<WorkspaceMemberResponse>)),
            ["Workspaces.AddWorkspaceMember"] = new(
                "Add a workspace member",
                "Adds an existing registered user to a workspace by email and assigns Administrator, Manager, or Member role. Requires workspace management permission.",
                StatusCodes.Status201Created,
                typeof(WorkspaceMemberResponse)),
            ["Workspaces.UpdateWorkspaceMemberRole"] = new(
                "Update workspace member role",
                "Changes a workspace member's role. Requires workspace management permission and prevents demoting the last workspace administrator.",
                StatusCodes.Status200OK,
                typeof(WorkspaceMemberResponse)),
            ["Workspaces.RemoveWorkspaceMember"] = new(
                "Remove a workspace member",
                "Removes a user from the workspace, removes their project and channel memberships in that workspace, and clears matching task assignments. Requires workspace management permission and prevents removing the last workspace administrator or the only project administrator for any project.",
                StatusCodes.Status200OK,
                typeof(bool)),

            ["Channels.GetWorkspaceChannels"] = new(
                "List workspace channels",
                "Returns channels in a workspace that the current user can access. Private channels are included only when the current user is a channel member.",
                StatusCodes.Status200OK,
                typeof(IEnumerable<ChannelResponse>)),
            ["Channels.CreateChannel"] = new(
                "Create a channel",
                "Creates a public or private channel inside a workspace. Requires workspace management permission and validates the channel name and description length.",
                StatusCodes.Status201Created,
                typeof(ChannelResponse)),
            ["Channels.GetChannelMessages"] = new(
                "List channel messages",
                "Returns up to 200 messages from a channel in chronological order. Private-channel access is enforced before messages are returned.",
                StatusCodes.Status200OK,
                typeof(IEnumerable<MessageResponse>)),
            ["Channels.GetChannelAttachments"] = new(
                "List channel AI attachments",
                "Returns AI-indexed files uploaded to a channel. The current user must be allowed to access the channel before attachment metadata is returned.",
                StatusCodes.Status200OK,
                typeof(IReadOnlyCollection<ChannelAttachmentResponse>)),
            ["Channels.UploadChannelAttachment"] = new(
                "Upload and index a channel attachment",
                "Uploads a PDF, Word, text, markdown, or image attachment for channel AI context. The backend validates channel access, file presence, file size, supported content type, extracted text, and workspace AI provider configuration before storing searchable chunks.",
                StatusCodes.Status200OK,
                typeof(ChannelAttachmentResponse)),
            ["Channels.RunChannelAi"] = new(
                "Run a channel AI command",
                "Executes an @TaskFlow AI command against recent channel messages and optional indexed attachment context. Requires channel access and workspace AI permission, then returns a preview with grounded sources, suggested tasks, or generated artifact content without posting it into the channel.",
                StatusCodes.Status200OK,
                typeof(AiChannelCommandResponse)),
            ["Channels.ShareChannelAi"] = new(
                "Share an approved channel AI result",
                "Posts a previously previewed TaskFlow AI result into the channel only after user confirmation. Report and PPT previews are converted into downloadable DOCX or PPTX attachments at this approval step.",
                StatusCodes.Status200OK,
                typeof(AiChannelCommandResponse)),
            ["Channels.GetChannelMembers"] = new(
                "List channel members",
                "Returns the users who belong to a channel. Requires channel access; private channel access is enforced before members are returned.",
                StatusCodes.Status200OK,
                typeof(IEnumerable<ChannelMemberResponse>)),
            ["Channels.AddChannelMember"] = new(
                "Add a channel member",
                "Adds an existing registered workspace member to a channel by email. Requires workspace management permission.",
                StatusCodes.Status201Created,
                typeof(ChannelMemberResponse)),
            ["Channels.RemoveChannelMember"] = new(
                "Remove a channel member",
                "Removes a user from a channel. Requires workspace management permission for the channel's workspace.",
                StatusCodes.Status200OK,
                typeof(bool)),

            ["Messages.CreateMessage"] = new(
                "Send a channel message",
                "Creates a message in a channel after validating that Content is present and no longer than 4000 characters. The sender is the current user.",
                StatusCodes.Status201Created,
                typeof(MessageResponse)),
            ["Messages.UpdateMessage"] = new(
                "Edit a message",
                "Updates message content and sets EditedAtUtc. The sender can edit their own message; workspace managers can also edit messages in their workspace.",
                StatusCodes.Status200OK,
                typeof(MessageResponse)),
            ["Messages.DeleteMessage"] = new(
                "Delete a message",
                "Soft-deletes a message by marking it deleted and replacing its content with [deleted]. The sender or a workspace manager can delete it.",
                StatusCodes.Status200OK,
                typeof(bool)),

            ["Projects.GetWorkspaceProjects"] = new(
                "List workspace projects",
                "Returns projects in a workspace that the current user can access, including member and task counts.",
                StatusCodes.Status200OK,
                typeof(IEnumerable<ProjectResponse>)),
            ["Projects.CreateProject"] = new(
                "Create a project",
                "Creates a project under a workspace after validating the name, description, and optional deadline. The creator becomes the project administrator.",
                StatusCodes.Status201Created,
                typeof(ProjectResponse)),
            ["Projects.GetProject"] = new(
                "Get project details",
                "Returns one project with status, deadline, member count, task count, and completed task count. Inaccessible projects are returned as 404.",
                StatusCodes.Status200OK,
                typeof(ProjectResponse)),
            ["Projects.UpdateProject"] = new(
                "Update a project",
                "Updates project name, description, status, and deadline. Requires project management permission or workspace management permission.",
                StatusCodes.Status200OK,
                typeof(ProjectResponse)),
            ["Projects.DeleteProject"] = new(
                "Delete a project",
                "Deletes a project and its related data through EF Core relationships. Requires project or workspace management permission.",
                StatusCodes.Status200OK,
                typeof(bool)),
            ["Projects.GetProjectMembers"] = new(
                "List project members",
                "Returns project members with their project role and join timestamp. Requires project access.",
                StatusCodes.Status200OK,
                typeof(IEnumerable<ProjectMemberResponse>)),
            ["Projects.AddProjectMember"] = new(
                "Add a project member",
                "Adds an existing registered workspace member to a project by email and assigns Administrator, Manager, or Member role. Requires project management permission.",
                StatusCodes.Status201Created,
                typeof(ProjectMemberResponse)),
            ["Projects.UpdateProjectMemberRole"] = new(
                "Update project member role",
                "Changes a project member's role. Requires project management permission and prevents demoting the last project administrator.",
                StatusCodes.Status200OK,
                typeof(ProjectMemberResponse)),
            ["Projects.RemoveProjectMember"] = new(
                "Remove a project member",
                "Removes a user from a project and clears their task assignments in that project. Requires project management permission and prevents removing the last project administrator.",
                StatusCodes.Status200OK,
                typeof(bool)),

            ["Tasks.GetProjectTasks"] = new(
                "List project tasks",
                "Returns tasks in a project ordered by status and deadline. Access is based on project/workspace membership.",
                StatusCodes.Status200OK,
                typeof(IEnumerable<TaskResponse>)),
            ["Tasks.CreateTask"] = new(
                "Create a task",
                "Creates a task after validating title, description, priority, optional assignee, and optional deadline. Requires project management permission through workspace Administrator/Manager, global Administrator, or project administrator role. The assignee must already be a project member.",
                StatusCodes.Status201Created,
                typeof(TaskResponse)),
            ["Tasks.GetTask"] = new(
                "Get task details",
                "Returns a single task, including status, priority, assignee, deadline, and completion timestamp. Inaccessible tasks are returned as 404.",
                StatusCodes.Status200OK,
                typeof(TaskResponse)),
            ["Tasks.UpdateTask"] = new(
                "Update a task",
                "Updates task title, description, priority, assignee, and deadline. Requires project management permission through workspace Administrator/Manager, global Administrator, or project administrator role.",
                StatusCodes.Status200OK,
                typeof(TaskResponse)),
            ["Tasks.DeleteTask"] = new(
                "Delete a task",
                "Deletes a task when the current user has project management permission.",
                StatusCodes.Status200OK,
                typeof(bool)),
            ["Tasks.UpdateTaskStatus"] = new(
                "Update task status",
                "Changes the task status to Todo, InProgress, Blocked, or Done. When set to Done, CompletedAtUtc is recorded; otherwise it is cleared.",
                StatusCodes.Status200OK,
                typeof(TaskResponse)),
            ["Tasks.AssignTask"] = new(
                "Assign or unassign a task",
                "Sets AssigneeId or clears it when null. The assignee must be a project member.",
                StatusCodes.Status200OK,
                typeof(TaskResponse)),
            ["Tasks.SetTaskDeadline"] = new(
                "Set or clear task deadline",
                "Updates DeadlineUtc or clears it when null. Requires project management permission.",
                StatusCodes.Status200OK,
                typeof(TaskResponse)),
            ["Tasks.GetTaskComments"] = new(
                "List task comments",
                "Returns task comments in chronological order, including author name and creation timestamp. Requires access to the task's project.",
                StatusCodes.Status200OK,
                typeof(IEnumerable<TaskCommentResponse>)),
            ["Tasks.AddTaskComment"] = new(
                "Add a task comment",
                "Adds a comment to a task and updates the task timestamp. Requires project management permission.",
                StatusCodes.Status201Created,
                typeof(TaskCommentResponse)),
            ["Tasks.DeleteTaskComment"] = new(
                "Delete a task comment",
                "Deletes a task comment. Requires project management permission.",
                StatusCodes.Status200OK,
                typeof(bool)),

            ["Dashboard.GetWorkspaceDashboard"] = new(
                "Get workspace dashboard metrics",
                "Returns project count, channel count, total tasks, completed tasks, overdue tasks, task status distribution, and recent activity labels for a workspace.",
                StatusCodes.Status200OK,
                typeof(WorkspaceDashboardResponse)),
            ["Dashboard.GetProjectDashboard"] = new(
                "Get project dashboard metrics",
                "Returns total tasks, completed tasks, overdue tasks, completion rate, deadline, and task status distribution for one project.",
                StatusCodes.Status200OK,
                typeof(ProjectDashboardResponse)),

            ["Notifications.GetNotifications"] = new(
                "List my notifications",
                "Returns the current user's latest notifications, newest first, after pruning historical duplicate rows for identical notifications.",
                StatusCodes.Status200OK,
                typeof(IEnumerable<NotificationResponse>)),
            ["Notifications.MarkAsRead"] = new(
                "Mark a notification as read",
                "Marks one notification as read only when it belongs to the current user.",
                StatusCodes.Status200OK,
                typeof(NotificationResponse)),
            ["Notifications.MarkAllAsRead"] = new(
                "Mark all notifications as read",
                "Marks every unread notification owned by the current user as read and returns the number of updated records.",
                StatusCodes.Status200OK,
                typeof(int))
        };

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var key = GetKey(context);
        if (!Docs.TryGetValue(key, out var doc))
        {
            return;
        }

        operation.OperationId = key.Replace(".", "_", StringComparison.Ordinal);
        operation.Summary = doc.Summary;
        operation.Description = doc.Description + Environment.NewLine + Environment.NewLine
            + "Validation and error format: responses use ApiResponse<T> with Success, Message, Data, Errors, and StatusCode. Data annotation validation covers required fields, string length limits, enum values, and route GUIDs.";

        operation.Responses ??= new OpenApiResponses();
        operation.Responses.Clear();
        operation.Responses.Add(doc.SuccessStatusCode.ToString(), CreateResponse(doc.SuccessDescription, doc.DataType, context));
        operation.Responses.Add("400", CreateResponse("Validation failed. Check required fields, string length limits, enum values, email format, GUID route values, and business rules such as assignee project membership or duplicate membership.", ErrorResponseType, context));
        operation.Responses.Add("401", CreateResponse("Authentication is required. Use a valid Bearer JWT unless the local development fallback is explicitly enabled.", ErrorResponseType, context));
        operation.Responses.Add("403", CreateResponse("The current user is authenticated but does not have the required workspace, project, channel, or task permission.", ErrorResponseType, context));
        operation.Responses.Add("404", CreateResponse("The resource was not found or is intentionally hidden because the current user cannot access it.", ErrorResponseType, context));
    }

    private static string GetKey(OperationFilterContext context)
    {
        var controller = context.MethodInfo.DeclaringType?.Name.Replace("Controller", string.Empty, StringComparison.Ordinal) ?? "Unknown";
        return $"{controller}.{context.MethodInfo.Name}";
    }

    private static OpenApiResponse CreateResponse(string description, Type dataType, OperationFilterContext context)
    {
        var responseType = dataType.IsGenericType && dataType.GetGenericTypeDefinition() == typeof(ApiResponse<>)
            ? dataType
            : typeof(ApiResponse<>).MakeGenericType(dataType);

        return new OpenApiResponse
        {
            Description = description,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new()
                {
                    Schema = context.SchemaGenerator.GenerateSchema(responseType, context.SchemaRepository)
                }
            }
        };
    }

    private sealed record EndpointDoc(
        string Summary,
        string Description,
        int SuccessStatusCode,
        Type DataType)
    {
        public string SuccessDescription =>
            SuccessStatusCode == StatusCodes.Status201Created
                ? "Created successfully."
                : "Completed successfully.";
    }
}
