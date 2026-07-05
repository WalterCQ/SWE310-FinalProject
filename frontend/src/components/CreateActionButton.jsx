import { Plus } from "lucide-react";
import { motion } from "motion/react";

export default function CreateActionButton({
  as: Component = "button",
  children,
  onClick,
  disabled = false,
  className = "",
  ariaLabel,
  iconLayoutId,
  titleLayoutId,
  ...props
}) {
  return (
    <Component
      aria-label={ariaLabel}
      className={`primary-button small create-action-button ${className}`.trim()}
      type="button"
      onClick={onClick}
      disabled={disabled}
      {...props}
    >
      <motion.span className="linear-modal-trigger-icon" layoutId={iconLayoutId}>
        <Plus size={18} />
      </motion.span>
      <motion.span className="linear-modal-trigger-title" layoutId={titleLayoutId}>
        {children}
      </motion.span>
    </Component>
  );
}
