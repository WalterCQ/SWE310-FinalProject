import { useEffect, useState } from "react";
import {
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  Pie,
  PieChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import StatCard from "../components/StatCard.jsx";
import StatusBadge from "../components/StatusBadge.jsx";
import {
  dashboard,
  formatApiError,
  notifications as notificationsApi,
  projects as projectsApi,
  tasks as tasksApi,
  workspaces as workspacesApi,
} from "../api/taskflowApi.js";
import {
  asArray,
  mapNotification,
  mapProject,
  mapTask,
  mapTasksByStatus,
  mapWorkspace,
  selectPrimaryWorkspace,
} from "../api/mappers.js";

const pieColors = ["#07998d", "#ef6845", "#e5b544", "#b4ef40"];
const emptyState = {
  loading: true,
  error: "",
  workspaceName: "",
  stats: [],
  taskStatusData: [],
  priorityData: [],
  projects: [],
  tasks: [],
  notifications: [],
  activities: [],
};

function buildStats(summary, statusData) {
  const inProgress = statusData.find((item) => item.name === "In Progress")?.value || 0;

  return [
    { label: "Total projects", value: summary.projectCount || 0, change: "+0", tone: "amber" },
    { label: "Total tasks", value: summary.taskCount || 0, change: "+0", tone: "green" },
    { label: "In progress", value: inProgress, change: "+0", tone: "yellow" },
    { label: "Completed", value: summary.completedTaskCount || 0, change: "+0", tone: "done" },
    { label: "Overdue", value: summary.overdueTaskCount || 0, change: "+0", tone: "red" },
  ];
}

function countPriorities(tasks) {
  return ["High", "Medium", "Low"].map((priority) => ({
    priority,
    count: tasks.filter((task) => task.priority === priority).length,
  }));
}

export default function Dashboard() {
  const [state, setState] = useState(emptyState);

  useEffect(() => {
    let active = true;

    async function loadDashboard() {
      setState((current) => ({ ...current, loading: true, error: "" }));

      try {
        const workspaceList = asArray(await workspacesApi.list()).map(mapWorkspace);
        const workspace = selectPrimaryWorkspace(workspaceList);

        if (!workspace) {
          if (active) {
            setState({ ...emptyState, loading: false, workspaceName: "" });
          }
          return;
        }

        const [summary, projectResponse, notificationResponse] = await Promise.all([
          dashboard.workspace(workspace.id),
          projectsApi.listByWorkspace(workspace.id),
          notificationsApi.list(),
        ]);

        const projectItems = asArray(projectResponse).map(mapProject);
        const projectLookup = Object.fromEntries(projectItems.map((project) => [project.id, project]));
        const taskResponses = await Promise.all(projectItems.map((project) => tasksApi.listByProject(project.id)));
        const taskItems = taskResponses.flatMap((items) => asArray(items).map((task) => mapTask(task, projectLookup)));
        const statusData = mapTasksByStatus(summary?.tasksByStatus);

        if (active) {
          setState({
            loading: false,
            error: "",
            workspaceName: workspace.name,
            stats: buildStats(summary || {}, statusData),
            taskStatusData: statusData,
            priorityData: countPriorities(taskItems),
            projects: projectItems,
            tasks: taskItems,
            notifications: asArray(notificationResponse).map(mapNotification),
            activities: asArray(summary?.recentActivities),
          });
        }
      } catch (error) {
        if (active) {
          setState((current) => ({
            ...current,
            loading: false,
            error: formatApiError(error),
          }));
        }
      }
    }

    loadDashboard();

    return () => {
      active = false;
    };
  }, []);

  const totalStatusCount = state.taskStatusData.reduce((sum, item) => sum + item.value, 0);
  const totalPriorityCount = state.priorityData.reduce((sum, item) => sum + item.count, 0);
  const activityItems = state.activities.length > 0
    ? state.activities.map((title, index) => ({ id: `activity-${index}`, title, time: "Recent" }))
    : state.notifications.slice(0, 4);

  return (
    <div className="page-stack">
      <section className="dashboard-hero">
        <div className="hero-copy">
          <span className="hero-kicker">Frontend scope</span>
          <h1>Dashboard, validation, demo.</h1>
          <p>
            This board tracks the live Azure workspace data used by TaskFlow: dashboard charts, task forms, clean errors, and AI project summary evidence.
          </p>
        </div>

        <aside className="deadline-card">
          <div>
            <small>Active workspace</small>
            <strong>{state.workspaceName || "None"}</strong>
          </div>
          <div className="demo-route" aria-label="Demo route">
            <span>01 Login with demo token</span>
            <span>02 Load Azure dashboard</span>
            <span>03 Create task through API</span>
            <span>04 Show AI summary</span>
          </div>
        </aside>
      </section>

      <div className="page-heading">
        <div>
          <p className="eyebrow">Dashboard</p>
          <h1>Build status</h1>
        </div>
        <button className="secondary-button">Export report</button>
      </div>

      {state.loading && <section className="panel">Loading dashboard from Azure...</section>}
      {state.error && <section className="panel"><ErrorBlock message={state.error} /></section>}
      {!state.loading && !state.error && !state.workspaceName && (
        <section className="panel">No workspace data is available yet.</section>
      )}

      {!state.loading && !state.error && state.workspaceName && (
        <>
          <section className="stats-grid">
            {state.stats.map((stat) => <StatCard key={stat.label} {...stat} />)}
          </section>

          <section className="dashboard-grid">
            <article className="panel large-panel">
              <div className="panel-header">
                <div>
                  <h3>Task status</h3>
                  <span>Live status values returned by the backend.</span>
                </div>
                <span>{state.workspaceName}</span>
              </div>
              {totalStatusCount === 0 ? (
                <p>No task status data yet.</p>
              ) : (
                <div className="chart-row">
                  <ResponsiveContainer width="100%" height={260}>
                    <PieChart>
                      <Pie data={state.taskStatusData} dataKey="value" nameKey="name" innerRadius={64} outerRadius={98} paddingAngle={3} stroke="#f7f6f1">
                        {state.taskStatusData.map((entry, index) => <Cell key={entry.name} fill={pieColors[index]} />)}
                      </Pie>
                      <Tooltip />
                    </PieChart>
                  </ResponsiveContainer>
                  <div className="chart-legend">
                    {state.taskStatusData.map((item) => (
                      <div key={item.name}>
                        <span>{item.name}</span>
                        <strong>{item.value} tasks</strong>
                      </div>
                    ))}
                  </div>
                </div>
              )}
            </article>

            <article className="panel large-panel">
              <div className="panel-header">
                <div>
                  <h3>Priority load</h3>
                  <span>Priority counts are calculated from project tasks.</span>
                </div>
                <span>{state.workspaceName}</span>
              </div>
              {totalPriorityCount === 0 ? (
                <p>No task priority data yet.</p>
              ) : (
                <ResponsiveContainer width="100%" height={260}>
                  <BarChart data={state.priorityData}>
                    <CartesianGrid strokeDasharray="4 4" vertical={false} stroke="#dedbd2" />
                    <XAxis dataKey="priority" stroke="#666a65" />
                    <YAxis stroke="#666a65" />
                    <Tooltip />
                    <Bar dataKey="count" radius={[10, 10, 0, 0]} fill="#07998d" />
                  </BarChart>
                </ResponsiveContainer>
              )}
            </article>
          </section>

          <section className="bottom-grid">
            <article className="panel">
              <div className="panel-header">
                <h3>Active projects</h3>
                <a>View all</a>
              </div>
              <div className="project-list compact">
                {state.projects.length === 0 && <p>No projects found.</p>}
                {state.projects.slice(0, 3).map((project) => (
                  <div className="project-row" key={project.id}>
                    <div>
                      <h4>{project.name}</h4>
                      <p>{project.status}</p>
                    </div>
                    <div className="progress-shell"><span style={{ width: `${project.progress}%` }} /></div>
                    <strong>{project.progress}%</strong>
                  </div>
                ))}
              </div>
            </article>

            <article className="panel">
              <div className="panel-header">
                <h3>Demo checklist</h3>
                <a>View all</a>
              </div>
              <div className="task-list compact">
                {state.tasks.length === 0 && <p>No tasks found.</p>}
                {state.tasks.slice(0, 4).map((task) => (
                  <div className="task-line" key={task.id}>
                    <div>
                      <h4>{task.title}</h4>
                      <p>{task.project}</p>
                    </div>
                    <StatusBadge>{task.priority}</StatusBadge>
                    <span>{task.dueDate}</span>
                  </div>
                ))}
              </div>
            </article>

            <article className="panel">
              <div className="panel-header">
                <h3>Recent activity</h3>
              </div>
              <div className="activity-list">
                {activityItems.length === 0 && <p>No recent activity yet.</p>}
                {activityItems.map((item, index) => (
                  <div className="activity-item" key={item.id}>
                    <div className="activity-index">{String(index + 1).padStart(2, "0")}</div>
                    <div>
                      <p>{item.title}</p>
                      <span>{item.time}</span>
                    </div>
                  </div>
                ))}
              </div>
            </article>
          </section>
        </>
      )}
    </div>
  );
}

function ErrorBlock({ message }) {
  return (
    <div>
      <strong>Unable to load dashboard.</strong>
      <p>{message}</p>
    </div>
  );
}
