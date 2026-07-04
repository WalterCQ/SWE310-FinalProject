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
import { useI18n } from "../i18n.jsx";

const navItems = [
  { labelKey: "nav.dashboard", path: "/dashboard", icon: LayoutDashboard },
  { labelKey: "nav.workspaces", path: "/workspaces", icon: Boxes },
  { labelKey: "nav.channels", path: "/channels", icon: MessageSquare },
  { labelKey: "nav.projects", path: "/projects", icon: KanbanSquare },
  { labelKey: "nav.tasks", path: "/tasks", icon: SquareCheckBig },
  { labelKey: "nav.aiAssistant", path: "/ai-assistant", icon: Bot },
  { labelKey: "nav.notifications", path: "/notifications", icon: Bell },
];

export default function Sidebar({ user }) {
  const navigate = useNavigate();
  const { t } = useI18n();
  const userName = user?.name || localStorage.getItem("userName") || t("app.user.default");
  const userRole = user?.globalRole || user?.role || localStorage.getItem("userRole") || t("app.role.default");
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
          <span>{t("app.brand.projectBoard")}</span>
        </div>
      </div>

      <nav className="nav-list" aria-label={t("nav.label")}>
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
              <span>{t(item.labelKey)}</span>
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
          <LogOut size={16} /> {t("nav.logout")}
        </button>
      </div>
    </aside>
  );
}
