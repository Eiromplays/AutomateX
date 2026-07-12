import { fileURLToPath } from "node:url";
import { reactRouter } from "@react-router/dev/vite";
import tailwindcss from "@tailwindcss/vite";
import { defineConfig } from "vite";

// Aspire injects the API endpoint for referenced resources.
const apiUrl = process.env.services__api__http__0 ?? "http://localhost:5180";

export default defineConfig({
  plugins: [tailwindcss(), reactRouter()],
  // `~` → ./app so shadcn's generated imports (~/lib/utils, ~/components/ui/*) resolve.
  // A plain alias keeps it to one line and avoids a tsconfig-paths plugin dep.
  resolve: {
    alias: { "~": fileURLToPath(new URL("./app", import.meta.url)) },
  },
  // Pre-bundle everything up front so Vite never re-optimizes mid-session
  // (mixed-pass chunks cause "Cannot read properties of null (reading 'useContext')").
  optimizeDeps: {
    include: ["react", "react-dom", "react-router", "@tanstack/react-query", "@microsoft/signalr"],
  },
  server: {
    port: Number(process.env.PORT) || 5173,
    proxy: {
      "/api": { target: apiUrl, changeOrigin: true },
      "/hubs": { target: apiUrl, changeOrigin: true, ws: true },
      // OIDC flow endpoints live on the API. changeOrigin stays OFF so the handler
      // sees the browser's Host and builds redirect URIs on the web origin —
      // the whole login loop then stays on this port.
      "/auth": { target: apiUrl },
      "/signin-oidc": { target: apiUrl },
      "/signout-callback-oidc": { target: apiUrl },
    },
  },
});
