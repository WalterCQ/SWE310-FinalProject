import { Navigate, Outlet, Route, Routes, useLocation } from "react-router-dom";
import { useEffect, useMemo, useState } from "react";
import Login from "./pages/Login.jsx";
import Register from "./pages/Register.jsx";
import Dashboard from "./pages/Dashboard.jsx";
import Workspaces from "./pages/Workspaces.jsx";
import Channels from "./pages/Channels.jsx";
import Projects from "./pages/Projects.jsx";
import Tasks from "./pages/Tasks.jsx";
import Notifications from "./pages/Notifications.jsx";
import Admin from "./pages/Admin.jsx";
import Sidebar from "./components/Sidebar.jsx";
import Topbar from "./components/Topbar.jsx";
import { auth } from "./api/taskflowApi.js";
import { clearAuthStorage, getStoredUser, normalizeGlobalRole, storeAuthUser } from "./api/authStorage.js";
import { useI18n } from "./i18n.jsx";
import PageRoleContext from "./pageRoleContext.jsx";

const routeMeta = {
  "/dashboard": { eyebrowKey: "dashboard.eyebrow", titleKey: "dashboard.title" },
  "/workspaces": { eyebrowKey: "workspace.eyebrow", titleKey: "workspace.title" },
  "/channels": { eyebrowKey: "channel.eyebrow", titleKey: "channel.title" },
  "/projects": { eyebrowKey: "project.eyebrow", titleKey: "project.title" },
  "/tasks": { eyebrowKey: "task.eyebrow", titleKey: "task.title" },
  "/notifications": { eyebrowKey: "notifications.eyebrow", titleKey: "notifications.title" },
  "/admin": { eyebrowKey: "admin.eyebrow", titleKey: "admin.title" },
};

const SIDEBAR_COLLAPSED_KEY = "taskflow.sidebarCollapsed";

function normalizeMemberContextRole(value, fallbackRole) {
  const normalized = String(value || "").trim().toLowerCase();
  if (normalized === "manager" || normalized === "1") return "Manager";
  if (normalized === "member" || normalized === "2") return "Member";
  return fallbackRole;
}

function getPageMeta(pathname, search, t) {
  const path = `/${pathname.split("/").filter(Boolean)[0] || "dashboard"}`;
  const query = new URLSearchParams(search);

  if (path === "/tasks" && query.get("view") === "mine") {
    return {
      eyebrow: t("task.eyebrow"),
      title: t("task.myTasksTitle"),
    };
  }

  const meta = routeMeta[path] || routeMeta["/dashboard"];

  return {
    eyebrow: t(meta.eyebrowKey),
    title: t(meta.titleKey),
  };
}

function ProtectedShell({ allowedRoles }) {
  const { t } = useI18n();
  const location = useLocation();
  const pageMeta = getPageMeta(location.pathname, location.search, t);
  const isChannelRoute = location.pathname.startsWith("/channels");
  const [authVersion, setAuthVersion] = useState(0);
  const [isSidebarCollapsed, setIsSidebarCollapsed] = useState(() => {
    return localStorage.getItem(SIDEBAR_COLLAPSED_KEY) === "true";
  });
  const [session, setSession] = useState({
    checking: true,
    user: getStoredUser(),
  });
  const [contextRole, setContextRole] = useState("");
  const pageRoleContext = useMemo(() => ({ setContextRole }), [setContextRole]);
  const token = localStorage.getItem("token");

  function toggleSidebarCollapsed() {
    setIsSidebarCollapsed((current) => {
      const next = !current;
      localStorage.setItem(SIDEBAR_COLLAPSED_KEY, String(next));
      return next;
    });
  }

  useEffect(() => {
    function handleAuthExpired() {
      setAuthVersion((version) => version + 1);
    }

    window.addEventListener("taskflow:auth-expired", handleAuthExpired);
    return () => window.removeEventListener("taskflow:auth-expired", handleAuthExpired);
  }, []);

  useEffect(() => {
    document.title = `${pageMeta.title} | TaskFlow`;
  }, [pageMeta.title]);

  useEffect(() => {
    setContextRole("");
  }, [location.pathname]);

  useEffect(() => {
    let active = true;

    async function verifySession() {
      if (!token) {
        setSession({ checking: false, user: null });
        return;
      }

      setSession((current) => ({ ...current, checking: true }));

      try {
        const user = await auth.me();
        storeAuthUser(user);
        if (active) setSession({ checking: false, user });
      } catch {
        clearAuthStorage();
        if (active) setSession({ checking: false, user: null });
      }
    }

    verifySession();

    return () => {
      active = false;
    };
  }, [token, authVersion]);

  if (!token) {
    return <Navigate to="/login" state={{ from: location }} replace />;
  }

  if (session.checking) {
    return <main className="login-page"><section className="login-card">{t("app.loading.session")}</section></main>;
  }

  if (!session.user) {
    return <Navigate to="/login" state={{ from: location }} replace />;
  }

  const userRole = normalizeGlobalRole(session.user.globalRole || session.user.role);
  const sidebarRole = userRole === "Administrator"
    ? userRole
    : normalizeMemberContextRole(contextRole, userRole);

  if (allowedRoles && !allowedRoles.includes(userRole)) {
    return <Navigate to="/dashboard" replace />;
  }

  return (
    <div className={`app-shell ${isSidebarCollapsed ? "sidebar-collapsed" : ""}`}>
      <Sidebar
        collapsed={isSidebarCollapsed}
        onToggleCollapsed={toggleSidebarCollapsed}
        user={session.user}
        displayRole={sidebarRole}
      />
      <main className={`main-area ${isChannelRoute ? "channel-main-area" : ""}`}>
        <Topbar pageMeta={pageMeta} />
        <section className={`page-content ${isChannelRoute ? "channel-page-content" : ""}`}>
          <PageRoleContext.Provider value={pageRoleContext}>
            <Outlet />
          </PageRoleContext.Provider>
        </section>
      </main>
    </div>
  );
}

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<Login />} />
      <Route path="/register" element={<Register />} />
      <Route path="/" element={<Navigate to="/dashboard" replace />} />

      <Route element={<ProtectedShell />}>
        <Route path="/dashboard" element={<Dashboard />} />
        <Route path="/workspaces" element={<Workspaces />} />
        <Route path="/channels" element={<Channels />} />
        <Route path="/projects" element={<Projects />} />
        <Route path="/tasks" element={<Tasks />} />
        <Route path="/notifications" element={<Notifications />} />
      </Route>
      <Route element={<ProtectedShell allowedRoles={["Administrator"]} />}>
        <Route path="/admin" element={<Admin />} />
      </Route>
      <Route path="*" element={<Navigate to="/dashboard" replace />} />
    </Routes>
  );
}
