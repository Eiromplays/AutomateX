import * as Menu from "@radix-ui/react-dropdown-menu";
import { type ReactNode } from "react";

// Thin shadcn-style wrappers over Radix DropdownMenu, styled with our zinc Tailwind classes.
export const DropdownMenu = Menu.Root;
export const DropdownMenuTrigger = Menu.Trigger;

export function DropdownMenuContent({ children }: { children: ReactNode }) {
  return (
    <Menu.Portal>
      <Menu.Content
        align="end"
        sideOffset={4}
        className="z-50 min-w-40 rounded-md border border-zinc-700 bg-zinc-900 p-1 shadow-xl focus:outline-none"
      >
        {children}
      </Menu.Content>
    </Menu.Portal>
  );
}

export function DropdownMenuItem({
  children,
  onSelect,
  destructive,
}: {
  children: ReactNode;
  onSelect?: () => void;
  destructive?: boolean;
}) {
  return (
    <Menu.Item
      onSelect={onSelect}
      className={`cursor-pointer select-none rounded px-2 py-1.5 text-xs outline-none data-[highlighted]:bg-zinc-800 ${
        destructive ? "text-red-400" : "text-zinc-300"
      }`}
    >
      {children}
    </Menu.Item>
  );
}
