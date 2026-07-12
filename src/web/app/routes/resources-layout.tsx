import { Link, Outlet, useLocation } from "react-router";
import { Tabs, TabsList, TabsTrigger } from "../components/ui/tabs";

// Workspace resources grouped behind one nav entry. The URLs stay flat
// (/connections, /variables) — this pathless layout only adds the tab strip.
const TABS = [
  { to: "/connections", label: "Connections" },
  { to: "/variables", label: "Variables" },
];

export default function ResourcesLayout() {
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
