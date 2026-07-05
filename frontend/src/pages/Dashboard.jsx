import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
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

const taskStatusColors = {
  "To Do": "#8e958d",
  "In Progress": "#07998d",
  Blocked: "#ef6845",
  Done: "#b4ef40",
};
const priorityColors = {
  High: "#ef6845",
  Medium: "#e5b544",
  Low: "#07998d",
};
const deadlineRiskMeta = [
  { key: "overdue", labelKey: "dashboard.deadline.overdue", color: "#ef6845" },
  { key: "dueSoon", labelKey: "dashboard.deadline.dueSoon", color: "#e5b544" },
  { key: "later", labelKey: "dashboard.deadline.later", color: "#07998d" },
  { key: "noDeadline", labelKey: "dashboard.deadline.noDeadline", color: "#8e958d" },
];
const dueSoonWindowMs = 7 * 24 * 60 * 60 * 1000;
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

function calculateCompletionRate(summary) {
  const taskCount = Number(summary?.taskCount || 0);
  if (taskCount <= 0) return 0;
  return Math.round((Number(summary?.completedTaskCount || 0) / taskCount) * 100);
}

function buildStats(summary, statusData) {
  const inProgress = statusData.find((item) => item.name === "In Progress")?.value || 0;
  const blocked = statusData.find((item) => item.name === "Blocked")?.value || 0;
  const overdue = Number(summary.overdueTaskCount || 0);
  const completionRate = calculateCompletionRate(summary);

  return [
    { labelKey: "stats.totalProjects", value: summary.projectCount || 0, tone: "amber", icon: "projects", supportingKey: "stats.liveWorkspace", supportingTone: "neutral" },
    { labelKey: "stats.totalTasks", value: summary.taskCount || 0, tone: "green", icon: "tasks", supportingKey: "stats.completionRate", supportingParams: { rate: completionRate }, supportingTone: completionRate >= 70 ? "positive" : "neutral" },
    { labelKey: "stats.inProgress", value: inProgress, tone: "yellow", icon: "progress", supportingKey: blocked > 0 ? "stats.blockedCount" : "stats.noBlocked", supportingParams: { count: blocked }, supportingTone: blocked > 0 ? "negative" : "positive" },
    { labelKey: "stats.completed", value: summary.completedTaskCount || 0, tone: "done", icon: "completed", supportingKey: "stats.completedShare", supportingParams: { rate: completionRate }, supportingTone: "positive" },
    { labelKey: "stats.overdue", value: overdue, tone: "red", icon: "overdue", supportingKey: overdue > 0 ? "stats.overdueRisk" : "stats.overdueClear", supportingParams: { count: overdue }, supportingTone: overdue > 0 ? "negative" : "positive" },
  ];
}

function countPriorities(tasks) {
  return ["High", "Medium", "Low"].map((priority) => ({
    priority,
    count: tasks.filter((task) => task.priorityLabel === priority).length,
  }));
}

function buildDeadlineRisk(tasks) {
  const counts = {
    overdue: 0,
    dueSoon: 0,
    later: 0,
    noDeadline: 0,
  };
  const now = Date.now();
  const dueSoonLimit = now + dueSoonWindowMs;

  tasks.forEach((task) => {
    if (task.statusLabel === "Done") return;

    const dueAt = task.deadlineUtc ? new Date(task.deadlineUtc).getTime() : Number.NaN;
    if (!Number.isFinite(dueAt)) {
      counts.noDeadline += 1;
    } else if (dueAt < now) {
      counts.overdue += 1;
    } else if (dueAt <= dueSoonLimit) {
      counts.dueSoon += 1;
    } else {
      counts.later += 1;
    }
  });

  return deadlineRiskMeta.map((item) => ({
    ...item,
    count: counts[item.key],
  }));
}

function chartLabel(value, maxLength = 24) {
  const label = String(value || "").trim();
  if (label.length <= maxLength) return label || "-";
  return `${label.slice(0, maxLength - 3)}...`;
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
  const projectProgressData = state.projects.slice(0, 5).map((project) => ({
    id: project.id,
    name: chartLabel(project.name),
    fullName: project.name,
    progress: project.progress,
    tasks: project.taskCount,
  }));
  const deadlineRiskData = buildDeadlineRisk(state.tasks).map((item) => ({
    ...item,
    label: t(item.labelKey),
  }));
  const totalDeadlineRiskCount = deadlineRiskData.reduce((sum, item) => sum + item.count, 0);

  function renderProjectTooltip({ active, payload }) {
    if (!active || !payload?.length) return null;
    const item = payload[0].payload;

    return (
      <div className="chart-tooltip">
        <strong>{item.fullName}</strong>
        <span>{t("dashboard.projectProgressTooltip", { progress: item.progress, tasks: item.tasks })}</span>
      </div>
    );
  }

  function exportReport() {
    const rows = [
      [t("dashboard.csv.workspace"), state.workspaceName || t("dashboard.none")],
      [t("dashboard.csv.totalProjects"), state.stats.find((stat) => stat.labelKey === "stats.totalProjects")?.value || 0],
      [t("dashboard.csv.totalTasks"), state.stats.find((stat) => stat.labelKey === "stats.totalTasks")?.value || 0],
      [t("dashboard.csv.completed"), state.stats.find((stat) => stat.labelKey === "stats.completed")?.value || 0],
      [t("dashboard.csv.overdue"), state.stats.find((stat) => stat.labelKey === "stats.overdue")?.value || 0],
      [],
      [
        t("dashboard.csv.project"),
        t("dashboard.csv.status"),
        t("dashboard.csv.progress"),
        t("dashboard.csv.tasks"),
        t("dashboard.csv.completed"),
      ],
      ...state.projects.map((project) => [
        project.name,
        t(enumProjectStatusKey(project.statusLabel)),
        `${project.progress}%`,
        project.taskCount,
        project.completedTaskCount,
      ]),
    ];
    const csv = rows
      .map((row) => row.map((cell) => `"${String(cell ?? "").replaceAll('"', '""')}"`).join(","))
      .join("\n");
    const blob = new Blob([csv], { type: "text/csv;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `taskflow-${state.workspaceName || "workspace"}-report.csv`;
    link.click();
    URL.revokeObjectURL(url);
  }

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
          <button className="secondary-button" type="button" onClick={exportReport} disabled={state.loading || !state.workspaceName}>
            {t("dashboard.exportReport")}
          </button>
        </aside>
      </section>

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
                sinceText={t(stat.supportingKey, stat.supportingParams)}
                supportingTone={stat.supportingTone}
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
                <div className="chart-row" role="img" aria-label={t("dashboard.taskStatusAria")}>
                  <ResponsiveContainer width="100%" height={260}>
                    <PieChart>
                      <Pie data={state.taskStatusData} dataKey="value" nameKey="name" innerRadius={64} outerRadius={98} paddingAngle={3} stroke="#f7f6f1">
                        {state.taskStatusData.map((entry) => <Cell key={entry.name} fill={taskStatusColors[entry.name] || "#8e958d"} />)}
                      </Pie>
                      <Tooltip
                        formatter={(value, name, item) => [
                          t("dashboard.taskCount", { count: value }),
                          t(enumTaskStatusKey(item?.payload?.name || name)),
                        ]}
                      />
                    </PieChart>
                  </ResponsiveContainer>
                  <div className="chart-legend">
                    {state.taskStatusData.map((item) => (
                      <div key={item.name}>
                        <span><i style={{ background: taskStatusColors[item.name] || "#8e958d" }} />{t(enumTaskStatusKey(item.name))}</span>
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
                <div className="chart-frame" role="img" aria-label={t("dashboard.priorityAria")}>
                  <ResponsiveContainer width="100%" height={260}>
                    <BarChart data={priorityChartData}>
                      <CartesianGrid strokeDasharray="4 4" vertical={false} stroke="#dedbd2" />
                      <XAxis dataKey="priorityLabel" stroke="#666a65" />
                      <YAxis allowDecimals={false} stroke="#666a65" />
                      <Tooltip
                        formatter={(value) => [t("dashboard.taskCount", { count: value }), t("dashboard.tooltip.tasks")]}
                      />
                      <Bar dataKey="count" radius={[10, 10, 0, 0]}>
                        {priorityChartData.map((item) => <Cell key={item.priority} fill={priorityColors[item.priority] || "#07998d"} />)}
                      </Bar>
                    </BarChart>
                  </ResponsiveContainer>
                </div>
              )}
            </article>
          </section>

          <section className="bottom-grid">
            <article className="panel">
              <div className="panel-header">
                <div>
                  <h3>{t("dashboard.projectProgress")}</h3>
                  <span>{t("dashboard.projectProgressHelp")}</span>
                </div>
                <Link to="/projects">{t("dashboard.viewAll")}</Link>
              </div>
              {projectProgressData.length === 0 ? (
                <p>{t("dashboard.noProjectProgress")}</p>
              ) : (
                <div className="chart-frame" role="img" aria-label={t("dashboard.projectProgressAria")}>
                  <ResponsiveContainer width="100%" height={260}>
                    <BarChart data={projectProgressData} layout="vertical" margin={{ top: 4, right: 12, bottom: 4, left: 4 }}>
                      <CartesianGrid strokeDasharray="4 4" horizontal={false} stroke="#dedbd2" />
                      <XAxis type="number" domain={[0, 100]} tickFormatter={(value) => `${value}%`} stroke="#666a65" />
                      <YAxis type="category" dataKey="name" width={132} stroke="#666a65" tick={{ fontSize: 12 }} />
                      <Tooltip content={renderProjectTooltip} />
                      <Bar dataKey="progress" radius={[0, 10, 10, 0]} fill="#07998d" />
                    </BarChart>
                  </ResponsiveContainer>
                </div>
              )}
            </article>

            <article className="panel">
              <div className="panel-header">
                <div>
                  <h3>{t("dashboard.deadlineRisk")}</h3>
                  <span>{t("dashboard.deadlineRiskHelp")}</span>
                </div>
                <Link to="/tasks">{t("dashboard.viewAll")}</Link>
              </div>
              {totalDeadlineRiskCount === 0 ? (
                <p>{t("dashboard.noDeadlineRisk")}</p>
              ) : (
                <div className="chart-frame" role="img" aria-label={t("dashboard.deadlineRiskAria")}>
                  <ResponsiveContainer width="100%" height={260}>
                    <BarChart data={deadlineRiskData}>
                      <CartesianGrid strokeDasharray="4 4" vertical={false} stroke="#dedbd2" />
                      <XAxis dataKey="label" interval={0} tick={{ fontSize: 12 }} stroke="#666a65" />
                      <YAxis allowDecimals={false} stroke="#666a65" />
                      <Tooltip
                        formatter={(value) => [t("dashboard.taskCount", { count: value }), t("dashboard.tooltip.openTasks")]}
                      />
                      <Bar dataKey="count" radius={[10, 10, 0, 0]}>
                        {deadlineRiskData.map((item) => <Cell key={item.key} fill={item.color} />)}
                      </Bar>
                    </BarChart>
                  </ResponsiveContainer>
                </div>
              )}
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
