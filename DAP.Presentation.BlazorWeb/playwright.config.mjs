import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: ".",
  testMatch: "ui.spec.mjs",
  fullyParallel: false,
  workers: 1,
  timeout: 45000,
  expect: { timeout: 15000 },
  use: {
    baseURL: "http://localhost:5077",
    screenshot: "only-on-failure"
  },
  projects: [
    { name: "desktop", use: { viewport: { width: 1440, height: 900 } } },
    { name: "compact", use: { viewport: { width: 1280, height: 720 } } },
    { name: "mobile", use: { viewport: { width: 390, height: 844 } } }
  ],
  webServer: {
    command: "dotnet run --no-launch-profile -- --urls http://localhost:5077 --ManagedDataScheduler:Enabled=false --HistoryApi:UseMockResponses=true --HistoryApi:BaseAddress=http://localhost:5077 --HistoryApi:RequestTimeoutSeconds=5",
    url: "http://localhost:5077",
    env: { ASPNETCORE_ENVIRONMENT: "Development" },
    reuseExistingServer: false,
    timeout: 120000
  }
});
