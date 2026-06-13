import { defineConfig } from "vitest/config";

// Standalone from vite.config.ts: the React Router plugin isn't needed (and interferes) for
// the pure-logic unit tests. esbuild's automatic JSX lets us import .tsx modules without
// rendering them.
export default defineConfig({
  esbuild: { jsx: "automatic" },
  test: {
    environment: "node",
    include: ["app/**/*.test.ts"],
  },
});
