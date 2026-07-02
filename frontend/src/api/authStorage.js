const authKeys = ["token", "userRole", "userName", "userId"];

export function clearAuthStorage() {
  authKeys.forEach((key) => localStorage.removeItem(key));
}

export function storeAuthUser(user) {
  if (!user) return;

  const role = user.globalRole || user.role;

  if (user.userId) localStorage.setItem("userId", user.userId);
  if (user.name) localStorage.setItem("userName", user.name);
  if (role) localStorage.setItem("userRole", role);
}

export function getStoredUser() {
  return {
    userId: localStorage.getItem("userId") || "",
    name: localStorage.getItem("userName") || "User",
    globalRole: localStorage.getItem("userRole") || "User",
  };
}
