const taskStatusLabels = {
  0: "To Do",
  todo: "To Do",
  "to do": "To Do",
  1: "In Progress",
  inprogress: "In Progress",
  "in progress": "In Progress",
  2: "Blocked",
  blocked: "Blocked",
  3: "Done",
  done: "Done",
};

const priorityLabels = {
  0: "Low",
  low: "Low",
  1: "Medium",
  medium: "Medium",
  2: "High",
  high: "High",
};

const projectStatusLabels = {
  0: "Planned",
  planned: "Planned",
  1: "Active",
  active: "Active",
  2: "Completed",
  completed: "Completed",
  3: "Archived",
  archived: "Archived",
};

const notificationTypeLabels = {
  0: "General",
  general: "General",
  1: "Message",
  message: "Message",
  2: "Task",
  task: "Task",
  3: "Reminder",
  reminder: "Reminder",
  4: "AI",
  ai: "AI",
};

function enumKey(value) {
  if (value === null || typeof value === "undefined") return "";
  return String(value).trim().toLowerCase();
}

export function mapTaskStatus(value) {
  return taskStatusLabels[enumKey(value)] || "To Do";
}

export function mapPriority(value) {
  return priorityLabels[enumKey(value)] || "Medium";
}

export function mapProjectStatus(value) {
  return projectStatusLabels[enumKey(value)] || "Planned";
}

export function mapNotificationType(value) {
  return notificationTypeLabels[enumKey(value)] || "General";
}

export function formatDeadline(deadlineUtc) {
  if (!deadlineUtc) return "No deadline";

  const date = new Date(deadlineUtc);
  if (Number.isNaN(date.getTime())) return "No deadline";

  return date.toLocaleDateString("en-GB", {
    day: "numeric",
    month: "short",
    year: "numeric",
  });
}

export function formatDateTime(value) {
  if (!value) return "";

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "";

  return date.toLocaleString("en-GB", {
    day: "numeric",
    month: "short",
    hour: "2-digit",
    minute: "2-digit",
  });
}

export function calculateProjectProgress(project) {
  const taskCount = Number(project?.taskCount || 0);
  const completedTaskCount = Number(project?.completedTaskCount || 0);

  if (taskCount <= 0) return 0;
  return Math.round((completedTaskCount / taskCount) * 100);
}

export function asArray(value) {
  if (Array.isArray(value)) return value;
  if (Array.isArray(value?.items)) return value.items;
  if (Array.isArray(value?.data)) return value.data;
  return [];
}

export function selectPrimaryWorkspace(workspaces) {
  const items = asArray(workspaces);
  return items.find((workspace) => workspace.name === "TaskFlow Demo Workspace")
    || items.find((workspace) => Number(workspace.projects || workspace.projectCount || 0) > 0)
    || items.find((workspace) => Number(workspace.channels || workspace.channelCount || 0) > 0)
    || items[0];
}

export function shortId(value) {
  if (!value) return "Unassigned";
  return String(value).slice(0, 8);
}

export function mapWorkspace(workspace) {
  return {
    ...workspace,
    id: workspace.id,
    name: workspace.name || "Untitled workspace",
    description: workspace.description || "",
    members: workspace.memberCount ?? workspace.members ?? 0,
    projects: workspace.projectCount ?? workspace.projects ?? 0,
    channels: workspace.channelCount ?? workspace.channels ?? 0,
    createdAt: formatDateTime(workspace.createdAtUtc),
  };
}

export function mapProject(project) {
  const status = typeof project.status === "undefined" ? 0 : project.status;

  return {
    ...project,
    id: project.id,
    name: project.name || "Untitled project",
    status,
    statusLabel: mapProjectStatus(status),
    progress: calculateProjectProgress(project),
    owner: project.ownerName || project.createdByUserName || shortId(project.createdByUserId),
    deadlineLabel: formatDeadline(project.deadlineUtc),
    taskCount: project.taskCount ?? 0,
    completedTaskCount: project.completedTaskCount ?? 0,
  };
}

export function mapTask(task, projectLookup = {}) {
  const projectId = task.projectId;
  const projectName = projectLookup[projectId]?.name || task.projectName || "Unknown project";
  const status = typeof task.status === "undefined" ? 0 : task.status;
  const priority = typeof task.priority === "undefined" ? 1 : task.priority;

  return {
    ...task,
    id: task.id,
    title: task.title || "Untitled task",
    description: task.description || "No description provided.",
    status,
    statusLabel: mapTaskStatus(status),
    priority,
    priorityLabel: mapPriority(priority),
    project: projectName,
    deadlineLabel: formatDeadline(task.deadlineUtc),
    assignee: task.assigneeName || shortId(task.assigneeId),
  };
}

export function mapChannel(channel) {
  return {
    ...channel,
    id: channel.id,
    name: channel.name || "general",
    description: channel.description || "",
    memberCount: channel.memberCount ?? 0,
    createdAt: formatDateTime(channel.createdAtUtc),
  };
}

export function mapMessage(message) {
  return {
    ...message,
    id: message.id,
    sender: message.senderName || shortId(message.senderId),
    text: message.content || "",
    time: formatDateTime(message.createdAtUtc),
  };
}

export function mapNotification(notification) {
  const type = typeof notification.type === "undefined" ? 0 : notification.type;

  return {
    ...notification,
    id: notification.id,
    title: notification.title || "Notification",
    message: notification.message || "",
    type,
    typeLabel: mapNotificationType(type),
    time: formatDateTime(notification.createdAtUtc),
  };
}

export function mapTasksByStatus(tasksByStatus = {}) {
  const counts = {
    "To Do": 0,
    "In Progress": 0,
    Blocked: 0,
    Done: 0,
  };

  Object.entries(tasksByStatus || {}).forEach(([status, count]) => {
    counts[mapTaskStatus(status)] += Number(count || 0);
  });

  return Object.entries(counts).map(([name, value]) => ({ name, value }));
}
