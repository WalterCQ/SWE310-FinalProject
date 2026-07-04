import { Navigate, Outlet, Route, Routes, useLocation } from "react-router-dom";
import { useEffect, useState } from "react";
import Login from "./pages/Login.jsx";
import Register from "./pages/Register.jsx";
import Dashboard from "./pages/Dashboard.jsx";
import Workspaces from "./pages/Workspaces.jsx";
import Channels from "./pages/Channels.jsx";
import Projects from "./pages/Projects.jsx";
import Tasks from "./pages/Tasks.jsx";
import Notifications from "./pages/Notifications.jsx";
import Sidebar from "./components/Sidebar.jsx";
import Topbar from "./components/Topbar.jsx";
import { auth } from "./api/taskflowApi.js";
import { clearAuthStorage, getStoredUser, storeAuthUser } from "./api/authStorage.js";
import { useI18n } from "./i18n.jsx";

const routeMeta = {
  "/dashboard": { eyebrowKey: "dashboard.eyebrow", titleKey: "dashboard.title" },
  "/workspaces": { eyebrowKey: "workspace.eyebrow", titleKey: "workspace.title" },
  "/channels": { eyebrowKey: "channel.eyebrow", titleKey: "channel.title" },
  "/projects": { eyebrowKey: "project.eyebrow", titleKey: "project.title" },
  "/tasks": { eyebrowKey: "task.eyebrow", titleKey: "task.title" },
  "/notifications": { eyebrowKey: "notifications.eyebrow", titleKey: "notifications.title" },
};

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
  const [authVersion, setAuthVersion] = useState(0);
  const [session, setSession] = useState({
    checking: true,
    user: getStoredUser(),
  });
  const token = localStorage.getItem("token");

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

  const userRole = session.user.globalRole || session.user.role || "User";

  if (allowedRoles && !allowedRoles.includes(userRole)) {
    return <Navigate to="/dashboard" replace />;
  }

  return (
    <div className="app-shell">
      <Sidebar user={session.user} />
      <main className="main-area">
        <Topbar pageMeta={pageMeta} />
        <section className="page-content">
          <Outlet />
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
      <Route path="*" element={<Navigate to="/dashboard" replace />} />
    </Routes>
  );
}
