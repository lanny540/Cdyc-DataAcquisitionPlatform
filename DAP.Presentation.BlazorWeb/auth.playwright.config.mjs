import { defineConfig } from "@playwright/test";

// 认证流程验证专用配置：单浏览器、单 worker，按声明顺序跑完整条链路。
// 超管锁定策略会累计失败次数，因此不能像 ui.spec 那样铺三个视口。
export default defineConfig({
  testDir: ".",
  testMatch: "auth.spec.mjs",
  fullyParallel: false,
  workers: 1,
  timeout: 60000,
  expect: { timeout: 15000 },
  use: {
    baseURL: "http://localhost:5077",
    screenshot: "only-on-failure",
    trace: "off"
  },
  projects: [
    { name: "desktop", use: { viewport: { width: 1440, height: 900 } } }
  ],
  webServer: {
    command:
      "dotnet run --no-launch-profile -- --urls http://localhost:5077 --ManagedDataScheduler:Enabled=false --AcquisitionServers:UseMockResponses=true --AcquisitionServers:DefaultRequestTimeoutSeconds=5",
    url: "http://localhost:5077/login",
    env: { ASPNETCORE_ENVIRONMENT: "Development" },
    reuseExistingServer: false,
    timeout: 180000
  }
});
