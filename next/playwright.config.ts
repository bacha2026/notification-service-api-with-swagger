import { defineConfig, devices } from "@playwright/test";

const port = 3100;
const externalBaseUrl = process.env.PLAYWRIGHT_BASE_URL;
const baseURL = externalBaseUrl ?? `http://127.0.0.1:${port}`;

export default defineConfig({
  testDir: "./tests/e2e",
  fullyParallel: false,
  workers: 1,
  timeout: 45_000,
  expect: {
    timeout: 10_000,
  },
  reporter: process.env.CI ? [["line"], ["html", { open: "never" }]] : "list",
  use: {
    baseURL,
    trace: "retain-on-failure",
    ...devices["Desktop Chrome"],
  },
  webServer: externalBaseUrl
    ? undefined
    : {
        command: "node .next/standalone/server.js",
        env: {
          HOSTNAME: "127.0.0.1",
          NODE_ENV: "production",
          PORT: String(port),
        },
        url: `${baseURL}/rendering`,
        reuseExistingServer: !process.env.CI,
        timeout: 120_000,
      },
});
