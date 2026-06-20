# AutomateX UI — conventions

AutomateX is a React 19 + React Router app styled with **Tailwind CSS v4**. There
is no theme provider and no CSS-in-JS — every component is styled with Tailwind
utility classes, and the design language lives in the class vocabulary below.
Import components from `window.AutomateX` (the bundle is `_ds_bundle.js`).

## Canvas & wrapping

The app runs on a **dark zinc canvas with an emerald accent**. Render screens
inside a dark shell — this is what `app/root.tsx` does:

```jsx
<body className="min-h-screen bg-zinc-950 text-zinc-100 antialiased">
  {/* page content */}
</body>
```

Without that wrapper, text defaults to near-black on near-black and panels lose
contrast. Components that draw their own surface (CodeBlock, DialogContent,
DropdownMenuContent, Toasts) already use `bg-zinc-900`, so they read correctly,
but body text, labels, and layout glue should be set against `bg-zinc-950`.

Setup notes for specific components:

- **Overlays — `DialogContent`, `DropdownMenuContent`** are Radix-based and
  render through a portal. Compose them inside their root + trigger:
  `<Dialog><DialogTrigger/><DialogContent/></Dialog>` and
  `<DropdownMenu><DropdownMenuTrigger/><DropdownMenuContent>…<DropdownMenuItem/></DropdownMenuContent></DropdownMenu>`.
  No provider needed — the root component supplies context.
- **`ConfirmProvider`** wraps the app (or a subtree) so descendants can call
  `useConfirm()` / `usePrompt()` for promise-based confirm and text-prompt
  dialogs: `if (await confirm({ title: "Delete?" })) …`.
- **`Toasts`** is mounted once in the shell; fire notifications from anywhere
  with the imperative helper — `toast.success("Saved.")` / `toast.error("…")`.

## Styling idiom — Tailwind v4 utility classes

Style with utility classes (no class props on a theme object, no `styled`).
The system's actual vocabulary:

| Role | Classes used |
|---|---|
| Surfaces | `bg-zinc-950` (canvas), `bg-zinc-900` (panels/inputs/menus) |
| Borders | `border border-zinc-700` (controls), `border-zinc-800` (dividers) |
| Text | `text-zinc-100` (primary), `text-zinc-300`/`text-zinc-400` (secondary), `text-zinc-500`/`text-zinc-600` (muted) |
| Accent (primary action) | `bg-emerald-600 hover:bg-emerald-500`, `text-emerald-400`, `focus:border-emerald-500` |
| Destructive | `bg-red-600 hover:bg-red-500`, `text-red-400` |
| Status tints | `bg-emerald-500/15 text-emerald-400`, `bg-amber-500/15 text-amber-400`, `bg-red-500/15 text-red-400` |
| Radius / shape | `rounded-md` (controls), `rounded-lg` (dialogs), `rounded-full` (pills) |
| Typography | `text-sm` body, `text-xs` labels/meta, `font-medium`/`font-semibold` |

Primary button pattern: `rounded-md bg-emerald-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-emerald-500`.
Input pattern: `rounded-md border border-zinc-700 bg-zinc-900 px-3 py-1.5 text-sm text-zinc-100 focus:border-emerald-500 focus:outline-none`.

## Where the truth lives

- `styles.css` (and the `_ds_bundle.css` it imports) — the compiled Tailwind
  utilities and the dark-canvas defaults.
- Each component's `<Name>.prompt.md` (usage) and `<Name>.d.ts` (props) under
  `components/general/<Name>/`.

## One idiomatic snippet

```jsx
const { StatusBadge, CodeBlock } = window.AutomateX;

<div className="rounded-lg border border-zinc-800 bg-zinc-950 p-4">
  <div className="flex items-center justify-between">
    <span className="text-sm font-medium text-zinc-100">Nightly data sync</span>
    <StatusBadge status="Running" />
  </div>
  <CodeBlock text={`{ "recordsWritten": 128 }`} />
</div>
```
