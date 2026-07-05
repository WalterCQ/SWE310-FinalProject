import { useEffect, useId, useRef } from "react";
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

export default function FormModal({
  open,
  title,
  description,
  children,
  onClose,
  closeLabel = translateKey("common.close"),
  labelledById,
  initialFocusRef,
  size = "md",
}) {
  const generatedTitleId = useId();
  const titleId = labelledById || generatedTitleId;
  const panelRef = useRef(null);
  const onCloseRef = useRef(onClose);
  const previouslyFocusedRef = useRef(null);

  useEffect(() => {
    onCloseRef.current = onClose;
  }, [onClose]);

  useEffect(() => {
    if (!open) return undefined;

    previouslyFocusedRef.current = document.activeElement;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";

    const focusTarget = initialFocusRef?.current || panelRef.current?.querySelector(focusableSelector);
    window.requestAnimationFrame(() => focusTarget?.focus());

    function handleKeyDown(event) {
      if (event.key === "Escape") {
        event.preventDefault();
        onCloseRef.current?.();
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
  }, [initialFocusRef, open]);

  if (!open) return null;

  return (
    <div
      className="modal-backdrop"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onClose?.();
      }}
    >
      <section
        aria-labelledby={titleId}
        aria-modal="true"
        className={`form-modal form-modal-${size}`}
        ref={panelRef}
        role="dialog"
        tabIndex={-1}
      >
        <div className="form-modal-header">
          <div>
            <h3 id={titleId}>{title}</h3>
            {description && <p>{description}</p>}
          </div>
          <button className="icon-button" type="button" aria-label={closeLabel} onClick={onClose}>
            <X size={18} />
          </button>
        </div>

        <div className="form-modal-body">{children}</div>
      </section>
    </div>
  );
}
