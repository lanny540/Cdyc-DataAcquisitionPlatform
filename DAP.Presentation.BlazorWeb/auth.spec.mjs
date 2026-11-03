import { test, expect } from "@playwright/test";

// 初始密码来自 appsettings 的 Auth:BootstrapAdmin。
// 改密用例会把密码换成 UPDATED_PASSWORD，重复运行时自动回退到新密码，
// 使整套用例可以反复执行（成功登录会把失败计数清零，不会把自己锁死）。
const ADMIN = "admin";
const INITIAL_PASSWORD = "Admin@123456";
const UPDATED_PASSWORD = "Cdyc@2026dev";

const state = { password: null };

async function submitLogin(page, userName, password) {
  await page.goto("/login");
  await page.locator("#userName").fill(userName);
  await page.locator("#password").fill(password);

  await Promise.all([
    page.waitForURL(url => url.pathname !== "/login" || url.searchParams.has("error"), { timeout: 30000 }),
    page.locator("button.platform-auth-submit").click()
  ]);
}

/**
 * 登录并返回落地地址。首次运行用初始密码，改过密码后用新密码。
 */
async function signIn(page) {
  await submitLogin(page, ADMIN, INITIAL_PASSWORD);

  if (new URL(page.url()).pathname !== "/login") {
    state.password = INITIAL_PASSWORD;
    return new URL(page.url()).pathname;
  }

  await submitLogin(page, ADMIN, UPDATED_PASSWORD);
  state.password = UPDATED_PASSWORD;
  return new URL(page.url()).pathname;
}

test("未登录访问业务页面跳转登录页并带回跳地址", async ({ page }) => {
  await page.goto("/platform-overview");

  const url = new URL(page.url());
  expect(url.pathname).toBe("/login");
  expect(url.searchParams.get("returnUrl")).toBe("/platform-overview");

  // 登录页是极简外壳：有表单，没有顶栏与抽屉，避免用户对着点不动的导航发呆。
  await expect(page.locator(".platform-auth-form")).toBeVisible();
  await expect(page.locator(".platform-appbar")).toHaveCount(0);
  await expect(page.locator(".platform-auth-brand")).toContainText("CDYC 数据采集平台");
});

test("密码错误时停在登录页并给出提示", async ({ page }) => {
  await submitLogin(page, ADMIN, "WrongPassword123");

  expect(new URL(page.url()).pathname).toBe("/login");
  await expect(page.locator(".platform-auth-error")).toContainText("账号或密码不正确");
  // 账号要回填，省得用户重敲一遍。
  await expect(page.locator("#userName")).toHaveValue(ADMIN);
});

test("超管登录、强制改密、进入平台并登出", async ({ page }) => {
  const landing = await signIn(page);

  if (landing === "/change-password") {
    await expect(page.locator(".platform-auth-section-title")).toHaveText("修改密码");

    await page.locator("#currentPassword").fill(state.password);
    await page.locator("#newPassword").fill(UPDATED_PASSWORD);
    await page.locator("#confirmPassword").fill(UPDATED_PASSWORD);

    await Promise.all([
      page.waitForURL(url => url.pathname !== "/change-password", { timeout: 30000 }),
      page.locator("button.platform-auth-submit").click()
    ]);

    state.password = UPDATED_PASSWORD;
  }

  // 超管落地页是平台概览（不在业务域内，不受域授权限制）。
  expect(new URL(page.url()).pathname).toBe("/platform-overview");
  await expect(page.getByRole("heading", { name: "数据采集平台概览" })).toBeVisible();

  // 顶栏账号区读会话 claims，不额外请求接口。
  await expect(page.locator(".platform-user-name")).toHaveText("超级管理员");

  // 顶栏按职能域渲染（P1 域驱动导航）：超管对全部域短路放行，
  // 钞纸/印钞/能源/平台管理四个域都可见，空域带「未上线」标记。
  const topNav = page.locator(".platform-topnav");
  await expect(topNav.locator(".platform-topnav-item")).toHaveCount(4);
  await expect(topNav).toContainText("钞纸");
  await expect(topNav).toContainText("印钞");
  await expect(topNav).toContainText("能源");
  await expect(topNav).toContainText("平台管理");

  // 刷新后仍处于登录态：Cookie 是 HttpOnly 的服务端会话。
  await page.reload();
  expect(new URL(page.url()).pathname).toBe("/platform-overview");
  await expect(page.locator(".platform-user-name")).toHaveText("超级管理员");

  // 切到钞纸域，确认落地页可访问且顶栏高亮跟随。
  await page.locator(".platform-topnav-item", { hasText: "钞纸" }).click();
  await page.waitForURL(url => url.pathname === "/", { timeout: 30000 });
  await expect(page.locator(".platform-topnav-item--active")).toContainText("钞纸");

  // 登出走原生表单 POST，完成后回到登录页。
  await Promise.all([
    page.waitForURL(url => url.pathname === "/login", { timeout: 30000 }),
    page.locator(".platform-logout-button").click()
  ]);

  // 登出后再访问业务页面会被闸门拦回登录页。
  await page.goto("/");
  expect(new URL(page.url()).pathname).toBe("/login");
});

test("超管可打开域与页面配置页并看到四个域", async ({ page }) => {
  await signIn(page);
  await page.goto("/domain-config");

  // 页头与域卡片：四个域齐全，空域呈现「未上线」标记。
  await expect(page.getByRole("heading", { name: "域与页面配置" })).toBeVisible();
  await expect(page.locator(".platform-section-card--tinted", { hasText: "钞纸" })).toBeVisible();
  await expect(page.locator(".platform-section-card--tinted", { hasText: "印钞" })).toContainText("空域（未上线）");
  await expect(page.locator(".platform-section-card--tinted", { hasText: "平台管理" })).toContainText("系统域");
});

test("超管可打开部门与授权页并新建部门", async ({ page }) => {
  await signIn(page);
  await page.goto("/department-grants");

  await expect(page.getByRole("heading", { name: "部门与授权" })).toBeVisible();

  // 新建部门走抽屉表单：编码 + 名称，保存后列表出现新行。
  // 编码带时间戳保证用例可重复执行（已存在的编码会被服务端拒绝）。
  const departmentCode = `PW-${Date.now().toString(36).toUpperCase()}`;
  await page.getByRole("button", { name: "新建部门" }).click();
  await page.getByLabel("部门编码").fill(departmentCode);
  await page.getByLabel("部门名称").fill("浏览器实测部门");
  await page.getByRole("button", { name: "保存", exact: true }).click();

  await expect(page.locator(".platform-data-table", { hasText: "浏览器实测部门" }))
    .toBeVisible({ timeout: 30000 });
});
