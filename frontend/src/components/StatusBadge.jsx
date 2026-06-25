export default function StatusBadge({ children }) {
  const text = String(children).toLowerCase().replaceAll(" ", "-");
  return <span className={`status-badge ${text}`}>{children}</span>;
}
