# AutomateX v5.1.0 — UI modernization

A **web UI modernization** — incremental, not a rewrite. The web app moves onto **shadcn/ui** (owned source
on the Radix + Tailwind v4 + lucide stack it already used), becomes **mobile-responsive**, and its navigation
is regrouped from nine flat items to six sections. **Frontend-only and non-breaking:** no EF migration, no API
or config changes, and the React Flow editor and SignalR live views are untouched.

Full notes: [CHANGELOG.md](CHANGELOG.md) · design:
[docs/ui-modernization-design.md](docs/ui-modernization-design.md).

## Highlights

- **Design tokens + shadcn foundation.** A CSS-variable token layer (`:root`/`.dark` + `@theme inline`) tuned
  to the existing zinc/emerald palette — `--background` is zinc-950 exactly, so nothing shifts visually — plus
  `components.json`, a `~ → ./app` alias, a `cn` helper, and `tw-animate-css`. Dark-only for now (a light
  theme is a cheap follow-up).
- **Owned shadcn components.** Dialog, DropdownMenu, AlertDialog, Sheet, Tabs, Badge, Button, Skeleton, and
  Sonner toasts replace the hand-rolled primitives. `useConfirm`/`usePrompt` and `toast.success/error` keep
  their signatures (shims), so call sites were untouched.
- **Responsive shell.** The top nav collapses into a drawer below `md` behind a hamburger; the high-traffic
  pages (Dashboard, Workflows, Executions, Execution detail) were audited at narrow widths. The workflow
  editor stays pan/read-only on mobile.
- **Fewer nav items (9 → 6).** Connections + Variables group under **Resources**; Workspace + API keys + Audit
  under **Settings** — each behind a tab strip. Grouping uses pathless React Router layout routes, so **every
  URL is unchanged and there are no redirects**.
- **Plugins stays top-level.** Its catalog overhaul is a separate **v5.2** arc.

## Upgrading

**Nothing to do.** This is a frontend-only release — no migration, no config, no compose changes. Pull the
refreshed `automatex-web` image (or rebuild); the API image is unchanged apart from the version tag. Every
existing URL still resolves, so bookmarks and deep links keep working.
