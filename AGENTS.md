# CDYC 数据采集平台 · AI 协作规则

本文件是 AI 助手在本仓库工作时的强制约束。与代码同等优先，冲突时以本文件为准。

---

## 强制规则：UI 设计必须遵守设计规范

**做任何 UI 改动前，必须先读 `docs/DESIGN_SYSTEM.md`。**

### 1. 色彩（最重要）

项目采用 **Material Design 3** 七色封闭体系，禁止新增色相：

| 角色 | Base | 变量前缀 |
|---|---|---|
| Primary | `#0B4FC4` | `--md-primary` / `DapPrimary*` |
| Secondary | `#33475C` | `--md-secondary` / `DapSecondary*` |
| Tertiary | `#7B2F9E` | `--md-tertiary` / `DapTertiary*` |
| Success | `#0D7A3C` | `--md-success` / `DapSuccess*` |
| Warning | `#B15600` | `--md-warning` / `DapWarning*` |
| Error | `#B32318` | `--md-error` / `DapError*` |
| Info | `#0F6FBD` | `--md-info` / `DapInfo*` |

> 上表为速查值，**一切以 `app.css` 的 `:root` 与 `docs/DESIGN_SYSTEM.md` 1.2 节为准**；
> 三级深度（Base / Soft / Tint）的完整色值见设计规范。

语义映射：在线/成功=绿、离线/失败/删除=红、停用/检测中=琥珀、提示=信息蓝、主操作=蓝。
**禁用灰色表达业务状态。**

**改色板必须核验四条底线**（由守卫测试 `Palette_Should_Meet_Contrast_Baselines` 强制）：

| 约束 | 阈值 |
|---|---|
| 白字 / base | ≥ 4.5:1（WCAG AA）|
| tint / 白底 | ≥ 1.20:1（底纹要肉眼可辨）|
| soft / 白底 | ≥ 1.25:1（统计卡底色要跳脱）|
| strong / 底色 | ≥ 4.5:1（卡片数字要可读）|

底色**必须整块铺满**，不要用 `linear-gradient(tint → white)` 把颜色稀释回白色——
那样卡片九成面积仍是白色，等于没上色。层次靠卡内的白色表格/图表底板区分。

### 2. 禁止硬编码色值

页面与组件中**不得出现十六进制色值**。必须走：

- Web：CSS 变量 `var(--md-*)` / `var(--platform-*)`
- 桌面：`{DynamicResource Dap*Brush}`（必须 `DynamicResource`，禁用 `StaticResource`）

权威色值文件：`app.css` 的 `:root`、`Themes/DapPalette.axaml`。

### 3. Web 端配色只加 class，不写新规则

需要换配色时，加一个既有 `.platform-accent-*` 类即可：

```razor
<MudPaper Class="platform-stat-card platform-stat-card--tinted platform-accent-success pa-4">
```

可用类：`platform-accent-primary` / `-secondary` / `-tertiary` / `-success` / `-warning` / `-error` / `-info` / `-neutral`。

**不得为具体色相新增 CSS 规则。** 这些类的内部实现只注入 `--card-accent*` 变量，其余样式统一读取变量。

### 4. 不得覆盖 accent 已决定的文字颜色

统计卡片的数值/标题颜色由 accent 自动决定，**不要再写 `Color="Color.Success"`** 之类。

### 5. 复用既有结构，不手搓

| 场景 | 必须用 |
|---|---|
| 页面根 | `<MudStack Class="platform-page-shell" Spacing="2">` |
| 页面头部 | `platform-page-hero` + accent |
| 统计卡 | `platform-stat-card--tinted` + `platform-stat-header`（底色铺满 soft 档）|
| 区块卡/面板 | `platform-section-card--tinted` / `platform-panel--tinted`（底色铺满 tint 档 + 左侧 4px 色条）|
| 表格 | `platform-data-table` |
| 空状态 | `platform-empty-state` + `platform-empty-icon` |
| 抽屉 | `platform-sheet` + `platform-sheet-section`（各分区不同 accent） |
| 对话框 | `Common/PlatformDialogs.cs`（禁止内联声明 `DialogOptions`） |
| 代码面板 | `platform-code-panel` / `platform-code-heading` / `platform-code-content` |
| 图表 | `platform-chart-panel` + `platform-chart-frame`（系列色随卡片 accent，见下）|
| 月历 | `platform-calendar` + `platform-calendar-cell`（MudBlazor 9.12 无独立日历组件） |

> 类名必须写完整：`platform-stat-card--tinted`、`platform-section-card--tinted`、
> `platform-panel--tinted`。历史上曾把双连字符前缀截断成 `--tinted`——那不是任何已定义的
> CSS 类，卡片会静默失去底色，且编译与守卫测试都发现不了。

### 6. 图表系列色跟随卡片 accent

MudChart 把系列色硬编码为 Material 默认蓝（`#2979FF`）。`app.css` 已按所在卡片的
`--card-accent` 覆盖折线/数据点/柱形与网格线。**图表必须放在带 `platform-accent-*`
的卡片内**，否则系列色会退回 Primary 蓝。图例色块与系列同源取色，不得写十六进制。

### 7. 界面文案不得出现英文异常消息

兜底 catch **禁止拼接 `ex.Message`**（会把 `No such host is known. (host:443)`、
`The operation was canceled.` 等英文框架文案和内网地址暴露给用户）。统一走
`UserFacingErrors.Describe(ex)`（Web 端命名空间 `DAP.Presentation.BlazorWeb.Client`，
桌面端 `DAP.Presentation.AvaloniaApp`）：超时 → 中文结论；`InvalidOperationException`
（服务端中文业务消息）→ 原样展示；网络不可达 → 中文结论；其余 → 「请查看服务端日志」。
守卫测试 `Catch_Blocks_Should_Not_Leak_Raw_Exception_Message` 强制。

### 8. 导航图标取色

导航已分组，**禁止用 `nth-child`** 给图标着色，改用 `platform-nav-link--*` 修饰类。

### 9. 新增页面必须登记主色

在 `docs/DESIGN_SYSTEM.md` 的「页面主色分配」表中登记，并在 `app.css` 之外无其他色板定义。

---

## 其他项目约定

### 代码风格

- 中文注释与中文提交信息（见 `CHANGELOG.md` 记录规则）
- `.razor` 业务逻辑放`.razor.cs` 代码后置（该项目已全面采用）
- 文件编码 UTF-8；Axaml 注释内**不得出现 `--`**（会导致 `AVLN1001`）

### 改色板时必须同步的四处

改配色不是只改一个文件，需同步：

1. `DAP.Presentation.BlazorWeb/wwwroot/app.css` → `:root`
2. `DAP.Presentation.BlazorWeb.Client/Layout/PlatformTheme.cs` → `MudTheme.PaletteLight`
3. `DAP.Presentation.AvaloniaApp/Themes/DapPalette.axaml`
4. `DAP.Presentation.AvaloniaApp/Themes/SemiBrandOverride.axaml`

`PaletteLight` 原先写在 `MainLayout.razor`，因登录页也要用同一份色板，已抽到
`PlatformTheme.cs`。守卫测试 `AppCss_And_MudTheme_Should_Share_Same_Color` 读的是新文件，
改回布局里会让构建失败。

### 登录与页面登记

全站已启用登录（Cookie 服务端会话），新增页面不需要加 `[Authorize]`——
服务端闸门会拦住未登录请求，`AuthorizeRouteView` 会拦住会话失效后的切页。要注意的是：

- 新增业务页面必须在 `Layout/NavigationCatalog.cs` 登记，并给出**唯一的 `PageKey`**
  与所属 `DomainCode`：启动对账据此写入 `domain_pages`，缺了页面不会报错但会从授权矩阵里消失。
- 页面等级只有两档：`ViewAccessLevel`（1，只看）与 `ConfigureAccessLevel`（2，可改）。
  写操作页面（导入、编辑、清空）必须是 2。
- 登录、改密这类页面用 `@layout AuthLayout`（无顶栏与抽屉）；其余页面的根容器是
  `platform-page-shell`，页面外壳守卫按 `@layout AuthLayout` 自动豁免，不要按文件名开口子。
- 表单提交走原生 `method="post"`，不要挂 `@onsubmit`：InteractiveAuto 下页面可能落在
  Server 电路里，电路内没有 `HttpContext`，签不出也签不掉 Cookie。
- 设计基线见 `docs/AUTH_DESIGN.md`；当前只落地 P0，域授权矩阵与审计属 P1。

### 组件 API 不可臆测

Material 图标名、MudBlazor 属性名必须先核实存在再使用：

- MudBlazor 图标：反射枚举 `Icons.Material.Filled` 字段
- Semi 色键：`app.TryGetResource(key, ThemeVariant.Light, out var v)` 验证
- Palette 字段：反射 `PaletteLight` 的可写属性

实例：`Icons.Material.Filled.PortraitOff` **不存在**（会报 CS0117）。

### 交付前必须验证

```bash
dotnet build DAP.slnx       # 期望 0 warning / 0 error
dotnet test DAP.Tests.UnitTests   # 期望全绿
```

改了 Web 端配色需启动应用截图核对；`dotnet build` 返回码非 0 但输出 `Build succeeded` 时，
真实原因常是沙箱拦截 `~/Library/Application Support/AvaloniaUI/` 的文件删除，与代码无关。

改动登录、会话或路由守卫后，另跑一次浏览器用例：

```bash
cd DAP.Presentation.BlazorWeb
npx playwright test --config auth.playwright.config.mjs
```

它会自起一个 5077 端口的实例（关闭采集调度），验证未登录拦截、错误密码提示、
首登强制改密、登录后落地、刷新保持与登出。注意：首次运行会把开发库超管密码
改为用例里的新密码，之后重复运行会自动识别。`ui.spec.mjs` 写于登录改造之前，
当前不可直接运行，不要用它判断登录是否正常。

### 提交前更新文档

- 变更写入 `CHANGELOG.md` 的 `## [未发布]` 小节，按 `新增/优化/修复/文档` 分类
- 架构与运行说明变更同步 `README.md`

---

**详细设计规范见 `docs/DESIGN_SYSTEM.md`，提交时必须先读。**