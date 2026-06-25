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
import { priorityData, projects, stats, taskStatusData, tasks, notifications } from "../data/mockData.js";

const pieColors = ["#07998d", "#ef6845", "#b4ef40"];

export default function Dashboard() {
  return (
    <div className="page-stack">
      <section className="dashboard-hero">
        <div className="hero-copy">
          <span className="hero-kicker">Frontend scope</span>
          <h1>Dashboard, validation, demo.</h1>
          <p>
            This board tracks only the frontend work that must be visible in the Week 15 recording: dashboard charts, task forms, clean errors, and backup screenshots.
          </p>
        </div>

        <aside className="deadline-card">
          <div>
            <small>Coursework deadline</small>
            <strong>8 Jul</strong>
          </div>
          <div className="demo-route" aria-label="Demo route">
            <span>01 Login with JWT</span>
            <span>02 Open dashboard</span>
            <span>03 Create failed task form</span>
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

      <section className="stats-grid">
        {stats.map((stat) => <StatCard key={stat.label} {...stat} />)}
      </section>

      <section className="dashboard-grid">
        <article className="panel large-panel">
          <div className="panel-header">
            <div>
              <h3>Task status</h3>
              <span>Use the same status values returned by the backend.</span>
            </div>
            <span>Week 15</span>
          </div>
          <div className="chart-row">
            <ResponsiveContainer width="100%" height={260}>
              <PieChart>
                <Pie data={taskStatusData} dataKey="value" nameKey="name" innerRadius={64} outerRadius={98} paddingAngle={3} stroke="#f7f6f1">
                  {taskStatusData.map((entry, index) => <Cell key={entry.name} fill={pieColors[index]} />)}
                </Pie>
                <Tooltip />
              </PieChart>
            </ResponsiveContainer>
            <div className="chart-legend">
              {taskStatusData.map((item) => (
                <div key={item.name}>
                  <span>{item.name}</span>
                  <strong>{item.value} tasks</strong>
                </div>
              ))}
            </div>
          </div>
        </article>

        <article className="panel large-panel">
          <div className="panel-header">
            <div>
              <h3>Priority load</h3>
              <span>High priority work should have an owner before the demo.</span>
            </div>
            <span>Week 15</span>
          </div>
          <ResponsiveContainer width="100%" height={260}>
            <BarChart data={priorityData}>
              <CartesianGrid strokeDasharray="4 4" vertical={false} stroke="#dedbd2" />
              <XAxis dataKey="priority" stroke="#666a65" />
              <YAxis stroke="#666a65" />
              <Tooltip />
              <Bar dataKey="count" radius={[10, 10, 0, 0]} fill="#07998d" />
            </BarChart>
          </ResponsiveContainer>
        </article>
      </section>

      <section className="bottom-grid">
        <article className="panel">
          <div className="panel-header">
            <h3>Active projects</h3>
            <a>View all</a>
          </div>
          <div className="project-list compact">
            {projects.slice(0, 3).map((project) => (
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
            {tasks.slice(0, 4).map((task) => (
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
            {notifications.slice(0, 4).map((item, index) => (
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
    </div>
  );
}
