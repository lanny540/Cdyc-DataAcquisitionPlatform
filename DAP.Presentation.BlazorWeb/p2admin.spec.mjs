import { test, expect } from "@playwright/test";

// P2 端到端验证：用户与角色页、系统审计页、部门管理员边界（docs/AUTH_DESIGN.md 9.2–9.4）。
//
// 数据准备走 HTTP 接口（比在界面上逐格点选稳定），界面只验证「人能看到、能点、能生效」。
// 账号名带时间戳，用例可反复执行；部门复用库里已启用的第一个部门，
// 没有就自己建一个并授予钞纸域——部门管理员必须归属部门才管得到人。

const STAMP = Date.now().toString(36);
const PLAIN_USER = `pw_user_${STAMP}`;
const DEPT_ADMIN_USER = `pw_dept_${STAMP}`;
const PLAIN_PASSWORD = "PwUser@2026";
const DEPT_ADMIN_PASSWORD = "PwDept@2026";
const DEPT_ADMIN_PASSWORD_NEW = "PwDept2@2026";

// 超管初始密码可能已被 auth.spec 改过：按顺序试探，成功即停。
// 失败一次会累计锁定计数，故列表不能太长（阈值 5 次）。
const ADMIN_PASSWORDS = ["Admin@123456", "Cdyc@2026dev", "Playwright@2026"];

const state = { departmentId: null, departmentName: null };

/**
 * 以超管身份登录给定的 API 上下文，返回真正生效的密码。
 */
async function apiSignInAdmin(api) {
  for (const password of ADMIN_PASSWORDS) {
    await api.post("/api/auth/login", { form: { userName: "admin", password } });
    const me = await api.get("/api/auth/me");

    if (!me.ok()) {
      continue;
    }

    const body = await me.json();

    if (body.userName === "admin") {
      return password;
    }
  }

  throw new Error("超管登录失败：候选密码全部无效。");
}

/**
 * 保证库中存在一个已启用、且已授予钞纸域的部门，返回其标识与名称。
 * 部门管理员必须归属部门，否则用户管理页看到的是空列表。
 */
async function ensureDepartment(api) {
  const departments = await (await api.get("/api/admin/departments")).json();
  const enabled = departments.find(item => item.isEnabled);

  if (enabled) {
    if (enabled.grantedDomainIds.length === 0) {
      await grantPaperDomain(api, enabled.id);
    }

    return enabled;
  }

  const code = `PW-${STAMP.toUpperCase()}`;
  const created = await api.post("/api/admin/departments", {
    data: { code, name: "浏览器实测部门", isEnabled: true }
  });

  if (!created.ok()) {
    throw new Error(`新建部门失败：${created.status()} ${await created.text()}`);
  }

  const refresh = await (await api.get("/api/admin/departments")).json();
  const department = refresh.find(item => item.code === code);

  if (!department) {
    throw new Error("新建部门后未能查到记录。");
  }

  await grantPaperDomain(api, department.id);
  return department;
}

async function grantPaperDomain(api, departmentId) {
  const domains = await (await api.get("/api/admin/domains")).json();
  const paper = domains.find(item => item.code === "banknote-paper");

  await api.put(`/api/admin/departments/${departmentId}/grants`, {
    data: { domainIds: [paper.id] }
  });
}

/**
 * 直接建账号。RoleIds 由调用方传，省得再查一次。
 */
async function createUser(api, { userName, displayName, password, departmentId, roleIds }) {
  const response = await api.post("/api/admin/users", {
    data: { userName, displayName, initialPassword: password, departmentId, roleIds }
  });

  if (!response.ok()) {
    throw new Error(`新建账号 ${userName} 失败：${response.status()} ${await response.text()}`);
  }
}

/**
 * 表单登录。返回落地地址，便于判断是否被强制改密。
 */
async function signIn(page, userName, password) {
  await page.goto("/login");
  await page.locator("#userName").fill(userName);
  await page.locator("#password").fill(password);

  await Promise.all([
    page.waitForURL(url => url.pathname !== "/login" || url.searchParams.has("error"), { timeout: 30000 }),
    page.locator("button.platform-auth-submit").click()
  ]);

  return new URL(page.url()).pathname;
}

test.beforeAll(async ({ playwright }) => {
  const api = await playwright.request.newContext({ baseURL: "http://localhost:5078" });

  await apiSignInAdmin(api);

  const department = await ensureDepartment(api);
  state.departmentId = department.id;
  state.departmentName = department.name;

  const roles = await (await api.get("/api/admin/users/roles")).json();
  const viewer = roles.find(role => role.code === "viewer");
  const departmentAdmin = roles.find(role => role.code === "department-admin");

  if (!departmentAdmin) {
    throw new Error("部门管理员角色不可用：请确认启动参数含 Auth:DepartmentAdmin:Enabled=true。");
  }

  await createUser(api, {
    userName: PLAIN_USER,
    displayName: "浏览器实测用户",
    password: PLAIN_PASSWORD,
    departmentId: department.id,
    roleIds: [viewer.id]
  });

  await createUser(api, {
    userName: DEPT_ADMIN_USER,
    displayName: "浏览器实测部门管理员",
    password: DEPT_ADMIN_PASSWORD,
    departmentId: department.id,
    roleIds: [departmentAdmin.id]
  });

  // 制造一条失败审计：超管尝试停用自己会被服务端拒绝，且必须留痕（result = failed）。
  const me = await (await api.get("/api/auth/me")).json();
  await api.post("/api/admin/users/batch-status", { data: { userIds: [me.id], isEnabled: false } });

  await api.dispose();
});

test("超管在用户与角色页新建、编辑、重置密码并批量停用", async ({ page }) => {
  // 每次运行后 admin 的密码可能已经被改过，用能登进去的那个记下来。
  let signedIn = null;

  for (const password of ADMIN_PASSWORDS) {
    const landing = await signIn(page, "admin", password);

    if (landing !== "/login") {
      signedIn = password;
      break;
    }
  }

  expect(signedIn, "超管登录失败：候选密码全部无效").not.toBeNull();

  await page.goto("/user-management");
  await expect(page.getByRole("heading", { name: "用户与角色" })).toBeVisible();

  // 预置账号要在列表里出现，且角色列显示的是中文名——证明角色关系确实取到了。
  const plainRow = page.locator(".platform-data-table tbody tr", { hasText: PLAIN_USER });
  await expect(plainRow).toBeVisible();
  await expect(plainRow).toContainText("普通用户");
  await expect(plainRow).toContainText("待改密");
  await expect(plainRow).toContainText(state.departmentName);

  // 1) 新建账号：不选部门与角色也能建（未归属/未分配），随后在列表里可编辑。
  const createdUser = `pw_new_${STAMP}`;
  await page.getByRole("button", { name: "新建用户" }).click();

  // 抽屉内的输入框按 label 定位；「姓名」必须精确匹配，否则会命中搜索框「搜索账号或姓名」。
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel("登录账号").fill(createdUser);
  await dialog.getByLabel("姓名", { exact: true }).fill("新建实账号");
  await dialog.getByLabel("初始密码").fill("PwNew@2026");
  await dialog.getByRole("button", { name: "保存", exact: true }).click();

  const createdRow = page.locator(".platform-data-table tbody tr", { hasText: createdUser });
  await expect(createdRow).toBeVisible({ timeout: 30000 });
  await expect(createdRow).toContainText("未归属");
  await expect(createdRow).toContainText("未分配");

  // 2) 编辑姓名：抽屉里账号不可改（登录标识），改完列表即时刷新。
  await createdRow.getByRole("button", { name: "编辑" }).click();
  await expect(dialog.getByLabel("登录账号")).toBeDisabled();
  await dialog.getByLabel("姓名", { exact: true }).fill("新建实账号（改名）");
  await dialog.getByRole("button", { name: "保存", exact: true }).click();
  await expect(page.locator(".platform-data-table tbody tr", { hasText: createdUser }))
    .toContainText("新建实账号（改名）", { timeout: 30000 });

  // 3) 重置密码：成功后给出提示，账号回到「待改密」。
  await page.locator(".platform-data-table tbody tr", { hasText: createdUser })
    .getByRole("button", { name: "重置密码" }).click();
  await dialog.getByLabel("新密码").fill("PwReset@2026");
  await dialog.getByRole("button", { name: "确认重置" }).click();
  await expect(page.locator(".mud-alert")).toContainText("已重置", { timeout: 30000 });

  // 4) 批量停用：勾选行后点按钮，状态 Chip 变「停用」。
  const rowToDisable = page.locator(".platform-data-table tbody tr", { hasText: createdUser });
  await rowToDisable.locator(".mud-checkbox").click();
  await page.getByRole("button", { name: "批量停用" }).click();
  await expect(page.locator(".platform-data-table tbody tr", { hasText: createdUser }))
    .toContainText("停用", { timeout: 30000 });
});

test("超管在系统审计页可按结果筛选并查看变更详情", async ({ page }) => {
  for (const password of ADMIN_PASSWORDS) {
    if ((await signIn(page, "admin", password)) !== "/login") {
      break;
    }
  }

  await page.goto("/system-audit");
  await expect(page.getByRole("heading", { name: "系统审计" })).toBeVisible();
  await expect(page.locator(".platform-data-table tbody tr").first()).toBeVisible();

  // 按结果筛选「失败」：只剩失败记录，且新建账号这条成功记录必须消失。
  await page.getByLabel("结果").click();
  await page.locator(".mud-list-item", { hasText: "失败" }).first().click();
  await page.getByRole("button", { name: "查询" }).click();

  const rows = page.locator(".platform-data-table tbody tr");
  await expect(rows.first()).toBeVisible({ timeout: 30000 });
  await expect(rows.filter({ hasText: "成功" })).toHaveCount(0);

  // 详情抽屉展示变更明细（before/after JSON）。
  await rows.first().getByRole("button", { name: "查看" }).click();
  await expect(page.locator(".platform-code-content")).toBeVisible();
  await expect(page.getByRole("button", { name: "关闭" })).toBeVisible();
});

test("部门管理员只看到本部门账号，且无系统审计入口", async ({ page }) => {
  const landing = await signIn(page, DEPT_ADMIN_USER, DEPT_ADMIN_PASSWORD);

  // 管理员建的账号首登强制改密。
  expect(landing).toBe("/change-password");
  await page.locator("#currentPassword").fill(DEPT_ADMIN_PASSWORD);
  await page.locator("#newPassword").fill(DEPT_ADMIN_PASSWORD_NEW);
  await page.locator("#confirmPassword").fill(DEPT_ADMIN_PASSWORD_NEW);

  await Promise.all([
    page.waitForURL(url => url.pathname !== "/change-password", { timeout: 30000 }),
    page.locator("button.platform-auth-submit").click()
  ]);

  // 部门管理员的可见域是业务域，用户管理页靠「共享页」出现在合成的「权限管理」域下。
  await page.goto("/user-management");
  await expect(page.getByRole("heading", { name: "用户与角色" })).toBeVisible();

  const table = page.locator(".platform-data-table");
  await expect(table).toContainText(DEPT_ADMIN_USER);
  await expect(table).toContainText(PLAIN_USER);

  // 跨部门与超管账号一律不出现在列表里。
  await expect(table).not.toContainText("超级管理员");

  // 移交超管按钮只对超管渲染。
  await expect(page.getByRole("button", { name: "移交超管" })).toHaveCount(0);

  // 左侧菜单：有「用户与角色」，没有「系统审计」。
  const nav = page.locator(".platform-nav-shell");
  await expect(nav).toContainText("用户与角色");
  await expect(nav).not.toContainText("系统审计");

  // 直接敲地址也被页面闸门拦到 /forbidden。
  await page.goto("/system-audit");
  await page.waitForURL(url => url.pathname === "/forbidden", { timeout: 30000 });
  expect(new URL(page.url()).pathname).toBe("/forbidden");
});
