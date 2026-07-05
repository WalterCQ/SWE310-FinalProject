import { Plus } from "lucide-react";

export default function CreateActionButton({
  children,
  onClick,
  disabled = false,
  className = "",
  ariaLabel,
}) {
  return (
    <button
      aria-label={ariaLabel}
      className={`primary-button small create-action-button ${className}`.trim()}
      type="button"
      onClick={onClick}
      disabled={disabled}
    >
      <Plus size={18} /> {children}
    </button>
  );
}
