import { test, expect } from "@playwright/test";

async function checkViewport(page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBeTruthy();
  const chip = page.locator(".platform-version-chip");
  await expect(chip).toBeVisible();
  const bounds = await chip.boundingBox();
  expect(bounds.x + bounds.width).toBeLessThanOrEqual(page.viewportSize().width + 1);
}

async function checkSheet(page) {
  const sheet = page.locator(".platform-sheet");
  await expect(sheet).toBeVisible();
  const layout = await sheet.evaluate(element => {
    const body = element.querySelector(".platform-sheet-body");
    const children = [...body.children].filter(child => child.getBoundingClientRect().height);
    const bounds = element.getBoundingClientRect();
    body.scrollTop = body.scrollHeight;
    return {
      right: bounds.right,
      bottom: bounds.bottom,
      bodyBottom: body.getBoundingClientRect().bottom,
      lastBottom: children.at(-1).getBoundingClientRect().bottom,
      horizontalOverflow: body.scrollWidth > body.clientWidth + 1
    };
  });
  expect(layout.right).toBeLessThanOrEqual(page.viewportSize().width + 1);
  expect(layout.bottom).toBeLessThanOrEqual(page.viewportSize().height + 1);
  expect(layout.lastBottom).toBeLessThanOrEqual(layout.bodyBottom + 1);
  expect(layout.horizontalOverflow).toBeFalsy();
}

test("overview, navigation and local styles", async ({ page }) => {
  await page.goto("/");
  await expect(page.getByRole("heading", { name: "数据采集平台概览" })).toBeVisible();
  await expect(page.locator(".platform-data-table").first()).toBeVisible();
  await checkViewport(page);
  expect(await page.locator(".platform-page-hero").evaluate(el => getComputedStyle(el).paddingTop)).toBe("16px");
  await page.screenshot({ path: test.info().outputPath("overview.png"), fullPage: true });
  await page.getByRole("button", { name: "Next page", exact: true }).first().click();
  await expect(page.locator(".mud-table-pagination").first()).toContainText("6-6");
  await page.getByRole("button", { name: "切换导航菜单" }).click();
  if (page.viewportSize().width < 960) {
    await page.getByRole("link", { name: "采集点管理", exact: true }).click();
    await expect(page.getByRole("heading", { name: "采集点配置管理" })).toBeVisible();
  }
});

for (const [route, action] of [
  ["/collection-points", "新增采集点"],
  ["/managed-data-definitions", "新增后台数据"]
]) {
  test(`${route} editor and pagination`, async ({ page }) => {
    await page.goto(route);
    await expect(page.locator(".mud-table-pagination")).toBeVisible();
    await checkViewport(page);
    await page.getByRole("button", { name: action, exact: true }).click();
    await checkSheet(page);
    await expect(page.locator(".platform-sheet-footer")).toBeInViewport();
    await page.screenshot({ path: test.info().outputPath("editor.png") });
    await page.getByRole("button", { name: "关闭编辑", exact: true }).click();
    await expect(page.locator(".platform-sheet")).not.toBeVisible();
    if (route === "/managed-data-definitions") {
      await page.getByRole("textbox", { name: "搜索编码、名称、部门、工序、数据标识", exact: true }).fill("__ui_no_matching_record__");
      await expect(page.getByRole("heading", { name: "当前还没有符合条件的后台数据定义" })).toBeVisible();
    }
  });
}

test("node configuration stays complete within its scroll area", async ({ page, request }) => {
  const response = await request.get("/api/managed-data-definitions/");
  expect(response.ok()).toBeTruthy();
  const definitions = await response.json();
  const definition = definitions.find(item => item.acquisitionType === "Historian API");
  test.skip(!definition, "Requires an existing Historian definition; this test never inserts data.");
  await page.goto(`/managed-data-definitions/${definition.id}`);
  await page.getByRole("button", { name: "节点配置", exact: true }).click();
  await checkSheet(page);
  await expect(page.locator(".platform-code-content")).toBeVisible();
  await page.screenshot({ path: test.info().outputPath("node-sheet.png") });
  await page.getByRole("button", { name: "关闭节点配置" }).click();
  await checkViewport(page);
});

test("API console and missing-page states", async ({ page }) => {
  await page.goto("/history-api-test");
  await expect(page.getByRole("button", { name: "查询最新值", exact: true })).toBeVisible();
  await checkViewport(page);
  await page.goto("/not-found");
  await expect(page.getByRole("heading", { name: "页面不存在" })).toBeVisible();
  await checkViewport(page);
});

test("connection status page settles after diagnostics", async ({ page }) => {
  await page.goto("/server-connection-status");
  await expect(page.getByRole("heading", { name: "服务器连接状态", exact: true })).toBeVisible();
  await expect(page.getByLabel("正在检测服务器连接")).not.toBeVisible({ timeout: 30000 });
  await checkViewport(page);
});
