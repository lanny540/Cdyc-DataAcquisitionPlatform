import { defineConfig } from "@playwright/test";

// P2（用户与角色 / 系统审计 / 部门管理员边界）验证专用配置。
// 与 auth.playwright.config.mjs 分离的原因：
// 1) 必须打开 Auth:DepartmentAdmin:Enabled，否则「部门管理员」角色不可分配；
// 2) 用例串行执行且共享账号数据（先建账号、再用该账号登录），不能并行；
// 3) 端口错开，便于两套用例同时存在于仓库而不互相抢占。
export default defineConfig({
  testDir: ".",
  testMatch: "p2admin.spec.mjs",
  fullyParallel: false,
  workers: 1,
  timeout: 90000,
  expect: { timeout: 20000 },
  use: {
    baseURL: "http://localhost:5078",
    screenshot: "only-on-failure",
    trace: "off"
  },
  projects: [
    { name: "desktop", use: { viewport: { width: 1440, height: 900 } } }
  ],
  webServer: {
    command:
      "dotnet run --no-launch-profile -- --urls http://localhost:5078 --ManagedDataScheduler:Enabled=false --AcquisitionServers:UseMockResponses=true --AcquisitionServers:DefaultRequestTimeoutSeconds=5 --Auth:DepartmentAdmin:Enabled=true",
    url: "http://localhost:5078/login",
    env: { ASPNETCORE_ENVIRONMENT: "Development" },
    reuseExistingServer: false,
    timeout: 180000
  }
});
