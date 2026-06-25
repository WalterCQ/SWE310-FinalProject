import { Navigate, Route, Routes, useLocation } from "react-router-dom";
import Login from "./pages/Login.jsx";
import Dashboard from "./pages/Dashboard.jsx";
import Workspaces from "./pages/Workspaces.jsx";
import Channels from "./pages/Channels.jsx";
import Projects from "./pages/Projects.jsx";
import Tasks from "./pages/Tasks.jsx";
import AIAssistant from "./pages/AIAssistant.jsx";
import Notifications from "./pages/Notifications.jsx";
import Sidebar from "./components/Sidebar.jsx";
import Topbar from "./components/Topbar.jsx";

function ProtectedShell({ children }) {
  const token = localStorage.getItem("token");
  const location = useLocation();

  if (!token) {
    return <Navigate to="/login" state={{ from: location }} replace />;
  }

  return (
    <div className="app-shell">
      <Sidebar />
      <main className="main-area">
        <Topbar />
        <section className="page-content">{children}</section>
      </main>
    </div>
  );
}

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<Login />} />
      <Route path="/" element={<Navigate to="/dashboard" replace />} />
      <Route
        path="/dashboard"
        element={
          <ProtectedShell>
            <Dashboard />
          </ProtectedShell>
        }
      />
      <Route
        path="/workspaces"
        element={
          <ProtectedShell>
            <Workspaces />
          </ProtectedShell>
        }
      />
      <Route
        path="/channels"
        element={
          <ProtectedShell>
            <Channels />
          </ProtectedShell>
        }
      />
      <Route
        path="/projects"
        element={
          <ProtectedShell>
            <Projects />
          </ProtectedShell>
        }
      />
      <Route
        path="/tasks"
        element={
          <ProtectedShell>
            <Tasks />
          </ProtectedShell>
        }
      />
      <Route
        path="/ai-assistant"
        element={
          <ProtectedShell>
            <AIAssistant />
          </ProtectedShell>
        }
      />
      <Route
        path="/notifications"
        element={
          <ProtectedShell>
            <Notifications />
          </ProtectedShell>
        }
      />
      <Route path="*" element={<Navigate to="/dashboard" replace />} />
    </Routes>
  );
}
