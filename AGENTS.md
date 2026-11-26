# CDYC 数据采集平台 · AI 协作规则

本文件是 AI 助手在本仓库工作时的强制约束。与代码同等优先，冲突时以本文件为准。

---

## 强制规则：UI 设计必须遵守设计规范

**做任何 UI 改动前，必须先读 `docs/DESIGN_SYSTEM.md`。**

### 1. 色彩（最重要）

项目采用 **Material Design 2** 七色封闭体系（品牌红+琥珀，示意稿定稿），禁止新增色相：

| 角色      | Base（填充底） | On（底上文字） | 变量前缀                           |
|-----------|----------------|----------------|------------------------------------|
| Primary   | `#D32F2F`      | `#FFFFFF`      | `--md-primary` / `DapPrimary*`     |
| Secondary | `#455A64`      | `#FFFFFF`      | `--md-secondary` / `DapSecondary*` |
| Tertiary  | `#FFB300`      | `#3E2A00`      | `--md-tertiary` / `DapTertiary*`   |
| Success   | `#2E7D32`      | `#FFFFFF`      | `--md-success` / `DapSuccess*`     |
| Warning   | `#B15600`      | `#FFFFFF`      | `--md-warning` / `DapWarning*`     |
| Error     | `#8E1B1E`      | `#FFFFFF`      | `--md-error` / `DapError*`         |
| Info      | `#1976D2`      | `#FFFFFF`      | `--md-info` / `DapInfo*`           |

> 上表为速查值，**一切以 `app.css` 的 `:root` 与 `docs/DESIGN_SYSTEM.md` 1.2 节为准**；
> 五档深度（Base / On / Strong / Soft / Tint）的完整色值见设计规范。

语义映射：在线/成功=绿、离线/失败/删除=错误深红、停用/检测中=橙、提示=信息蓝、
点缀/强调徽章=琥珀（tertiary）、主操作/输入焦点/选中控件=红（配白字）。 **禁用灰色表达业务状态。**

**全站采用浅色主题**（Web `IsDarkMode="false"` + 桌面 `RequestedThemeVariant="Light"`）：
页面底为中性灰 `#FAFAFA`，白色卡片以 MD2 投影浮于其上——**卡片不铺 tint 底纹、不加色条**；色彩由顶栏、圆形图标底块（accent 实底 + On 档图标）、徽章与图表系列承载。

**MD2 视觉语言（2026-11-25 示意稿定稿）**：红色实底顶栏（红+白字+4dp 投影）、灰底白卡加投影、
无色块页头（标题直排页底）、圆形实色图标底块、100 阶浅底徽章（去描边）、
黑色透明度阶文字（.87/.60/.54）、4dp 圆角。**页面头部不再一页一色**——hero 为无色块白底，
页面个性由卡片 accent 表达。

**主色红与错误深酒红靠明度区分**：亮红 `#D32F2F`（primary）＝品牌/操作，深酒红 `#8E1B1E`＝错误；
色差须 ≥ ΔE 20（守卫 `Error_Should_Be_Distinguishable_From_Accent` 对 primary+tertiary 双查，
当前实际 ΔE 73.8），改错误色时不得退回 `#B3261E` 一类的亮红。
强调琥珀 `#FFB300`（tertiary）＝点缀/FAB（填充底配深褐字 `#3E2A00`）。
退役的品牌红变量体系（`--platform-brand*` / `DapBrand*`）不得复活——语义由 primary 承接。

**主色填充底一律白字**：primary 是深色（红），`On` 档为白色；填充态读 `PrimaryContrastText`，
描边/文字按钮与聚焦态直接取 base 色（对白面 5.0:1），无需额外修正。

**桌面端主色淡色底纹不铺数据容器**：`DapPrimarySoftBrush`/`DapPrimaryTintBrush` 不得作为指标卡、
区块卡的整卡底色（page-hero 例外）；桌面指标卡保持「白卡 + 色条 + 图标块」样式，
主色实底头条卡（`DapPrimaryBrush`/`DapPrimaryStrongBrush` 反白）是合法形态
（守卫 `Desktop_Data_Containers_Should_Not_Use_Primary_Pale_Brushes` 强制）。

**改色板必须核验对比度底线**（由守卫测试 `Palette_Should_Meet_Contrast_Baselines` 与
`Neutral_Palette_Should_Meet_Contrast_Baselines` 强制，
参照表面从 app.css 的 `--platform-surface` 读取）：

| 约束                   | 阈值                                                    |
|------------------------|---------------------------------------------------------|
| On / base              | ≥ 4.5:1（WCAG AA，填充底上的文字）                      |
| tint / 卡片表面        | ≥ 1.20:1（卡片底纹要肉眼可辨）                          |
| soft / 卡片表面        | ≥ 1.25:1（头条 KPI 底色要跳脱）                         |
| strong / tint、/ soft  | ≥ 4.5:1（标签行与图表系列要可读）                       |
| 正文 / tint            | ≥ 7:1（卡片数值要清晰）                                 |
| 次要、弱化文字 / 表面  | ≥ 4.5:1（文字不能发灰）                                 |
| 输入框边框 / 表面      | ≥ 3.0:1（输入区域边界要清楚）                           |
| 错误色 vs 强调色       | ΔE ≥ 20（CIE76，错误与点缀要一眼可分）                  |

**组件风格统一（强制，细则见 `docs/DESIGN_SYSTEM.md` 3.10；守卫 `Components_Should_Follow_Unified_Style`）**——
外观由动作角色决定，**页面 accent 不得用来挑按钮颜色**：

| 角色 | 写法 |
|------|------|
| 主操作（新建/查询/保存/导入） | `Color.Primary` + `Variant.Filled`，每屏 1~2 个 |
| 次操作（刷新/重置/导出/测试） | `Color.Secondary` + `Variant.Outlined` |
| 取消 / 关闭 / 返回 | `Color.Secondary` + `Variant.Text` |
| 破坏性确认 / 危险入口 | `Color.Error`（敏感操作确认用 `Color.Warning`） |
| 表格行内操作 | `Variant.Text` + `Size.Small` + `table-action-button`，详情/编辑=primary、调试=info、入库=success、删除=error |

- `Secondary/Info/Success/Tertiary` 不得做填充按钮；按钮不得省略 `Color`，不得用 `Default`/`Inherit`。
- 成对的选中切换：选中项 Primary 填充、未选中项 Secondary 描边。
- 输入类组件一律 `Variant.Outlined` + `Margin.Dense`；开关 `Color.Primary`（无 Variant 参数）；徽章与提示条 `Variant.Outlined`。
- 桌面端：`Button.Primary`=主操作，`Button.ghost`=次操作（刷新/导出）。

示意稿定稿后**卡片一律白底 + MD2 投影，禁止再铺 tint 底纹、渐变或色条**（含 `linear-gradient(tint → surface)`）——
色彩只由顶栏、圆形图标底块、徽章与图表系列承载。层次靠卡内的表格/图表底板与投影区分。

### 2. 禁止硬编码色值

页面与组件中**不得出现十六进制色值**。必须走：

- Web：CSS 变量 `var(--md-*)` / `var(--platform-*)`
- 桌面：`{DynamicResource Dap*Brush}`（必须 `DynamicResource`，禁用 `StaticResource`）

权威色值文件：`app.css` 的 `:root`、`Themes/DapPalette.axaml`。
守卫测试把这条规则落到两端：`Razor_Pages_Should_Not_Hardcode_Hex_Colors`（.razor）与
`Avalonia_Styles_Should_Not_Hardcode_Hex_Colors`（.axaml，仅豁免两个色板文件本身）。

### 3. Web 端配色只加 class，不写新规则

需要换配色时，加一个既有 `.platform-accent-*` 类即可：

```razor
<MudPaper Class="platform-stat-card platform-stat-card--tinted platform-accent-success pa-4">
```

可用类：`platform-accent-primary` / `-secondary` / `-tertiary` / `-success` / `-warning` / `-error` / `-info` /
`-neutral`。

**不得为具体色相新增 CSS 规则。** 这些类的内部实现只注入 `--card-accent*` 变量，其余样式统一读取变量。

### 4. 不得覆盖 accent 已决定的文字颜色

统计卡片的数值/标题颜色由 accent 自动决定，**不要再写 `Color="Color.Success"`** 之类。

### 5. 复用既有结构，不手搓

| 场景        | 必须用                                                                                                                                                           |
|-------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| 页面根      | `<MudStack Class="platform-page-shell" Spacing="2">`                                                                                                             |
| 页面头部    | `platform-page-hero`（示意稿定稿：无色块白底直排标题，不铺 tint、不加色条；accent 类名保留作标记）                                                                  |
| 统计卡      | `platform-stat-card--tinted` + `platform-stat-header`（白卡 + MD2 投影，不铺底纹、无色条；色相由**圆形实色图标底块（accent 实底 + On 档图标）**与标签行承载；`--tinted/--filled` 类名保留作标记，无视觉差异） |
| 区块卡/面板 | `platform-section-card--tinted` / `platform-panel--tinted`（白卡 + MD2 投影，无底纹无色条；卡内表格/抽屉同为白色表面）                    |
| 表格        | `platform-data-table`                                                                                                                                            |
| 空状态      | `platform-empty-state` + `platform-empty-icon`                                                                                                                   |
| 新增/编辑表单 | **整页编辑视图**（与采集服务器页一致）：列表与表单同页按 `_isEditorOpen` 等状态切换；hero 标题 + 「返回列表」按钮 + `platform-section-card--tinted` 分区卡 + 底部操作条；**禁止新用 `MudDialog` 做表单抽屉**（`platform-sheet` 体系已退役） |
| 对话框      | `Common/PlatformDialogs.cs`（禁止内联声明 `DialogOptions`）；仅保留删除等二次确认用途                                                                                                      |
| 代码面板    | `platform-code-panel` / `platform-code-heading` / `platform-code-content`                                                                                        |
| 图表        | `platform-chart-panel` + `platform-chart-frame`（系列色随卡片 accent，见下）                                                                                     |
| 月历        | `platform-calendar` + `platform-calendar-cell`（MudBlazor 9.12 无独立日历组件）                                                                                  |

> 类名必须写完整：`platform-stat-card--tinted`、`platform-section-card--tinted`、
> `platform-panel--tinted`。历史上曾把双连字符前缀截断成 `--tinted`——那不是任何已定义的
> CSS 类；示意稿定稿后这些修饰类只作标记（无视觉差异），但截断仍会让代码失去语义一致性，
> 且未来恢复差异化样式时会静默失效。

### 6. 图表系列色跟随卡片 accent

MudChart 把系列色硬编码为 Material 默认蓝（`#2979FF`）。`app.css` 已按所在卡片的
`--card-accent` 覆盖折线/数据点/柱形与网格线。**图表必须放在带 `platform-accent-*`
的卡片内**，否则系列色会退回默认蓝。图例色块与系列同源取色，不得写十六进制。

### 7. 界面文案不得出现英文异常消息

兜底 catch **禁止拼接 `ex.Message`**（会把 `No such host is known. (host:443)`、
`The operation was canceled.` 等英文框架文案和内网地址暴露给用户）。统一走
`UserFacingErrors.Describe(ex)`（Web 端命名空间 `DAP.Presentation.BlazorWeb.Client`，
桌面端 `DAP.Presentation.AvaloniaApp`）：超时 → 中文结论；`InvalidOperationException`
（服务端中文业务消息）→ 原样展示；网络不可达 → 中文结论；其余 → 「请查看服务端日志」。
守卫测试 `Catch_Blocks_Should_Not_Leak_Raw_Exception_Message` 强制。

### 8. 导航图标取色

导航已分组，**禁止用 `nth-child`** 给图标着色，改用 `platform-nav-link--*` 修饰类。
注意特异度：MudBlazor 用 (0,4,0) 选择器（`.mud-navmenu.mud-navmenu-default .mud-nav-link.active:not(...)` 与
`.mud-nav-link:not(.mud-nav-link-disabled) .mud-nav-link-icon.mud-nav-link-icon-default`）强制激活文字与图标颜色，
自定义规则必须以 `.mud-navmenu .mud-nav-item.platform-nav-link...` 开头（(0,5,0) 起），否则**静默被压制**
（历史上分区图标着色与激活文字都因此从未生效）。**顶栏用主色实底反白，抽屉选中项取主色 tint 底 + strong 字**
（`--md-primary*` / `DapPrimary*`），这是外壳样式。

### 9. 设计风格双端同步（强制）

**Web 端的设计风格调整必须同步到桌面端**（卡片底色、文字层级、色档语义、导航状态等）。
桌面端等价物：Web 的 CSS 变量 `--md-*` ↔ `DapPalette.axaml` 的 `Dap*Brush`；
`platform-stat-card/section-card` ↔ `DapStyles.axaml` 的 `Border.stat-card/section-card` +
页面 axaml 的资源绑定。同步后 `dotnet build` + `dotnet test` 必须全绿。

### 10. 新增页面必须登记主色

在 `docs/DESIGN_SYSTEM.md` 的「页面主色分配」表中登记，并在 `app.css` 之外无其他色板定义。

桌面端另有一张同名表（含每个页面的 `Accent` 值）；新增桌面页面还要在
`Navigation/DesktopNavigationCatalog.cs` 登记，否则守卫测试
`Avalonia_Page_ViewModels_Should_Be_Registered_In_Navigation_Catalog` 会失败——
这是为了防止「页面写完了却没有菜单入口」。

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
- 授权模型为三层收窄（职能域 → 页面等级 → 权限点），判定入口是 `PageAccessResolver`
  与 `AuthorizationCatalog`；不再维护独立设计文档，改动须同步本文件与 `docs/DESIGN_SYSTEM.md`。

### 两端共用的 API 客户端

`DAP.Clients` 是 Web 与桌面端唯一的接口入口，**新增接口只加在这里**：

| 文件                                               | 职责                                                               |
|----------------------------------------------------|--------------------------------------------------------------------|
| `IPlatformApiClient.cs` / `PlatformApiClient.cs`   | 业务契约与 HTTP 实现（`/api/*`）                                   |
| `IPlatformAuthClient.cs` / `PlatformAuthClient.cs` | 登录、当前用户、登出、改密（302 + `Location` 表达结果）            |
| `PlatformHttpResponse.cs`                          | 统一错误文案：401/403/404/429/5xx 翻成中文，非 JSON 响应不回显原文 |

约定：

- **不得**把接口搬回 `DAP.Presentation.BlazorWeb.Client`（会让桌面端反向依赖展示层），
  也**不得**在桌面端复制一份实现——守卫测试 `Shared_Api_Client_Should_Live_In_Dap_Clients_Project` 强制。
- `DAP.Clients` 只引用 `DAP.Core.Shared`，不引用 `DAP.Presentation.*` 与 `DAP.Infrastructure`。
- Web 端服务端预渲染走 `ServerPlatformApiClient`（`PlatformPresentationServiceCollectionExtensions.cs`
  内的适配器，直调领域服务）；它必须实现同一个接口，新增成员时两处都要补。
- 桌面端**不要**用 `IHttpClientFactory` 创建会话客户端：Cookie 容器随客户端实例存活，
  换实例就掉线。用 `DesktopSession` 里的专用 `HttpClient`（`AllowAutoRedirect = false`）。
- 桌面端只做读取。给 `IPlatformApiClient` 加写方法可以，但不要在桌面端界面上暴露写操作。

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

### 提交前更新文档（强制）

**每次代码提交都必须更新 `CHANGELOG.md`，没有例外。**

- **标题**：以提交日期为标题，形如 `## 2026-11-26`。同一天多次提交合并进同一天的小节，
  不再新建 `## [未发布]` 累积待发内容。
- **内容**：只写本次提交实际包含的变更——不写计划中的功能，不写与本次改动无关的条目，
  不重复历史小节里已有的描述。
- **写法**：简洁清晰，优先说明「改了什么」与「影响哪里」，避免「优化若干逻辑」这类空话。
  例：`排班宽表导入改为按班组编号 1-4 映射甲乙丙丁，缺列时回显中文提示`，
  而不是 `优化排班导入`。
- **分类**：条目较多时在小节内用 `### 新增 / ### 优化 / ### 修复 / ### 文档` 细分；
  单条变更可不分类。
- **一致性**：提交信息中的主要条目必须能在当日小节里找到对应描述，两者不得互相矛盾。
- 架构与运行说明变更同步更新 `README.md`。

> `CHANGELOG.md` 顶部的「记录规则」「变更分类建议」两节与本条规则同义；
> 表述冲突时以本条为准。

---

**详细设计规范见 `docs/DESIGN_SYSTEM.md`，提交时必须先读。**
