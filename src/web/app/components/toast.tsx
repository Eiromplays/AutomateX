import { Toaster } from "./ui/sonner";

// Thin shim over sonner: `toast.success/error` from anywhere, `<Toasts/>` mounted
// once in the shell. Keeping this module + names means no call sites changed when
// the hand-rolled notifications were replaced with sonner.
export { toast } from "sonner";

export function Toasts() {
  return <Toaster position="bottom-right" richColors closeButton />;
}
