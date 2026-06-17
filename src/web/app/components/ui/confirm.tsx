import { createContext, type ReactNode, useCallback, useContext, useRef, useState } from "react";
import { Dialog, DialogContent } from "./dialog";

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

// Promise-based confirm: `if (await confirm({...})) …`, rendered through the Radix Dialog so
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
        <Dialog open={options != null} onOpenChange={(open) => (open ? undefined : settle(false))}>
          {options && (
            <DialogContent title={options.title}>
              {options.body && <div className="mb-4 text-sm text-zinc-400">{options.body}</div>}
              <div className="flex justify-end gap-2">
                <button
                  type="button"
                  onClick={() => settle(false)}
                  className="rounded-md border border-zinc-700 px-3 py-1.5 text-sm hover:bg-zinc-900"
                >
                  {options.cancelLabel ?? "Cancel"}
                </button>
                <button
                  type="button"
                  onClick={() => settle(true)}
                  className={`rounded-md px-3 py-1.5 text-sm font-medium text-white ${
                    options.destructive
                      ? "bg-red-600 hover:bg-red-500"
                      : "bg-emerald-600 hover:bg-emerald-500"
                  }`}
                >
                  {options.confirmLabel ?? "Confirm"}
                </button>
              </div>
            </DialogContent>
          )}
        </Dialog>

        <Dialog open={prompt != null} onOpenChange={(open) => (open ? undefined : settlePrompt(null))}>
          {prompt && (
            <DialogContent title={prompt.title}>
              <form
                onSubmit={(e) => {
                  e.preventDefault();
                  settlePrompt(promptValue);
                }}
                className="space-y-3"
              >
                {prompt.label && <span className="block text-xs text-zinc-400">{prompt.label}</span>}
                <input
                  autoFocus
                  type={prompt.password ? "password" : "text"}
                  className="w-full rounded-md border border-zinc-700 bg-zinc-900 px-3 py-1.5 text-sm placeholder:text-zinc-600 focus:border-emerald-500 focus:outline-none"
                  placeholder={prompt.placeholder}
                  value={promptValue}
                  onChange={(e) => setPromptValue(e.target.value)}
                />
                <div className="flex justify-end gap-2">
                  <button
                    type="button"
                    onClick={() => settlePrompt(null)}
                    className="rounded-md border border-zinc-700 px-3 py-1.5 text-sm hover:bg-zinc-900"
                  >
                    Cancel
                  </button>
                  <button
                    type="submit"
                    className="rounded-md bg-emerald-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-emerald-500"
                  >
                    {prompt.confirmLabel ?? "OK"}
                  </button>
                </div>
              </form>
            </DialogContent>
          )}
        </Dialog>
      </PromptContext.Provider>
    </ConfirmContext.Provider>
  );
}
