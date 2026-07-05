import { useCallback, useEffect, useId, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { AnimatePresence, motion } from "motion/react";
import { X } from "lucide-react";
import { translateKey } from "../i18n.jsx";

const focusableSelector = [
  "a[href]",
  "button:not([disabled])",
  "textarea:not([disabled])",
  "input:not([disabled])",
  "select:not([disabled])",
  "[tabindex]:not([tabindex='-1'])",
].join(",");

const springTransition = {
  type: "spring",
  damping: 30,
  stiffness: 400,
};

export default function LinearModal({
  layoutId,
  title,
  description,
  icon: Icon,
  trigger,
  children,
  onClose,
  className = "",
  closeLabel = translateKey("common.close"),
  initialFocusRef,
  size = "md",
}) {
  const [isOpen, setIsOpen] = useState(false);
  const titleId = useId();
  const panelRef = useRef(null);
  const onCloseRef = useRef(onClose);
  const previouslyFocusedRef = useRef(null);
  const iconLayoutId = `${layoutId}-icon`;
  const titleLayoutId = `${layoutId}-title`;

  useEffect(() => {
    onCloseRef.current = onClose;
  }, [onClose]);

  const open = useCallback(() => {
    setIsOpen(true);
  }, []);

  const close = useCallback(() => {
    setIsOpen(false);
    onCloseRef.current?.();
  }, []);

  useEffect(() => {
    if (!isOpen) return undefined;

    previouslyFocusedRef.current = document.activeElement;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";

    const focusTarget = initialFocusRef?.current || panelRef.current?.querySelector(focusableSelector);
    window.requestAnimationFrame(() => focusTarget?.focus());

    function handleKeyDown(event) {
      if (event.key === "Escape") {
        event.preventDefault();
        close();
        return;
      }

      if (event.key !== "Tab" || !panelRef.current) return;

      const focusableElements = Array.from(panelRef.current.querySelectorAll(focusableSelector))
        .filter((element) => element.offsetParent !== null || element === document.activeElement);

      if (focusableElements.length === 0) {
        event.preventDefault();
        panelRef.current.focus();
        return;
      }

      const firstElement = focusableElements[0];
      const lastElement = focusableElements[focusableElements.length - 1];

      if (event.shiftKey && document.activeElement === firstElement) {
        event.preventDefault();
        lastElement.focus();
      } else if (!event.shiftKey && document.activeElement === lastElement) {
        event.preventDefault();
        firstElement.focus();
      }
    }

    document.addEventListener("keydown", handleKeyDown);

    return () => {
      document.removeEventListener("keydown", handleKeyDown);
      document.body.style.overflow = previousOverflow;
      previouslyFocusedRef.current?.focus?.();
    };
  }, [close, initialFocusRef, isOpen]);

  const modal = typeof document === "undefined" ? null : createPortal(
    <AnimatePresence>
      {isOpen && (
        <motion.div
          className="linear-modal-overlay"
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          transition={{ duration: 0.18 }}
        >
          <motion.div
            className="linear-modal-backdrop"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.22 }}
            onMouseDown={close}
          />
          <motion.section
            aria-labelledby={titleId}
            aria-modal="true"
            className={`linear-modal-container linear-modal-${size} ${className}`.trim()}
            layoutId={layoutId}
            ref={panelRef}
            role="dialog"
            tabIndex={-1}
            transition={springTransition}
          >
            <motion.button
              aria-label={closeLabel}
              className="linear-modal-close"
              type="button"
              initial={{ opacity: 0, scale: 0.85 }}
              animate={{ opacity: 1, scale: 1 }}
              exit={{ opacity: 0, scale: 0.85 }}
              transition={{ delay: 0.08, duration: 0.16 }}
              onClick={close}
            >
              <X size={18} />
            </motion.button>

            <div className="linear-modal-header">
              {Icon && (
                <motion.div className="linear-modal-icon" layoutId={iconLayoutId} transition={springTransition}>
                  <Icon size={26} />
                </motion.div>
              )}
              <div className="linear-modal-title-wrap">
                <motion.h2
                  className="linear-modal-title"
                  id={titleId}
                  layoutId={titleLayoutId}
                  transition={springTransition}
                >
                  {title}
                </motion.h2>
                {description && <p className="linear-modal-description">{description}</p>}
              </div>
            </div>

            <motion.div
              className="linear-modal-body"
              initial={{ opacity: 0, y: 12 }}
              animate={{ opacity: 1, y: 0 }}
              exit={{ opacity: 0, y: 8 }}
              transition={{ duration: 0.18, delay: 0.08 }}
            >
              {children({ close, isOpen, open })}
            </motion.div>
          </motion.section>
        </motion.div>
      )}
    </AnimatePresence>,
    document.body
  );

  return (
    <div className="linear-modal-root">
      {trigger({ close, iconLayoutId, isOpen, layoutId, open, titleLayoutId })}
      {modal}
    </div>
  );
}
