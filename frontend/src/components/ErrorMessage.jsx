export default function ErrorMessage({ children }) {
  if (!children) return null;
  return <p className="error-message">{children}</p>;
}
