const authKeys = ["token", "userRole", "userName", "userId"];
export const ADMINISTRATOR_ROLE = "Administrator";

export function normalizeGlobalRole(value) {
  const normalized = String(value || "").trim().toLowerCase();
  return normalized === "administrator" || normalized === "admin" ? ADMINISTRATOR_ROLE : "Member";
}

export function hasGlobalRole(value, allowedRoles = []) {
  const role = normalizeGlobalRole(value);
  return allowedRoles.map(normalizeGlobalRole).includes(role);
}

export function clearAuthStorage() {
  authKeys.forEach((key) => localStorage.removeItem(key));
}

export function storeAuthUser(user) {
  if (!user) return;

  const role = normalizeGlobalRole(user.globalRole || user.role);

  if (user.userId) localStorage.setItem("userId", user.userId);
  if (user.name) localStorage.setItem("userName", user.name);
  localStorage.setItem("userRole", role);
}

export function getStoredUser() {
  return {
    userId: localStorage.getItem("userId") || "",
    name: localStorage.getItem("userName") || "User",
    globalRole: normalizeGlobalRole(localStorage.getItem("userRole")),
  };
}
