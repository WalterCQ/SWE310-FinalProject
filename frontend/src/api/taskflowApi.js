import axiosClient from "./axiosClient.js";
import { translateKey } from "../i18n.jsx";

const technicalErrorPatterns = [
  /request failed with status code/i,
  /status code\s*'?[\d]+/i,
  /no connection with that id/i,
  /connection id/i,
  /hubconnection/i,
  /websocket/i,
  /negotiate/i,
  /system\./i,
  /microsoft\./i,
  /sqlexception/i,
  /exception\b/i,
  /stack trace/i,
  /object reference not set/i,
  /\bat\s+[A-Za-z0-9_.]+\(.*\)/,
];

const displayableBackendErrorPrefixes = [
  "ai provider request failed:",
  "pdf parsing failed:",
  "pinecone ",
];

const forcedTechnicalErrorPatterns = [
  /stack trace/i,
  /object reference not set/i,
  /\bat\s+[A-Za-z0-9_.]+\(.*\)/,
];

function getStatusCode(error, payload) {
  const statusCode = payload?.statusCode
    ?? error?.response?.status
    ?? error?.statusCode
    ?? 0;

  return Number(statusCode) || 0;
}

function getRequestLabel(error) {
  const method = String(error?.config?.method || "").trim().toUpperCase();
  const url = String(error?.config?.url || "").trim();

  if (!url) return "";
  return [method, url].filter(Boolean).join(" ");
}

function isTechnicalErrorMessage(message) {
  const value = String(message || "").trim();
  if (!value) return false;

  if (
    displayableBackendErrorPrefixes.some((prefix) => value.toLowerCase().startsWith(prefix))
    && !forcedTechnicalErrorPatterns.some((pattern) => pattern.test(value))
  ) {
    return false;
  }

  return technicalErrorPatterns.some((pattern) => pattern.test(value));
}

function getStatusMessage(statusCode, requestLabel = "") {
  if (statusCode === 0) return translateKey("api.unreachable");
  if (statusCode === 400 || statusCode === 422) return translateKey("api.badRequest");
  if (statusCode === 401) return translateKey("api.unauthorized");
  if (statusCode === 403) return translateKey("api.forbidden");
  if (statusCode === 404) return translateKey("api.notFound");
  if (statusCode === 409) return translateKey("api.conflict");
  if (statusCode === 413) return translateKey("api.tooLarge");
  if (statusCode === 429) return translateKey("api.tooManyRequests");
  if ((statusCode === 502 || statusCode === 503 || statusCode === 504) && requestLabel) {
    return translateKey("api.serverErrorWithEndpoint", { statusCode, endpoint: requestLabel });
  }
  if (statusCode === 502 || statusCode === 503 || statusCode === 504) return translateKey("api.serviceUnavailable");
  if (statusCode >= 500 && requestLabel) {
    return translateKey("api.serverErrorWithEndpoint", { statusCode, endpoint: requestLabel });
  }
  if (statusCode >= 500) return translateKey("api.serverError");

  return translateKey("api.requestFailed");
}

function getDisplayMessage(message, statusCode, requestLabel = "") {
  const value = String(message || "").trim();

  if (!value || isTechnicalErrorMessage(value)) {
    return getStatusMessage(statusCode, requestLabel);
  }

  return value;
}

function getDisplayErrors(errors, fallbackMessage) {
  if (!Array.isArray(errors) || errors.length === 0) {
    return [fallbackMessage];
  }

  const messages = errors
    .map((error) => String(error || "").trim())
    .filter(Boolean);

  if (messages.length === 0) {
    return [fallbackMessage];
  }

  const safeMessages = messages.filter((message) => !isTechnicalErrorMessage(message));
  return safeMessages.length > 0 ? safeMessages : [fallbackMessage];
}

export function normalizeApiError(error) {
  if (error?.request && !error?.response) {
    const normalized = new Error(translateKey("api.unreachable"));
    normalized.errors = [normalized.message];
    normalized.statusCode = 0;
    return normalized;
  }

  const responsePayload = error?.response?.data;
  const payload = responsePayload && typeof responsePayload === "object" ? responsePayload : error;
  const statusCode = getStatusCode(error, payload);
  const requestLabel = getRequestLabel(error);
  const rawMessage = payload?.message || error?.message || "";
  const message = getDisplayMessage(rawMessage, statusCode, requestLabel);
  const normalized = new Error(message);

  normalized.errors = getDisplayErrors(payload?.errors, message);
  normalized.requestLabel = requestLabel;
  normalized.statusCode = statusCode;

  return normalized;
}

export function formatApiError(error) {
  if (!error) return "";
  const normalized = error?.statusCode || error?.errors ? error : normalizeApiError(error);

  if (Array.isArray(normalized.errors) && normalized.errors.length > 0) {
    return normalized.errors.join(" ");
  }

  return normalized.message || getStatusMessage(normalized.statusCode || 0, normalized.requestLabel || "");
}

function unwrap(response) {
  const payload = response?.data;

  if (!payload || typeof payload.success === "undefined") {
    return payload;
  }

  if (payload.success) {
    return payload.data;
  }

  throw normalizeApiError(payload);
}

async function request(config) {
  try {
    const response = await axiosClient.request(config);
    return unwrap(response);
  } catch (error) {
    throw normalizeApiError(error);
  }
}

export const workspaces = {
  list: () => request({ method: "GET", url: "/api/workspaces" }),
  get: (workspaceId) => request({ method: "GET", url: `/api/workspaces/${workspaceId}` }),
  create: (payload) => request({ method: "POST", url: "/api/workspaces", data: payload }),
  update: (workspaceId, payload) => request({ method: "PUT", url: `/api/workspaces/${workspaceId}`, data: payload }),
  remove: (workspaceId) => request({ method: "DELETE", url: `/api/workspaces/${workspaceId}` }),
  members: (workspaceId) => request({ method: "GET", url: `/api/workspaces/${workspaceId}/members` }),
  addMember: (workspaceId, payload) => request({ method: "POST", url: `/api/workspaces/${workspaceId}/members`, data: payload }),
  updateMember: (workspaceId, userId, payload) => request({ method: "PUT", url: `/api/workspaces/${workspaceId}/members/${userId}`, data: payload }),
  removeMember: (workspaceId, userId) => request({ method: "DELETE", url: `/api/workspaces/${workspaceId}/members/${userId}` }),
  getAiProvider: (workspaceId) => request({ method: "GET", url: `/api/workspaces/${workspaceId}/ai/provider` }),
  saveAiProvider: (workspaceId, payload) => request({ method: "PUT", url: `/api/workspaces/${workspaceId}/ai/provider`, data: payload }),
  deleteAiProvider: (workspaceId) => request({ method: "DELETE", url: `/api/workspaces/${workspaceId}/ai/provider` }),
  githubSetup: (workspaceId) => request({ method: "GET", url: `/api/workspaces/${workspaceId}/github/setup` }),
  githubRepositories: (workspaceId) => request({ method: "GET", url: `/api/workspaces/${workspaceId}/github/repositories` }),
  completeGithubInstallation: (workspaceId, payload) => request({
    method: "POST",
    url: `/api/workspaces/${workspaceId}/github/installations/complete`,
    data: payload,
  }),
  connectGithubRepository: (workspaceId, payload) => request({
    method: "POST",
    url: `/api/workspaces/${workspaceId}/github/repositories`,
    data: payload,
  }),
  deleteGithubRepository: (workspaceId, repositoryId) => request({
    method: "DELETE",
    url: `/api/workspaces/${workspaceId}/github/repositories/${repositoryId}`,
  }),
};

export const dashboard = {
  workspace: (workspaceId) => request({ method: "GET", url: `/api/workspaces/${workspaceId}/dashboard` }),
  project: (projectId) => request({ method: "GET", url: `/api/projects/${projectId}/dashboard` }),
};

export const projects = {
  listByWorkspace: (workspaceId) => request({ method: "GET", url: `/api/workspaces/${workspaceId}/projects` }),
  create: (workspaceId, payload) => request({ method: "POST", url: `/api/workspaces/${workspaceId}/projects`, data: payload }),
  get: (projectId) => request({ method: "GET", url: `/api/projects/${projectId}` }),
  update: (projectId, payload) => request({ method: "PUT", url: `/api/projects/${projectId}`, data: payload }),
  remove: (projectId) => request({ method: "DELETE", url: `/api/projects/${projectId}` }),
  members: (projectId) => request({ method: "GET", url: `/api/projects/${projectId}/members` }),
  addMember: (projectId, payload) => request({ method: "POST", url: `/api/projects/${projectId}/members`, data: payload }),
  updateMember: (projectId, userId, payload) => request({ method: "PUT", url: `/api/projects/${projectId}/members/${userId}`, data: payload }),
  removeMember: (projectId, userId) => request({ method: "DELETE", url: `/api/projects/${projectId}/members/${userId}` }),
};

export const tasks = {
  listByProject: (projectId) => request({ method: "GET", url: `/api/projects/${projectId}/tasks` }),
  create: (projectId, payload) => request({ method: "POST", url: `/api/projects/${projectId}/tasks`, data: payload }),
  get: (taskId) => request({ method: "GET", url: `/api/tasks/${taskId}` }),
  update: (taskId, payload) => request({ method: "PUT", url: `/api/tasks/${taskId}`, data: payload }),
  remove: (taskId) => request({ method: "DELETE", url: `/api/tasks/${taskId}` }),
  updateStatus: (taskId, status) => request({ method: "PUT", url: `/api/tasks/${taskId}/status`, data: { status } }),
  assign: (taskId, assigneeId) => request({ method: "PUT", url: `/api/tasks/${taskId}/assign`, data: { assigneeId } }),
  setDeadline: (taskId, deadlineUtc) => request({ method: "PUT", url: `/api/tasks/${taskId}/deadline`, data: { deadlineUtc } }),
  comments: (taskId) => request({ method: "GET", url: `/api/tasks/${taskId}/comments` }),
  addComment: (taskId, payload) => request({ method: "POST", url: `/api/tasks/${taskId}/comments`, data: payload }),
  deleteComment: (taskId, commentId) => request({ method: "DELETE", url: `/api/tasks/${taskId}/comments/${commentId}` }),
};

export const channels = {
  listByWorkspace: (workspaceId) => request({ method: "GET", url: `/api/workspaces/${workspaceId}/channels` }),
  create: (workspaceId, payload) => request({ method: "POST", url: `/api/workspaces/${workspaceId}/channels`, data: payload }),
  messages: (channelId) => request({ method: "GET", url: `/api/channels/${channelId}/messages` }),
  members: (channelId) => request({ method: "GET", url: `/api/channels/${channelId}/members` }),
  addMember: (channelId, payload) => request({ method: "POST", url: `/api/channels/${channelId}/members`, data: payload }),
  removeMember: (channelId, userId) => request({ method: "DELETE", url: `/api/channels/${channelId}/members/${userId}` }),
  removeAttachment: (channelId, attachmentId) => request({ method: "DELETE", url: `/api/channels/${channelId}/attachments/${attachmentId}` }),
};

export const messages = {
  listByChannel: (channelId) => channels.messages(channelId),
  create: (channelId, payload) => request({ method: "POST", url: `/api/channels/${channelId}/messages`, data: payload }),
  createAttachment: (channelId, payload) => request({ method: "POST", url: `/api/channels/${channelId}/messages/attachments`, data: payload }),
  downloadAttachment: (downloadUrl) => axiosClient.request({ method: "GET", url: downloadUrl, responseType: "blob" }),
  update: (messageId, payload) => request({ method: "PUT", url: `/api/messages/${messageId}`, data: payload }),
  remove: (messageId) => request({ method: "DELETE", url: `/api/messages/${messageId}` }),
};

export const notifications = {
  list: () => request({ method: "GET", url: "/api/notifications" }),
  markRead: (notificationId) => request({ method: "PUT", url: `/api/notifications/${notificationId}/read` }),
  markAllRead: () => request({ method: "PUT", url: "/api/notifications/read-all" }),
};

export const auth = {
  login: (payload) => request({ method: "POST", url: "/api/auth/login", data: payload }),
  register: (payload) => request({ method: "POST", url: "/api/auth/register", data: payload }),
  me: () => request({ method: "GET", url: "/api/auth/me" }),
};

export const admin = {
  overview: () => request({ method: "GET", url: "/api/admin/overview" }),
  users: () => request({ method: "GET", url: "/api/admin/users" }),
  updateUser: (userId, payload) => request({ method: "PUT", url: `/api/admin/users/${userId}`, data: payload }),
  resetUserPassword: (userId, payload) => request({ method: "PUT", url: `/api/admin/users/${userId}/password`, data: payload }),
};

export const ai = {
  command: (payload) => request({ method: "POST", url: "/api/ai/command", data: payload }),
  channelCommand: (channelId, payload) => request({ method: "POST", url: `/api/channels/${channelId}/ai`, data: payload }),
  channelAttachments: (channelId) => request({ method: "GET", url: `/api/channels/${channelId}/attachments` }),
  uploadChannelAttachment: (channelId, payload) => request({ method: "POST", url: `/api/channels/${channelId}/attachments`, data: payload }),
  projectSummary: (projectId) => request({ method: "POST", url: "/api/ai/project-summary", data: { projectId } }),
  riskAnalysis: (projectId) => request({ method: "POST", url: "/api/ai/risk-analysis", data: { projectId } }),
  providers: () => request({ method: "GET", url: "/api/ai/providers" }),
  saveProvider: (payload) => request({ method: "POST", url: "/api/ai/providers", data: payload }),
};

export const agent = {
  createJob: (payload) => request({ method: "POST", url: "/api/agent/jobs", data: payload }),
  getJob: (jobId) => request({ method: "GET", url: `/api/agent/jobs/${jobId}` }),
  events: (jobId, sinceUtc) => request({
    method: "GET",
    url: `/api/agent/jobs/${jobId}/events`,
    params: sinceUtc ? { sinceUtc } : undefined,
  }),
  cancel: (jobId) => request({ method: "POST", url: `/api/agent/jobs/${jobId}/cancel` }),
  approve: (approvalId, note = "") => request({
    method: "POST",
    url: `/api/agent/approvals/${approvalId}/approve`,
    data: { note },
  }),
  reject: (approvalId, note = "") => request({
    method: "POST",
    url: `/api/agent/approvals/${approvalId}/reject`,
    data: { note },
  }),
  downloadArtifact: (artifactId) => axiosClient.request({
    method: "GET",
    url: `/api/agent/artifacts/${artifactId}/download`,
    responseType: "blob",
  }),
};

export const github = {
  setup: (workspaceId) => workspaces.githubSetup(workspaceId),
  listRepositories: (workspaceId) => workspaces.githubRepositories(workspaceId),
  completeInstallation: (workspaceId, payload) => workspaces.completeGithubInstallation(workspaceId, payload),
  connectRepository: (workspaceId, payload) => workspaces.connectGithubRepository(workspaceId, payload),
  deleteRepository: (workspaceId, repositoryId) => workspaces.deleteGithubRepository(workspaceId, repositoryId),
};

export const getProjectSummary = ai.projectSummary;
export const getRiskAnalysis = ai.riskAnalysis;

export default {
  workspaces,
  dashboard,
  projects,
  tasks,
  channels,
  messages,
  notifications,
  auth,
  admin,
  ai,
  agent,
  github,
};
