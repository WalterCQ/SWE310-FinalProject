import { Briefcase, CheckCircle2, Clock3, ListTodo, TriangleAlert } from "lucide-react";

const icons = {
  projects: Briefcase,
  tasks: ListTodo,
  progress: Clock3,
  completed: CheckCircle2,
  overdue: TriangleAlert,
};

export default function StatCard({ label, value, change, tone, icon = "projects", sinceText }) {
  const Icon = icons[icon] || Briefcase;

  return (
    <article className={`stat-card ${tone}`}>
      <div className="stat-icon"><Icon size={22} /></div>
      <div>
        <p>{label}</p>
        <h3>{value}</h3>
        <span className={change.startsWith("-") ? "negative" : "positive"}>
          {sinceText}
        </span>
      </div>
    </article>
  );
}
