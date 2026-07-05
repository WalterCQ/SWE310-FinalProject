import { Briefcase, CheckCircle2, Clock3, ListTodo, TriangleAlert } from "lucide-react";

const icons = {
  projects: Briefcase,
  tasks: ListTodo,
  progress: Clock3,
  completed: CheckCircle2,
  overdue: TriangleAlert,
};

export default function StatCard({ label, value, change = "", tone, icon = "projects", sinceText, supportingTone }) {
  const Icon = icons[icon] || Briefcase;
  const textTone = supportingTone || (change.startsWith("-") ? "negative" : "positive");

  return (
    <article className={`stat-card ${tone}`}>
      <div className="stat-icon"><Icon size={22} /></div>
      <div>
        <p>{label}</p>
        <h3>{value}</h3>
        <span className={textTone}>
          {sinceText}
        </span>
      </div>
    </article>
  );
}
