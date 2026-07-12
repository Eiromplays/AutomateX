# v5.1 — UI modernization (design)

Modernizes the web chrome **incrementally** — no rewrite. The app works (React 19 + React Router 8
framework mode + Tailwind v4 + Radix + TanStack Query + React Flow + SignalR); the shell is dated, it isn't
mobile-responsive, and the top nav is nine flat items. v5.1 adopts **shadcn/ui** — which sits on the exact
Radix + Tailwind v4 + lucide stack already in `package.json` — and tightens the information architecture,
landing slice by slice. Each slice is shippable and must not regress existing pages, the React Flow editor
(`workflow-canvas.tsx`, `workflow-graph.tsx`), or the SignalR live views (`lib/use-engine-events.ts`,
`lib/use-plugin-logs.ts`).

## What & why

The web app is functional but dated in three concrete ways. **Chrome:** `app.css` is a bare
`@import "tailwindcss"` — no design tokens; every surface hard-codes zinc/emerald Tailwind classes (≈880
colour literals across `app/`, 600 of them `zinc-*`), so there's no single knob for the theme and the look
drifts per page. **Mobile:** the shell in `root.tsx` is a fixed horizontal `<nav>` with nine `NavLink`s
inside a `max-w-5xl` container; below ~768px it overflows and there is no drawer — the app is desktop-only by
omission. **Clutter:** nine peer nav items (Dashboard, Workflows, Executions, Connections, Variables,
Plugins, Audit, Workspace, API keys) with no grouping; unrelated admin surfaces sit at the same level as the
daily-driver views.

shadcn/ui is the right lever because it is **not a component dependency** — it copies Radix-based source into
`app/components/ui/` that we own and restyle, on the same primitives we already ship (`@radix-ui/react-dialog`
1.1.17, `@radix-ui/react-dropdown-menu` 2.1.18, `lucide-react`, Tailwind v4). Its CLI now targets Tailwind v4
+ React 19 + React Router framework mode directly. So adoption is **additive and reversible**: we install the
token layer, then migrate components as we touch them. The design is built to **degrade to today** — tokens
map to the current zinc/emerald values, so day one looks identical and every slice ships without a visual
regression.

Two hard constraints frame every slice: the **React Flow editor** and the **SignalR live views** are correct
and must stay untouched at the wiring level. This arc changes chrome (shell, tokens, dialogs, menus, toasts)
and IA (grouping + tabs). It does not touch the canvas internals or the realtime data hooks.

## Shape

### shadcn adoption for our exact stack (additive, coexisting)

- **Init, once.** `pnpm dlx shadcn@latest init` against the existing project. Style `new-york` (the `default`
  style is deprecated), base colour neutral, OKLCH, lucide icons (already our set), CSS-variables theming on.
  `components.json` records the aliases and leaves `tailwind.config` **blank** — Tailwind v4 has no JS config.
- **Path alias.** shadcn emits `~/components/ui/*` and `~/lib/utils` imports; we currently use relative paths
  and define no alias. Add `~/* → ./app/*` to `tsconfig.json` `paths` and wire it into Vite (`vite-tsconfig-paths`,
  the standard React-Router-template resolver) so `~` resolves at build and typecheck. Existing relative
  imports keep working — the alias is additive.
- **`cn` helper + deps.** shadcn components use `cn()` (`clsx` + `tailwind-merge`) at `app/lib/utils.ts`, and
  `tw-animate-css` for enter/exit transitions (it replaced `tailwindcss-animate`). These are the only new
  runtime deps; Radix and lucide are already present.
- **Tokens + the existing dark theme.** Today "dark" is hard-coded on `<body>` (`bg-zinc-950 text-zinc-100`)
  with no `.dark` class. shadcn's model is CSS variables under `:root` / `.dark` mapped through
  `@theme inline` in `app.css`. We're a **dark-only** app, so we set `class="dark"` permanently on `<html>` in
  `Layout` and let the `.dark` block carry the palette. Crucially we **tune the tokens to current values** —
  `--background` ≈ zinc-950, `--foreground` ≈ zinc-100, `--primary` = the emerald accent, `--border` ≈ zinc-800
  — so shadcn components drop in matching the hand-rolled surfaces and nothing visually shifts. A light theme
  becomes a later freebie but is out of scope.
- **Coexistence strategy.** shadcn source lands alongside the hand-rolled components in the *same*
  `app/components/ui/` directory. New primitives (Button, Card, Input, Select, Tabs, Sheet, Badge) are added
  as needed; existing components are migrated **only when a slice touches them**. There is no flag day — the
  hand-rolled and shadcn components render side by side, unified by the shared token layer, until each is
  replaced on its own slice.

### Direct win: replace the hand-rolled Dialog + DropdownMenu first

The only two files importing Radix directly are `app/components/ui/dialog.tsx` and
`app/components/ui/dropdown-menu.tsx` — thin, self-described "shadcn-style" wrappers with a reduced API
(`DialogContent` takes a `title` prop; the close button is a literal `✕`; no animations, no focus-ring
tokens). shadcn's `dialog` / `dropdown-menu` / `alert-dialog` are the same primitives done fully: `data-slot`
styling, `tw-animate-css` transitions, portal/overlay tokens, complete a11y. Swapping the internals is
contained because the blast radius is already mapped: ≈12 files consume these wrappers or the
`useConfirm`/`usePrompt` helpers built on them (`confirm.tsx`, `root.tsx`, and the connection/variable/
plugin/execution/workflow/workspace/api-key routes). We keep the **ergonomic hooks** (`useConfirm`,
`usePrompt`) as the public surface — reimplemented over shadcn `AlertDialog` (confirm) and `Dialog` (prompt)
— so call sites barely change while the underlying component becomes real shadcn.

### Mobile: a responsive app shell with a nav drawer

- **The shell becomes responsive, data-driven.** The nine `NavLink`s are extracted into a nav-item array
  consumed by two renderers: an inline horizontal bar at `md:` and up (today's look), and a **Sheet** drawer
  below `md`, opened by a hamburger `Button` in a compact top bar (logo + workspace switcher + hamburger).
  shadcn `Sheet` is a Radix Dialog under the hood — same primitive family, a11y for free, closes on route
  change. Container padding goes responsive (`px-4 md:px-6`); `max-w-5xl` stays.
- **High-traffic pages audited at narrow widths.** Dashboard, the Workflows list, the Executions list, and
  Execution detail get a pass for overflow, tap targets, and tables that need to stack. This is layout-class
  work (`grid`/`flex` breakpoints, `overflow-x-auto`), not re-architecture.
- **The React Flow editor is explicitly not made touch-editable here.** On mobile the canvas stays
  pan/read-only; full mobile authoring is deferred. We only ensure it doesn't break the shell (no horizontal
  bleed) and that its own styling is untouched by the token layer.

### Less clutter: sections + tabs, URLs unchanged

Collapse nine top-level items to **six** by grouping the admin/resource surfaces:

- **Top level:** Dashboard, Workflows, Executions, **Resources**, Plugins, **Settings**.
- **Resources** groups the two workspace-resource surfaces the brief calls out — **Connections** and
  **Variables** — behind a shared tab strip.
- **Settings** groups the config/admin surfaces — **Workspace**, **API keys**, **Audit** — behind a shared
  tab strip.
- **Plugins stays top-level.** It's a heavy, distinct daily surface and v5.2 turns it into a browsable
  catalog; folding it now only to unfold it next arc is churn.
- **Templates** stays an unlisted route (reached from the workflow-create flow), as today.

The grouping is implemented with **React Router pathless layout routes** (`layout(...)`), not URL changes:
`/connections`, `/variables`, `/audit`, `/workspace`, `/api-keys` keep their paths, so **no broken bookmarks
and no redirects**. Each layout renders a shadcn `Tabs` header over its children; the top-nav "Resources" /
"Settings" entries point at the section's first child and highlight for any child route. This is the
incremental IA move — regroup the presentation without migrating the routing tree.

### What stays untouched (guardrails)

- **React Flow** (`workflow-canvas.tsx`, `workflow-graph.tsx`, `@xyflow/react`): no internal changes. Token
  work must not collide with React Flow's own CSS; verified by exercising the editor after m1.
- **SignalR hooks** (`use-engine-events.ts`, `use-plugin-logs.ts`): pure data, no UI. Untouched. The pages
  that render their output (Executions, Execution detail, Plugins) change visually only — the subscription
  wiring is preserved.
- **TanStack Query, API layer** (`lib/api.ts`): untouched. This arc is chrome + IA, not data or fetching.

## Slice plan

Workflow mirrors v5.0: I design + generate code; you build/run/lint/test, commit locally, report back. One
commit per slice; frontend gate every slice — `pnpm test`, `pnpm typecheck`, `pnpm lint`, `pnpm build` (biome
+ tsc + vitest + react-router build) — plus a manual narrow-width check where noted. Every slice keeps all
existing pages working.

**m1 — shadcn foundation + design tokens (additive, no component swaps).**
`shadcn init` (`new-york`, OKLCH, CSS vars); add `components.json`, the `~/* → ./app/*` alias (tsconfig +
`vite-tsconfig-paths`), `app/lib/utils.ts` (`cn`), deps `clsx` + `tailwind-merge` + `tw-animate-css`. Author
tokens in `app.css` (`:root`/`.dark` + `@theme inline`) tuned to the current zinc/emerald values; set
`class="dark"` on `<html>`. Add `Button` (and `Card`/`Input` scaffolding) but **replace nothing** — purely
additive. *Verify:* gate green; app renders pixel-close to today; open the React Flow editor and confirm no
style collision. *Commit:* `feat(web): adopt shadcn/ui foundation + design tokens (v5.1 m1)`.

**m2 — migrate Dialog + DropdownMenu (+ confirm/prompt) to shadcn (direct win).**
`shadcn add dialog dropdown-menu alert-dialog`. Reimplement `ui/dialog.tsx` and `ui/dropdown-menu.tsx` on the
shadcn components; rebuild `useConfirm`/`usePrompt` over `AlertDialog`/`Dialog` keeping their signatures.
Update the ≈12 consumers to the (near-identical) API. *Verify:* gate green; every confirm/prompt/menu across
connections, variables, plugins, executions, workflow-detail, workspace, api-keys opens, focus-traps, and
closes. *Commit:* `feat(web): migrate dialogs + menus to shadcn (v5.1 m2)`.

**m3 — responsive app shell + mobile nav drawer.**
Refactor `root.tsx`: extract nav items to an array; render inline nav at `md:+` and a `Sheet` drawer below
`md` behind a hamburger; responsive container padding. Same nine items still (IA regroup is m4) — this slice
is responsiveness only. *Verify:* gate green; at 375px the nav is a working drawer, at ≥768px it's unchanged;
drawer closes on navigation. *Commit:* `feat(web): responsive app shell with mobile nav drawer (v5.1 m3)`.

**m4 — IA regroup: Resources + Settings sections with tabs.**
Add pathless `layout` routes wrapping Connections+Variables (Resources) and Workspace+API keys+Audit
(Settings), each with a shadcn `Tabs` header; collapse the top nav to six entries. **No URL changes, no
redirects.** *Verify:* gate green; every old URL still resolves and deep-links land on the right tab;
active-state highlights the section for child routes. *Commit:* `feat(web): regroup nav into sections + tabs
(v5.1 m4)`.

**m5 — Sonner toasts + shared status/skeleton primitives.**
Replace the hand-rolled `toast.tsx` with shadcn **Sonner**, keeping a `toast.success/error` shim so call
sites are unchanged; adopt shadcn `Skeleton` and `Badge` behind the existing `Skeleton`/`ListSkeleton`/
`StatusBadge` components. *Verify:* gate green; success/error toasts fire from existing call sites; loading
and status states render. *Commit:* `feat(web): sonner toasts + shared status primitives (v5.1 m5)`.

**m6 — responsive audit of high-traffic pages.**
Narrow-width pass over Dashboard, Workflows list, Executions list, Execution detail: stack/scroll tables,
fix tap targets and overflow. React Flow editor stays pan/read-only on mobile (no authoring). *Verify:* gate
green; the four pages are usable at 375px with no horizontal bleed; the SignalR live updates still stream on
Executions/Execution detail. *Commit:* `feat(web): mobile-responsive high-traffic pages (v5.1 m6)`.

## Calls made

- **shadcn/ui, not a component library.** It copies source we own onto the Radix+Tailwind+lucide stack we
  already ship — no new abstraction, reversible, restyleable. A dependency like MUI/Mantine would fight our
  Tailwind surfaces and duplicate the Radix we already have.
- **Additive coexistence, tokens first.** Land the CSS-variable layer tuned to current zinc/emerald so day one
  is visually unchanged; migrate components only when a slice touches them. No flag-day rewrite — the brief's
  "incremental, not big-bang" is the whole point.
- **Dark-only via a permanent `.dark` on `<html>`.** Matches today's hard-coded dark; keeps one palette;
  leaves light mode as a later freebie without carrying a theme switcher now.
- **Dialog/DropdownMenu first — they're isolated.** The only two files importing Radix directly; ≈12
  well-mapped consumers behind stable hooks. Highest ratio of modernization to risk, so it goes early (m2).
- **Keep `useConfirm`/`usePrompt`/`toast` as shims.** Preserve the ergonomic call-site API over shadcn
  internals so migrations don't ripple across every route.
- **Group with pathless layout routes, keep URLs.** Regroup the *presentation* (nav + tabs) without touching
  the routing tree or breaking bookmarks — no redirects to maintain. Reparenting to `/settings/*` was
  considered and rejected as breakage with no user benefit this arc.
- **Six top-level items; Plugins stays top-level.** Resources = Connections+Variables, Settings =
  Workspace+API keys+Audit (the brief's open question, resolved). Plugins is heavy and v5.2 will overhaul it
  — folding then unfolding is churn.
- **`new-york` + OKLCH + `tw-animate-css`, no `tailwind.config`.** The current shadcn defaults for Tailwind
  v4; `default` style and `tailwindcss-animate` are deprecated.
- **React Flow + SignalR are guardrails, not scope.** Chrome and IA only. The canvas internals and the
  realtime hooks are verified-unchanged each slice, never edited.
- **Full-component replacement is opportunistic, not a slice.** Inputs, cards, selects, and per-page forms get
  shadcn as pages are touched — gating a "replace everything" slice would be over-engineering with no
  shippable boundary.
- **Commit scopes:** all six slices `feat(web): … (v5.1 mN)`; `CHANGELOG` entry lands with m6.

## Deferred

- **Plugin catalog overhaul** (browsable/shareable catalog, schema, third-party contributions) — its own arc,
  **v5.2**, a backend + product feature, explicitly out of this UI scope.
- **Full shadcn migration of every input/card/select/form** — ongoing and opportunistic, not a gated slice.
- **Light theme / theme switcher** — the token layer makes it cheap later; unneeded now.
- **Touch-editable React Flow authoring on mobile** — canvas stays pan/read-only; mobile editing is a
  separate effort.
- **URL restructure to `/resources/*` and `/settings/*`** — only if flat URLs later prove worth breaking;
  redirects would be required.
- **Command palette (⌘K) and breadcrumbs** — plausible IA follow-ups, not v5.1.
