import { NavLink, useNavigate } from "react-router-dom";
import {
  Bell,
  Bot,
  Boxes,
  KanbanSquare,
  LayoutDashboard,
  LogOut,
  MessageSquare,
  SquareCheckBig,
} from "lucide-react";
import { clearAuthStorage } from "../api/authStorage.js";

const navItems = [
  { label: "Dashboard", path: "/dashboard", icon: LayoutDashboard },
  { label: "Workspaces", path: "/workspaces", icon: Boxes },
  { label: "Channels", path: "/channels", icon: MessageSquare },
  { label: "Projects", path: "/projects", icon: KanbanSquare },
  { label: "Tasks", path: "/tasks", icon: SquareCheckBig },
  { label: "AI Assistant", path: "/ai-assistant", icon: Bot },
  { label: "Notifications", path: "/notifications", icon: Bell },
];

export default function Sidebar({ user }) {
  const navigate = useNavigate();
  const userName = user?.name || localStorage.getItem("userName") || "User";
  const userRole = user?.globalRole || user?.role || localStorage.getItem("userRole") || "User";
  const visibleNavItems = navItems.filter((item) => !item.allowedRoles || item.allowedRoles.includes(userRole));

  // Get initials from name (e.g. "John Doe" → "JD")
  const initials = userName
    .split(" ")
    .map((word) => word[0])
    .join("")
    .toUpperCase()
    .slice(0, 2);

  function logout() {
    clearAuthStorage();
    navigate("/login");
  }

  return (
    <aside className="sidebar">
      <div className="brand">
        <div className="brand-mark">TF</div>
        <div>
          <h1>TaskFlow</h1>
          <span>Project board</span>
        </div>
      </div>

      <nav className="nav-list" aria-label="Main navigation">
        {visibleNavItems.map((item) => {
          const Icon = item.icon;
          return (
            <NavLink
              key={item.path}
              to={item.path}
              className={({ isActive }) =>
                `nav-item ${isActive ? "active" : ""}`
              }
            >
              <Icon size={18} />
              <span>{item.label}</span>
            </NavLink>
          );
        })}
      </nav>

      <div className="sidebar-footer">
        <div className="profile-card">
          <div className="avatar">{initials}</div>
          <div>
            <strong>{userName}</strong>
            <p>{userRole}</p>
          </div>
        </div>
        <button onClick={logout} className="logout-btn">
          <LogOut size={16} /> Log out
        </button>
      </div>
    </aside>
  );
}
