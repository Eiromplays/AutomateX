import { reactRouter } from "@react-router/dev/vite";
import tailwindcss from "@tailwindcss/vite";
import { defineConfig } from "vite";

// Aspire injects the API endpoint for referenced resources.
const apiUrl = process.env.services__api__http__0 ?? "http://localhost:5180";

export default defineConfig({
  plugins: [tailwindcss(), reactRouter()],
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
    },
  },
});
