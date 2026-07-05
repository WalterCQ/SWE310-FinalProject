function roleKey(role) {
  if (role === null || typeof role === "undefined") return "";
  return String(role).trim().toLowerCase().replace(/[\s_-]/g, "");
}

export function isWorkspaceAdmin(role) {
  const key = roleKey(role);
  return key === "1" || key === "admin";
}

export function isWorkspaceOwner(role) {
  const key = roleKey(role);
  return key === "0" || key === "owner";
}

export function canManageWorkspaceMembers(role) {
  return isWorkspaceOwner(role) || isWorkspaceAdmin(role);
}

export function canDeleteWorkspace(role) {
  return isWorkspaceAdmin(role);
}

export function isProjectManager(role) {
  const key = roleKey(role);
  return key === "0" || key === "projectmanager";
}

export function canManageProjectMembers(workspaceRole, projectRole) {
  return canManageWorkspaceMembers(workspaceRole) || isProjectManager(projectRole);
}

export function canCreateProjectTask(workspaceRole, projectRole) {
  return canManageProjectMembers(workspaceRole, projectRole);
}

export function canShowTaskMutationControls({ workspaceRole, projectRole, task, currentUserId }) {
  return canManageProjectMembers(workspaceRole, projectRole);
}

export function canAddTaskComment(workspaceRole, projectRole) {
  return canManageProjectMembers(workspaceRole, projectRole);
}

export function canDeleteTaskComment({ workspaceRole, projectRole, comment, currentUserId }) {
  return canManageProjectMembers(workspaceRole, projectRole);
}
