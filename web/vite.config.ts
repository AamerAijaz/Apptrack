import { existsSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

// APPTRACK_API_ORIGIN is read here only. It is not VITE_-prefixed, so Vite
// does not inline it into the page bundle.
const apiOrigin = process.env.APPTRACK_API_ORIGIN ?? "http://localhost:5028";

// One-time setup so the dev server is HTTPS and the Secure oauth_state cookie
// is stored for localhost (cookies are not scoped by port, so the callback on
// http://localhost:5028 still receives it):
//   dotnet dev-certs https --trust
//   dotnet dev-certs https --export-path web/.cert/localhost.pem --format Pem --no-password
// The export writes web/.cert/localhost.pem and web/.cert/localhost.key.
function devHttps() {
  const certPath = fileURLToPath(new URL("./.cert/localhost.pem", import.meta.url));
  const keyPath = fileURLToPath(new URL("./.cert/localhost.key", import.meta.url));
  if (!existsSync(certPath) || !existsSync(keyPath)) {
    throw new Error(
      "Missing web/.cert/localhost.pem or web/.cert/localhost.key. Export the ASP.NET dev cert with: dotnet dev-certs https --export-path web/.cert/localhost.pem --format Pem --no-password",
    );
  }
  return {
    cert: readFileSync(certPath),
    key: readFileSync(keyPath),
  };
}

const isVitest = process.env.VITEST === "true";
const isBuild = process.argv.includes("build");

export default defineConfig({
  plugins: [react()],
  server:
    isVitest || isBuild
      ? undefined
      : {
          port: 5173,
          strictPort: true,
          https: devHttps(),
          proxy: {
            "/api": {
              target: apiOrigin,
              secure: false,
            },
          },
        },
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: "./src/test-setup.ts",
  },
});
