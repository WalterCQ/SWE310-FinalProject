import axiosClient from "./axiosClient.js";
import { translateKey } from "../i18n.jsx";

export function normalizeApiError(error) {
  if (error?.request && !error?.response) {
    const normalized = new Error(translateKey("api.unreachable"));
    normalized.errors = [normalized.message];
    normalized.statusCode = 0;
    return normalized;
  }

  const responsePayload = error?.response?.data;
  const payload = responsePayload && typeof responsePayload === "object" ? responsePayload : error;
  const message = payload?.message || error?.message || translateKey("api.requestFailed");
  const normalized = new Error(message);

  normalized.errors = Array.isArray(payload?.errors) && payload.errors.length > 0
    ? payload.errors
    : [message];
  normalized.statusCode = payload?.statusCode || error?.response?.status || 0;

  return normalized;
}

export function formatApiError(error) {
  if (!error) return "";
  if (Array.isArray(error.errors) && error.errors.length > 0) {
    return error.errors.join(" ");
  }
  return error.message || translateKey("api.requestFailed");
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
  members: (workspaceId) => request({ method: "GET", url: `/api/workspaces/${workspaceId}/members` }),
  addMember: (workspaceId, payload) => request({ method: "POST", url: `/api/workspaces/${workspaceId}/members`, data: payload }),
  updateMember: (workspaceId, userId, payload) => request({ method: "PUT", url: `/api/workspaces/${workspaceId}/members/${userId}`, data: payload }),
  removeMember: (workspaceId, userId) => request({ method: "DELETE", url: `/api/workspaces/${workspaceId}/members/${userId}` }),
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
};

export const messages = {
  listByChannel: (channelId) => channels.messages(channelId),
  create: (channelId, payload) => request({ method: "POST", url: `/api/channels/${channelId}/messages`, data: payload }),
  update: (messageId, payload) => request({ method: "PUT", url: `/api/messages/${messageId}`, data: payload }),
  remove: (messageId) => request({ method: "DELETE", url: `/api/messages/${messageId}` }),
};

export const notifications = {
  list: () => request({ method: "GET", url: "/api/notifications" }),
  markRead: (notificationId) => request({ method: "PUT", url: `/api/notifications/${notificationId}/read` }),
};

export const auth = {
  login: (payload) => request({ method: "POST", url: "/api/auth/login", data: payload }),
  register: (payload) => request({ method: "POST", url: "/api/auth/register", data: payload }),
  me: () => request({ method: "GET", url: "/api/auth/me" }),
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
  ai,
  agent,
};
