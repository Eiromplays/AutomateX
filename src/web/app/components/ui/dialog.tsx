import * as DialogPrimitive from "@radix-ui/react-dialog";
import type { ReactNode } from "react";

// Thin shadcn-style wrappers over Radix Dialog, styled with our zinc Tailwind classes.
export const Dialog = DialogPrimitive.Root;
export const DialogTrigger = DialogPrimitive.Trigger;
export const DialogClose = DialogPrimitive.Close;

export function DialogContent({ title, children }: { title: string; children: ReactNode }) {
  return (
    <DialogPrimitive.Portal>
      <DialogPrimitive.Overlay className="fixed inset-0 z-50 bg-black/60" />
      <DialogPrimitive.Content
        aria-describedby={undefined}
        className="fixed left-1/2 top-1/2 z-50 max-h-[85vh] w-full max-w-lg -translate-x-1/2 -translate-y-1/2 overflow-auto rounded-lg border border-zinc-700 bg-zinc-900 p-5 shadow-xl focus:outline-none"
      >
        <div className="mb-3 flex items-center justify-between gap-4">
          <DialogPrimitive.Title className="text-sm font-medium text-zinc-200">{title}</DialogPrimitive.Title>
          <DialogPrimitive.Close className="shrink-0 text-zinc-500 hover:text-zinc-200">
            ✕
          </DialogPrimitive.Close>
        </div>
        {children}
      </DialogPrimitive.Content>
    </DialogPrimitive.Portal>
  );
}
