export default function StatusBadge({ children, variant }) {
  const text = String(variant || children).toLowerCase().replaceAll(" ", "-");
  return <span className={`status-badge ${text}`}>{children}</span>;
}
