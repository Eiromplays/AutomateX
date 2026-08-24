import { createContext, type ReactNode, useCallback, useContext, useRef, useState } from "react";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "./alert-dialog";
import { Button } from "./button";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "./dialog";

type ConfirmOptions = {
  title: string;
  body?: ReactNode;
  confirmLabel?: string;
  cancelLabel?: string;
  destructive?: boolean;
};

type ConfirmFn = (options: ConfirmOptions) => Promise<boolean>;

type PromptOptions = {
  title: string;
  label?: string;
  placeholder?: string;
  initialValue?: string;
  confirmLabel?: string;
  password?: boolean;
};

type PromptFn = (options: PromptOptions) => Promise<string | null>;

const ConfirmContext = createContext<ConfirmFn | null>(null);
const PromptContext = createContext<PromptFn | null>(null);

// Promise-based confirm: `if (await confirm({...})) …`, rendered through a shadcn AlertDialog so
// every confirmation looks and behaves the same (instead of the browser's window.confirm).
export function useConfirm(): ConfirmFn {
  const fn = useContext(ConfirmContext);
  if (!fn) throw new Error("useConfirm must be used within a ConfirmProvider.");
  return fn;
}

// Promise-based text prompt: `const value = await prompt({...})` (null when cancelled).
export function usePrompt(): PromptFn {
  const fn = useContext(PromptContext);
  if (!fn) throw new Error("usePrompt must be used within a ConfirmProvider.");
  return fn;
}

export function ConfirmProvider({ children }: { children: ReactNode }) {
  const [options, setOptions] = useState<ConfirmOptions | null>(null);
  const resolver = useRef<((ok: boolean) => void) | null>(null);

  const confirm = useCallback<ConfirmFn>((next) => {
    setOptions(next);
    return new Promise<boolean>((resolve) => {
      resolver.current = resolve;
    });
  }, []);

  // Null the resolver on first settle so the auto-close that follows a button click
  // (which re-fires onOpenChange) can't overwrite the result.
  const settle = (ok: boolean) => {
    resolver.current?.(ok);
    resolver.current = null;
    setOptions(null);
  };

  const [prompt, setPrompt] = useState<PromptOptions | null>(null);
  const [promptValue, setPromptValue] = useState("");
  const promptResolver = useRef<((value: string | null) => void) | null>(null);

  const promptFn = useCallback<PromptFn>((next) => {
    setPrompt(next);
    setPromptValue(next.initialValue ?? "");
    return new Promise<string | null>((resolve) => {
      promptResolver.current = resolve;
    });
  }, []);

  const settlePrompt = (value: string | null) => {
    promptResolver.current?.(value);
    promptResolver.current = null;
    setPrompt(null);
  };

  return (
    <ConfirmContext.Provider value={confirm}>
      <PromptContext.Provider value={promptFn}>
        {children}
        <AlertDialog open={options != null} onOpenChange={(open) => (open ? undefined : settle(false))}>
          {options && (
            <AlertDialogContent>
              <AlertDialogHeader>
                <AlertDialogTitle>{options.title}</AlertDialogTitle>
                {options.body && <AlertDialogDescription>{options.body}</AlertDialogDescription>}
              </AlertDialogHeader>
              <AlertDialogFooter>
                <AlertDialogCancel onClick={() => settle(false)}>
                  {options.cancelLabel ?? "Cancel"}
                </AlertDialogCancel>
                <AlertDialogAction
                  variant={options.destructive ? "destructive" : "default"}
                  onClick={() => settle(true)}
                >
                  {options.confirmLabel ?? "Confirm"}
                </AlertDialogAction>
              </AlertDialogFooter>
            </AlertDialogContent>
          )}
        </AlertDialog>

        <Dialog open={prompt != null} onOpenChange={(open) => (open ? undefined : settlePrompt(null))}>
          {prompt && (
            <DialogContent className="sm:max-w-sm">
              <DialogHeader>
                <DialogTitle>{prompt.title}</DialogTitle>
              </DialogHeader>
              <form
                onSubmit={(e) => {
                  e.preventDefault();
                  settlePrompt(promptValue);
                }}
                className="space-y-3"
              >
                {prompt.label && <span className="block text-xs text-muted-foreground">{prompt.label}</span>}
                <input
                  autoFocus
                  type={prompt.password ? "password" : "text"}
                  className="w-full rounded-[2px] border border-input bg-transparent px-3 py-1.5 text-sm placeholder:text-muted-foreground focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50 focus-visible:outline-none"
                  placeholder={prompt.placeholder}
                  value={promptValue}
                  onChange={(e) => setPromptValue(e.target.value)}
                />
                <DialogFooter>
                  <Button type="button" variant="outline" onClick={() => settlePrompt(null)}>
                    Cancel
                  </Button>
                  <Button type="submit">{prompt.confirmLabel ?? "OK"}</Button>
                </DialogFooter>
              </form>
            </DialogContent>
          )}
        </Dialog>
      </PromptContext.Provider>
    </ConfirmContext.Provider>
  );
}
