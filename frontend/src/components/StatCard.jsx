import { Briefcase, CheckCircle2, Clock3, ListTodo, TriangleAlert } from "lucide-react";

const icons = {
  "Total projects": Briefcase,
  "Total tasks": ListTodo,
  "In progress": Clock3,
  Completed: CheckCircle2,
  Overdue: TriangleAlert,
};

export default function StatCard({ label, value, change, tone }) {
  const Icon = icons[label] || Briefcase;

  return (
    <article className={`stat-card ${tone}`}>
      <div className="stat-icon"><Icon size={22} /></div>
      <div>
        <p>{label}</p>
        <h3>{value}</h3>
        <span className={change.startsWith("-") ? "negative" : "positive"}>
          {change} since last update
        </span>
      </div>
    </article>
  );
}
