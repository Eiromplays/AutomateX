import { Link, Outlet, useLocation } from "react-router";
import { Tabs, TabsList, TabsTrigger } from "../components/ui/tabs";

// Admin/config surfaces grouped behind one nav entry. The URLs stay flat
// (/workspace, /api-keys, /audit) — this pathless layout only adds the tab strip.
const TABS = [
  { to: "/workspace", label: "Workspace" },
  { to: "/api-keys", label: "API keys" },
  { to: "/audit", label: "Audit" },
];

export default function SettingsLayout() {
  const { pathname } = useLocation();
  const active = TABS.find((t) => pathname === t.to || pathname.startsWith(`${t.to}/`))?.to ?? TABS[0].to;

  return (
    <div className="space-y-6">
      <Tabs value={active}>
        <TabsList variant="line">
          {TABS.map((t) => (
            <TabsTrigger key={t.to} value={t.to} asChild>
              <Link to={t.to}>{t.label}</Link>
            </TabsTrigger>
          ))}
        </TabsList>
      </Tabs>
      <Outlet />
    </div>
  );
}
