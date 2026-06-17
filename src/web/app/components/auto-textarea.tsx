import { type TextareaHTMLAttributes, useLayoutEffect, useRef } from "react";

// A textarea that grows to fit its content. Set a min height via a className utility (e.g.
// `min-h-[4.5rem]`); CSS min-height acts as the floor while the inline height tracks scrollHeight.
export function AutoTextarea({ value, className, ...rest }: TextareaHTMLAttributes<HTMLTextAreaElement>) {
  const ref = useRef<HTMLTextAreaElement>(null);

  useLayoutEffect(() => {
    const el = ref.current;
    if (!el) return;
    el.style.height = "auto";
    el.style.height = `${el.scrollHeight}px`;
  }, [value]);

  return (
    <textarea
      ref={ref}
      value={value}
      className={`resize-none overflow-hidden ${className ?? ""}`}
      {...rest}
    />
  );
}
