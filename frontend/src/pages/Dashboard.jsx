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
import { enumPriorityKey, enumProjectStatusKey, enumTaskStatusKey, useI18n } from "../i18n.jsx";

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
    { labelKey: "stats.totalProjects", value: summary.projectCount || 0, change: "+0", tone: "amber", icon: "projects" },
    { labelKey: "stats.totalTasks", value: summary.taskCount || 0, change: "+0", tone: "green", icon: "tasks" },
    { labelKey: "stats.inProgress", value: inProgress, change: "+0", tone: "yellow", icon: "progress" },
    { labelKey: "stats.completed", value: summary.completedTaskCount || 0, change: "+0", tone: "done", icon: "completed" },
    { labelKey: "stats.overdue", value: summary.overdueTaskCount || 0, change: "+0", tone: "red", icon: "overdue" },
  ];
}

function countPriorities(tasks) {
  return ["High", "Medium", "Low"].map((priority) => ({
    priority,
    count: tasks.filter((task) => task.priorityLabel === priority).length,
  }));
}

export default function Dashboard() {
  const { t } = useI18n();
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
    ? state.activities.map((title, index) => ({ id: `activity-${index}`, title, time: t("dashboard.recent") }))
    : state.notifications.slice(0, 4);
  const priorityChartData = state.priorityData.map((item) => ({
    ...item,
    priorityLabel: t(enumPriorityKey(item.priority)),
  }));

  return (
    <div className="page-stack">
      <section className="dashboard-hero">
        <div className="hero-copy">
          <span className="hero-kicker">{t("dashboard.heroKicker")}</span>
          <h1>{t("dashboard.heroTitle")}</h1>
          <p>{t("dashboard.heroBody")}</p>
        </div>

        <aside className="deadline-card">
          <div>
            <small>{t("dashboard.activeWorkspace")}</small>
            <strong>{state.workspaceName || t("dashboard.none")}</strong>
          </div>
          <div className="demo-route" aria-label={t("dashboard.demoRoute")}>
            <span>{t("dashboard.route.login")}</span>
            <span>{t("dashboard.route.dashboard")}</span>
            <span>{t("dashboard.route.task")}</span>
            <span>{t("dashboard.route.ai")}</span>
          </div>
        </aside>
      </section>

      <div className="page-heading">
        <div>
          <p className="eyebrow">{t("dashboard.eyebrow")}</p>
          <h1>{t("dashboard.title")}</h1>
        </div>
        <button className="secondary-button">{t("dashboard.export")}</button>
      </div>

      {state.loading && <section className="panel">{t("dashboard.loading")}</section>}
      {state.error && <section className="panel"><ErrorBlock message={state.error} t={t} /></section>}
      {!state.loading && !state.error && !state.workspaceName && (
        <section className="panel">{t("dashboard.emptyWorkspace")}</section>
      )}

      {!state.loading && !state.error && state.workspaceName && (
        <>
          <section className="stats-grid">
            {state.stats.map((stat) => (
              <StatCard
                key={stat.labelKey}
                {...stat}
                label={t(stat.labelKey)}
                sinceText={t("stats.since", { change: stat.change })}
              />
            ))}
          </section>

          <section className="dashboard-grid">
            <article className="panel large-panel">
              <div className="panel-header">
                <div>
                  <h3>{t("dashboard.taskStatus")}</h3>
                  <span>{t("dashboard.taskStatusHelp")}</span>
                </div>
                <span>{state.workspaceName}</span>
              </div>
              {totalStatusCount === 0 ? (
                <p>{t("dashboard.noTaskStatus")}</p>
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
                        <span>{t(enumTaskStatusKey(item.name))}</span>
                        <strong>{t("dashboard.taskCount", { count: item.value })}</strong>
                      </div>
                    ))}
                  </div>
                </div>
              )}
            </article>

            <article className="panel large-panel">
              <div className="panel-header">
                <div>
                  <h3>{t("dashboard.priorityLoad")}</h3>
                  <span>{t("dashboard.priorityHelp")}</span>
                </div>
                <span>{state.workspaceName}</span>
              </div>
              {totalPriorityCount === 0 ? (
                <p>{t("dashboard.noPriority")}</p>
              ) : (
                <ResponsiveContainer width="100%" height={260}>
                  <BarChart data={priorityChartData}>
                    <CartesianGrid strokeDasharray="4 4" vertical={false} stroke="#dedbd2" />
                    <XAxis dataKey="priorityLabel" stroke="#666a65" />
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
                <h3>{t("dashboard.activeProjects")}</h3>
                <a>{t("dashboard.viewAll")}</a>
              </div>
              <div className="project-list compact">
                {state.projects.length === 0 && <p>{t("dashboard.noProjects")}</p>}
                {state.projects.slice(0, 3).map((project) => (
                  <div className="project-row" key={project.id}>
                    <div>
                      <h4>{project.name}</h4>
                      <p>{t(enumProjectStatusKey(project.statusLabel))}</p>
                    </div>
                    <div className="progress-shell"><span style={{ width: `${project.progress}%` }} /></div>
                    <strong>{project.progress}%</strong>
                  </div>
                ))}
              </div>
            </article>

            <article className="panel">
              <div className="panel-header">
                <h3>{t("dashboard.demoChecklist")}</h3>
                <a>{t("dashboard.viewAll")}</a>
              </div>
              <div className="task-list compact">
                {state.tasks.length === 0 && <p>{t("dashboard.noTasks")}</p>}
                {state.tasks.slice(0, 4).map((task) => (
                  <div className="task-line" key={task.id}>
                    <div>
                      <h4>{task.title}</h4>
                      <p>{task.project}</p>
                    </div>
                    <StatusBadge variant={task.priorityLabel}>{t(enumPriorityKey(task.priorityLabel))}</StatusBadge>
                    <span>{task.deadlineLabel}</span>
                  </div>
                ))}
              </div>
            </article>

            <article className="panel">
              <div className="panel-header">
                <h3>{t("dashboard.recentActivity")}</h3>
              </div>
              <div className="activity-list">
                {activityItems.length === 0 && <p>{t("dashboard.noActivity")}</p>}
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

function ErrorBlock({ message, t }) {
  return (
    <div>
      <strong>{t("dashboard.unable")}</strong>
      <p>{message}</p>
    </div>
  );
}
