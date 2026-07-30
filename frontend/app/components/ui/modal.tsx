import { useEffect, useRef } from "react";
import type { ReactNode } from "react";
import { Button } from "./button";
import { Icon } from "./icon";

export type ModalProps = {
  open: boolean;
  title: ReactNode;
  children: ReactNode;
  footer?: ReactNode;
  onClose: () => void;
  className?: string;
  /** When true, backdrop / Escape / X cannot dismiss the dialog. */
  preventClose?: boolean;
};

export function Modal({ open, title, children, footer, onClose, className = "", preventClose = false }: ModalProps) {
  const dialogRef = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;

    if (open && !dialog.open) {
      dialog.showModal();
    } else if (!open && dialog.open) {
      dialog.close();
    }
  }, [open]);

  return (
    <dialog
      ref={dialogRef}
      className="modal"
      onCancel={(event) => {
        if (preventClose) {
          event.preventDefault();
        }
      }}
      onClose={() => {
        if (preventClose) {
          dialogRef.current?.showModal();
          return;
        }
        if (open) onClose();
      }}
    >
      <div className={`modal-box max-h-[90dvh] w-full max-w-xl overflow-y-auto ${className}`}>
        <form method="dialog">
          <Button
            type="submit"
            variant="ghost"
            size="xsmall"
            className="btn-circle absolute top-2 right-2"
            aria-label="Close"
            disabled={preventClose}
          >
            <Icon name="close" className="!text-[20px]" />
          </Button>
        </form>
        <h2 className="text-lg font-bold">{title}</h2>
        <div className="py-4">{children}</div>
        {footer && <div className="modal-action">{footer}</div>}
      </div>
      <form method="dialog" className="modal-backdrop">
        <button type="submit" disabled={preventClose}>close</button>
      </form>
    </dialog>
  );
}
