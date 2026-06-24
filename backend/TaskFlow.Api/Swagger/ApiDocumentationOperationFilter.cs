using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using TaskFlow.Api.DTOs.AI;
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
            ["Ai.ExecuteCommand"] = new(
                "Execute an AI collaboration command",
                "Runs a natural-language command inside a workspace. The Semantic Kernel plugin can read or modify real collaboration data only when the current user has workspace access. If AI:ApiKey is missing or the LLM call fails, the endpoint returns a deterministic fallback with UsedLlm=false.",
                StatusCodes.Status200OK,
                typeof(AiResponse)),
            ["Ai.SummarizeChannel"] = new(
                "Summarize recent channel messages",
                "Reads the latest channel messages that the current user can access and asks the LLM to summarize them. If no LLM is configured, the fallback response states the message count and latest message.",
                StatusCodes.Status200OK,
                typeof(AiResponse)),
            ["Ai.SummarizeProject"] = new(
                "Summarize project progress",
                "Uses the project dashboard data, including task counts, overdue tasks, and completion rate, to produce a stakeholder summary. Returns a deterministic dashboard summary when the LLM is unavailable.",
                StatusCodes.Status200OK,
                typeof(AiResponse)),
            ["Ai.AnalyzeProjectRisk"] = new(
                "Analyze project delivery risk",
                "Uses project dashboard metrics to identify overdue-task and completion-rate risks. If the LLM is unavailable, the fallback result still reports the risk signal from the dashboard.",
                StatusCodes.Status200OK,
                typeof(AiResponse)),
            ["Ai.GenerateTasksFromMessage"] = new(
                "Generate task suggestions from a message",
                "Turns message content into concise task suggestions for a project. The current user must be allowed to create tasks in the project. If MessageContent is empty, the fallback asks the frontend to send message content or wire MessageId lookup.",
                StatusCodes.Status200OK,
                typeof(AiResponse)),

            ["Workspaces.GetWorkspaces"] = new(
                "List accessible workspaces",
                "Returns workspaces visible to the current user. Global admins can see all workspaces; standard users only see workspaces where they are members.",
                StatusCodes.Status200OK,
                typeof(IEnumerable<WorkspaceResponse>)),
            ["Workspaces.CreateWorkspace"] = new(
                "Create a workspace",
                "Creates a workspace after validating the required name and optional description length. The current user is automatically added as the workspace owner.",
                StatusCodes.Status201Created,
                typeof(WorkspaceResponse)),
            ["Workspaces.GetWorkspace"] = new(
                "Get workspace details",
                "Returns one workspace with member, channel, and project counts. A missing workspace or inaccessible workspace is returned as 404 to avoid leaking private IDs.",
                StatusCodes.Status200OK,
                typeof(WorkspaceResponse)),
            ["Workspaces.UpdateWorkspace"] = new(
                "Update workspace details",
                "Updates workspace name and description after backend validation. Only workspace owners, workspace admins, or global admins can update it.",
                StatusCodes.Status200OK,
                typeof(WorkspaceResponse)),

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
                "Creates a project under a workspace after validating the name, description, and optional deadline. The creator becomes the project manager.",
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

            ["Tasks.GetProjectTasks"] = new(
                "List project tasks",
                "Returns tasks in a project ordered by status and deadline. Access is based on project/workspace membership.",
                StatusCodes.Status200OK,
                typeof(IEnumerable<TaskResponse>)),
            ["Tasks.CreateTask"] = new(
                "Create a task",
                "Creates a task after validating title, description, priority, optional assignee, and optional deadline. The assignee must already be a project member.",
                StatusCodes.Status201Created,
                typeof(TaskResponse)),
            ["Tasks.GetTask"] = new(
                "Get task details",
                "Returns a single task, including status, priority, assignee, deadline, and completion timestamp. Inaccessible tasks are returned as 404.",
                StatusCodes.Status200OK,
                typeof(TaskResponse)),
            ["Tasks.UpdateTask"] = new(
                "Update a task",
                "Updates task title, description, priority, assignee, and deadline. The task creator, current assignee, project manager, workspace manager, or global admin can update it.",
                StatusCodes.Status200OK,
                typeof(TaskResponse)),
            ["Tasks.DeleteTask"] = new(
                "Delete a task",
                "Deletes a task when the current user has task update permission.",
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
                "Updates DeadlineUtc or clears it when null. Requires task update permission.",
                StatusCodes.Status200OK,
                typeof(TaskResponse)),

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
                "Returns the current user's latest 100 notifications, newest first.",
                StatusCodes.Status200OK,
                typeof(IEnumerable<NotificationResponse>)),
            ["Notifications.MarkAsRead"] = new(
                "Mark a notification as read",
                "Marks one notification as read only when it belongs to the current user.",
                StatusCodes.Status200OK,
                typeof(NotificationResponse))
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
        operation.Responses.Add("400", CreateResponse("Validation failed. Check required fields, string length limits, enum values, GUID route values, and business rules such as assignee project membership.", ErrorResponseType, context));
        operation.Responses.Add("401", CreateResponse("Authentication is required. In production this should be a valid Bearer JWT; local development may use the configured fallback user.", ErrorResponseType, context));
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
