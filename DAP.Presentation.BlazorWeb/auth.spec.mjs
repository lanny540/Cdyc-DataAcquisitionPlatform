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

  // 三个顶级模块都在：超管对全部域短路放行。
  const topNav = page.locator(".platform-topnav");
  await expect(topNav.locator(".platform-topnav-item")).toHaveCount(3);
  await expect(topNav).toContainText("数据指标");
  await expect(topNav).toContainText("业务配置");
  await expect(topNav).toContainText("平台管理");

  // 刷新后仍处于登录态：Cookie 是 HttpOnly 的服务端会话。
  await page.reload();
  expect(new URL(page.url()).pathname).toBe("/platform-overview");
  await expect(page.locator(".platform-user-name")).toHaveText("超级管理员");

  // 换到数据指标模块，确认落地页可访问且顶栏高亮跟随。
  await page.locator(".platform-topnav-item", { hasText: "数据指标" }).click();
  await page.waitForURL(url => url.pathname === "/", { timeout: 30000 });
  await expect(page.locator(".platform-topnav-item--active")).toContainText("数据指标");

  // 登出走原生表单 POST，完成后回到登录页。
  await Promise.all([
    page.waitForURL(url => url.pathname === "/login", { timeout: 30000 }),
    page.locator(".platform-logout-button").click()
  ]);

  // 登出后再访问业务页面会被闸门拦回登录页。
  await page.goto("/");
  expect(new URL(page.url()).pathname).toBe("/login");
});
