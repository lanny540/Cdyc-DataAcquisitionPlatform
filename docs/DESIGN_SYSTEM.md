# CDYC 数据采集平台 · 设计规范

本文档是**全项目 UI 设计的强制规则**，适用于 Web 端（Blazor + MudBlazor）与桌面端（Avalonia + Semi.Avalonia）双终端。

违反本文档的代码视为缺陷，评审时应直接驳回。

---

## 一、色彩体系（Material Design 2 · 品牌红 + 琥珀，示意稿定稿）

> **全站固定浅色主题**（2026-11-25 起，MD2 示意稿定稿）：Web 端 MudThemeProvider 传 `IsDarkMode="false"` 读取
> `PlatformTheme.Light`；桌面端 `App.axaml` 的 `RequestedThemeVariant="Light"`。页面底为中性灰 `#FAFAFA`，
> 白色卡片以 MD2 投影浮于其上——**卡片不铺 tint 底纹、不加色条**，色彩由顶栏、圆形图标底块、
> 徽章与图表系列承载。深色面板不再保留——只维护一套主题。
>
> **MD2 视觉语言（示意稿定稿）**：红色实底顶栏（primary 红 + 白字 + 4dp 投影）、灰底白卡加投影、
> 无色块页头（标题直接排在页底上）、圆形实色图标底块（accent 实底 + 白图标）、
> 100 阶浅底徽章（去描边）、黑色透明度阶文字（.87/.60/.54）、4dp 圆角。
> 可读性底线：正文对表面 ≥12:1、次要与弱化文字 ≥4.5:1、输入框边框 ≥3:1。

### 1.1 唯一权威来源

| 端           | 权威文件                                                                                      | 说明                                     |
|--------------|-----------------------------------------------------------------------------------------------|------------------------------------------|
| Web          | `DAP.Presentation.BlazorWeb/wwwroot/app.css` 的 `:root` 变量                                  | `--md-*` / `--platform-*`                |
| 桌面         | `DAP.Presentation.AvaloniaApp/Themes/DapPalette.axaml`                                        | `Dap*Brush` / `Dap*Color`                |
| 桌面主题对接 | `DAP.Presentation.AvaloniaApp/Themes/SemiBrandOverride.axaml`                                 | 覆盖 Semi 默认调色板                     |
| Web 组件对接 | `DAP.Presentation.BlazorWeb.Client/Layout/PlatformTheme.cs` 的 `PaletteLight`                 | 传给 MudBlazor；主框架与登录页共用同一份 |

**改色流程**：先改权威文件 → 再同步到对接文件 → 验证双端一致。禁止只改其中一处。

### 1.2 七组角色色

每组角色色固定五档，**不得新增其他色相**。base 是唯一色相锚点：

| 角色              | 变量             | Base（图标底块/填充底） | On（填充底上的文字） | Strong（强调文字） | Soft（淡色容器） | Tint（浅底徽章） |
|-------------------|------------------|----------------|----------------------|--------------------|--------------------|------------------|
| Primary 主色（红）| `--md-primary`   | `#D32F2F`      | `#FFFFFF`            | `#A31515`          | `#F4B5B5`          | `#FFCDD2`        |
| Secondary 次色    | `--md-secondary` | `#455A64`      | `#FFFFFF`            | `#263238`          | `#B0BEC5`          | `#CFD8DC`        |
| Tertiary 强调色（琥珀）| `--md-tertiary`  | `#FFB300`      | `#3E2A00`            | `#8F4400`          | `#FFD54F`          | `#FFE082`        |
| Success 成功/在线 | `--md-success`   | `#2E7D32`      | `#FFFFFF`            | `#1B5E20`          | `#A5D6A7`          | `#C8E6C9`        |
| Warning 警告/停用 | `--md-warning`   | `#B15600`      | `#FFFFFF`            | `#8F4400`          | `#FFCC80`          | `#FFE0B2`        |
| Error 错误/危险   | `--md-error`     | `#8E1B1E`      | `#FFFFFF`            | `#5C0E10`          | `#F4C7C9`          | `#F9DDDF`        |
| Info 信息         | `--md-info`      | `#1976D2`      | `#FFFFFF`            | `#0D47A1`          | `#90CAF9`          | `#BBDEFB`        |

**一切以 `app.css` 的 `:root` 变量为准**，改色时先改那里；`On` 档对应 `--md-{role}-on`。

**深度用途（不可混用）**：

- **Base**：圆形图标底块（示意稿定稿：accent 实色圆 + On 档图标）、填充底（主按钮、开关、选中态）——配 On 档白字 ≥4.5:1；
- **Strong**：强调文字、浅底徽章文字、图表折线/柱形、标签行、表格主字段（在白面与 tint 容器上 ≥4.5:1）；
- **Soft**：对话框/提示图标底块等淡色容器、outlined 提示条的底色混合基色（与白色表面 ≥1.25:1）；
- **Tint**：浅底徽章与提示条底、hover 淡底、抽屉选中项淡底（与白色表面 ≥1.20:1）。

**文字层级铁律**：

- **数值（h3/h5）与页头/区块标题（h4/h6）一律用近黑 `--platform-text`**——示意稿中数值是页面最清晰的层级，
  大数字一旦用 accent 色就会与彩色图标底块抢视觉；
- accent 色相出现在**小面积元素**：圆形实色图标底块、标签行（caption）、图表折线/柱形（strong 档）；
- 卡片一律白底 + MD2 投影（示意稿定稿）：**禁止再铺 tint 底纹、渐变或色条**，色彩只由顶栏、圆形图标底块、
  徽章与图表系列承载。

**主色填充底与白字（MD2 铁律）**：primary 是深色（红），一切填充底（主按钮、开关、
选中态、桌面主色实底头条卡）配 **白字**（On 档，对比 ≥4.5:1）。MudBlazor 把 primary 既当「填充底」又当前景色用——
填充态读 `PrimaryContrastText`（已设白字），描边/文字按钮与聚焦态直接取 base 色，
在白面上 ≥6.7:1，无需额外修正。

**主色红与错误深酒红的区分**：主色是亮红 `#D32F2F`（Red 700），错误色是**深酒红 `#8E1B1E`**——
两者同属红族，只靠明度区分（暗红＝错误，亮红＝品牌/操作），色差 ΔE 73.8；强调琥珀 `#FFB300`
与错误色 ΔE ≈ 192，天然无冲突。守卫测试 `Error_Should_Be_Distinguishable_From_Accent`
对 primary 与 tertiary 双查（CIE76 ΔE ≥ 20）。历史上第一版错误色取 M3 基线 `#B3261E`，
与亮红色差只有 ΔE 10、一眼分不开；改用深酒红靠明度拉开。改错误色时不得退回亮红。

#### 1.2.1 对比度底线（可读性的硬约束）

「整站看上去很素」或「文字不明显」的根因，**不是颜色不够艳，而是层级之间的对比度不够**：
底色层与表面色太接近会让所有卡片呈现为同一个颜色；正文与次要文字不够黑会让整页发灰；
边框与表面太接近会让卡片轮廓糊掉。

当前色板已核验（守卫测试 `Palette_Should_Meet_Contrast_Baselines` 与
`Neutral_Palette_Should_Meet_Contrast_Baselines` 强制，参照表面从 app.css 的 `--platform-surface` 读取）：

| 约束                     | 阈值     | 含义                                       |
|--------------------------|----------|--------------------------------------------|
| On / base                | ≥ 4.5:1  | WCAG AA 正文级，主按钮与徽章可读           |
| tint / 卡片表面          | ≥ 1.20:1 | 浅底徽章/提示条底必须肉眼可辨             |
| soft / 卡片表面          | ≥ 1.25:1 | 对话框/提示图标底块可辨                   |
| strong / tint、/ soft    | ≥ 4.5:1  | 徽章文字与图表系列在淡色容器上仍清晰      |
| 正文 / 表面              | ≥ 7:1    | 卡片数值在白卡上清晰（实际 ≥11:1）        |
| 次要文字 / 表面          | ≥ 4.5:1  | 补充说明可读                               |
| 弱化文字 / 表面          | ≥ 4.5:1  | 时间戳、提示语可读                         |
| 常规边框 / 表面          | ≥ 1.25:1 | 输入框/分隔线轮廓可见                     |
| 输入框边框 / 表面        | ≥ 3.0:1  | WCAG 非文本对比，输入区域边界清楚         |

**改动色板时必须重新核验这些底线**，否则「改配色」很容易改成「变淡」或「看不清」。

### 1.2.2 卡片的投影语言（示意稿定稿）

页面底为中性灰 `#FAFAFA`，**所有卡片一律白底 + MD2 投影浮于其上**（`--platform-shadow`：
`0 1px 3px rgba(0,0,0,.12), 0 1px 2px rgba(0,0,0,.24)`），无边框、不铺 tint 底纹、无左色条、无顶部色带：

| 容器                       | 底色                    | 轮廓                 | 色彩由谁承载               | 修饰类                          |
|----------------------------|-------------------------|----------------------|----------------------------|---------------------------------|
| 页面头部                   | 无色块（直排页面灰底）  | 无                   | 顶栏 + 卡内 accent         | `platform-page-hero`（accent 类名保留，无视觉） |
| **头条 KPI**（全宽汇总卡） | 白                      | MD2 投影             | 圆形图标底块 + 数值近黑最大号 | `platform-stat-card--filled`    |
| 常规统计卡                 | 白                      | MD2 投影             | **圆形实色图标底块（accent 实底 + On 档图标）** + 标签行 | `platform-stat-card--tinted` |
| 区块卡片                   | 白                      | MD2 投影             | 图标底块 + accent 徽章     | `platform-section-card--tinted` |
| 面板                       | 白                      | MD2 投影             | 标题 + accent              | `platform-panel--tinted`        |
| 图表容器（卡内）           | 白                      | MD2 投影             | 系列色（strong 档）        | `platform-chart-frame`          |
| 数据表格（卡内）           | 白                      | 表头下 12% 黑分隔线  | 表头中性灰字               | `platform-data-table`           |

要点：

- 卡内表格、抽屉、代码面板同为白色表面，层次靠投影与分隔线区分，不再靠底纹色差；
- `--tinted` / `--filled` 修饰类**保留作语义标记但无视觉差异**（示意稿中所有统计卡都是白卡），
  写类名时仍必须带全双连字符前缀，为将来恢复差异化样式留语义；
- **桌面端等价实现**：`DapStyles.axaml` 的 `Border.stat-card` / `Border.section-card`
  同为白卡 + `BoxShadow` 投影（无边框无色条）；图标底块 = `Dap*Brush` 实色圆 + `Dap*OnBrush` 图标
  （`MetricCardModel.AccentOn`）；标签/图表系列 = `Dap*StrongBrush`，数值 = `DapTextPrimaryBrush`（近黑）。
  **Web 端设计风格调整必须同步到桌面端**。
- **页面头部全站统一无色块**：22 个页面 hero 均为白底直排标题，hero 上的 accent 类不再产生视觉效果；
  页面个性由卡片 accent 表达，不再一页一色。
- **桌面端淡色底纹不铺数据容器**：桌面指标卡的既定样式是「白卡 + 圆形图标底块」，
  `DapPrimarySoftBrush`/`DapPrimaryTintBrush` 不得铺整卡（page-hero 已无底色）；
  主色实底头条卡用 `DapPrimaryBrush`/`DapPrimaryStrongBrush` 反白是合法形态。
  守卫 `Desktop_Data_Containers_Should_Not_Use_Primary_Pale_Brushes` 强制。
- **禁止用渐变稀释色彩**：卡片、图表容器一律纯色白底，`linear-gradient` 仅保留在
  深色代码/日志面板之外的场景（示意稿中无渐变装饰）。

### 1.2.3 徽章与提示条（MudChip / MudAlert）的浅底形态

MD2 的状态徽章是「100 阶浅底 + 深色字」，不再用描边。app.css 统一覆盖：

- outlined / text 徽章与提示条：**底色取 tint 色块、文字取 strong 加深档、描边去掉**；
- 类名是**复合类**：`mud-chip-color-warning` / `mud-alert-outlined-warning`，
  不是 `mud-warning`（写错会静默不生效）。

填充态徽章（filled）读 base 底 + On 档白字，无需额外覆盖。

### 1.2.4 焦点框范围

`:focus-visible` 只作用于**可交互元素**（链接/按钮/输入/`[role=button]`/
非负 `[tabindex]`）。原因：Blazor 增强导航会给内容区标题加 `tabindex="-1"`
并在跳转后聚焦它，全量规则会让页头标题画出焦点框（仪表板页头实测出现过）。
焦点框取 primary 的 base 档（红色焦点环对白面 5.0:1，清晰可辨）。

### 1.3 中性色（浅色）

| 用途     | 变量                        | 色值      | 对白色表面   |
|----------|-----------------------------|-----------|--------------|
| 页面背景 | `--platform-bg`             | `#FAFAFA` | —            |
| 卡片表面 | `--platform-surface`        | `#FFFFFF` | —            |
| 次级表面 | `--platform-surface-muted`  | `#F5F5F5` | 1.09:1       |
| 下沉表面 | `--platform-surface-sunken` | `#EEEEEE` | 1.16:1       |
| 常规边框 | `--platform-border`         | `#E0E0E0` | 1.32:1       |
| 强边框   | `--platform-border-strong`  | `#8C8C8C` | 3.36:1       |
| 主要文字 | `--platform-text`           | `#212121` | 16.1:1       |
| 次要文字 | `--platform-text-muted`     | `#4F4F4F` | 8.2:1        |
| 弱化文字 | `--platform-text-subtle`    | `#757575` | 4.6:1        |

> 层级遵循浅色惯例「越浮越白」：页面底为中性灰（Grey 50），白色卡片以 MD2 投影浮起，
> 浮起的对话框用 `--platform-shadow`（MD2 4dp 投影）。
> 文字用黑色透明度阶口径（MD2 标准）：正文 .87 黑、次要 .60 黑、弱化 .54 黑。
> 强边框（输入框描边、登录输入框）对表面 ≥3:1，这是「输入框边界看得清」的保证。

### 1.4 语义色映射（强制）

| 语义                   | 颜色           | 用例                               |
|------------------------|----------------|------------------------------------|
| 在线 / 成功 / 已启用   | `success` 绿   | 在线采集点、连通、启用态           |
| 离线 / 失败 / 危险     | `error` 红     | 离线采集点、删除按钮、失败态       |
| 停用 / 待处理 / 进行中 | `warning` 橙   | 停用、检测中、不可连接             |
| 提示 / 中性信息        | `info` 信息蓝  | 来源标识、空状态、提示条           |
| 主操作 / 输入焦点 / 选中控件 | `primary` 红（配白字） | 主按钮、开关、复选框、输入焦点、表格主字段 |
| 点缀 / 强调徽章        | `tertiary` 琥珀 | 强调徽章、点缀色块、FAB            |

**禁用**：不得用`default`（灰）表达业务状态。灰只用于「未配置」这类无状态信息的场景。
**禁用**：内容区里的红有两个语义——主色（操作/品牌）与错误（危险），两者靠明度区分
（守卫 ΔE ≥ 20 强制）；需要「点缀/强调」时用 `tertiary`（琥珀，与两种红都不同族）。

---

## 二、accent 修饰类机制（Web 端核心模式）

### 2.1 八个语义修饰类

```css
.platform-accent-primary    .platform-accent-secondary  .platform-accent-tertiary
.platform-accent-success    .platform-accent-warning    .platform-accent-error
.platform-accent-info       .platform-accent-neutral
```

每个类**只做一件事**：向容器注入四个变量。

```css
.platform-accent-primary {
    --card-accent: var(--md-primary);          /* 圆形图标底块实色 */
    --card-accent-strong: var(--md-primary-strong); /* 强调文字/图表系列 */
    --card-accent-soft: var(--md-primary-soft);    /* 淡色容器 */
    --card-accent-tint: var(--md-primary-tint);    /* 浅底徽章/选中淡底 */
}
```

其余样式**一律读 `var(--card-accent*)`**，不得为具体色相写独立规则。

### 2.2 强制用法

```razor
<MudPaper Class="platform-stat-card platform-stat-card--tinted platform-accent-success pa-4">
```

- `platform-stat-card` + `platform-stat-card--tinted`：白卡 + 投影 + 圆形实色图标底块 + 标签行（`--tinted` 作标记，无视觉差异）
- `platform-stat-card--filled`：头条 KPI 专用标记（同为白卡，数值近黑最大号）
- `platform-page-hero platform-accent-<色>`：页面头部（无色块白底直排，accent 类名仅作标记）
- `platform-empty-state platform-accent-<色>`：空状态
- ~~`platform-sheet`~~：已全站退役（新增/编辑改整页编辑视图，见 3.4）

### 2.3 页面主色分配

**页面头部全站统一 primary**（MD2 经典样式：一致的页头，页面个性由卡片表达）。
hero 之外，每个页面在 accent 体系内登记一个**卡片主色**，用于区块卡与图表：

| 页面                                                              | 菜单归属                 | 卡片主色 |
|-------------------------------------------------------------------|--------------------------|----------|
| 平台概览 `/platform-overview`                                     | 平台管理                 | secondary |
| 点位配置 `/collection-points`                                     | 平台管理 → 采集配置      | info      |
| 点位数据管理 `/managed-data-definitions`                          | 平台管理 → 采集配置      | tertiary  |
| 采集服务器配置 `/acquisition-servers`                             | 平台管理 → 采集配置      | success   |
| 服务器状态查询 `/server-connection-status`                        | 平台管理                 | info      |
| History API 测试 `/history-api-test`                              | 平台管理 → 接口管理      | warning   |
| MQTT 测试 `/mqtt-test`                                            | 平台管理 → 接口管理      | tertiary  |
| 域与页面配置 `/domain-config`                                     | 平台管理 → 权限管理      | secondary |
| 部门与授权 `/department-grants`                                   | 平台管理 → 权限管理      | tertiary  |
| 用户与角色 `/user-management`                                     | 平台管理 → 权限管理      | info      |
| 系统审计 `/system-audit`                                          | 平台管理 → 权限管理      | warning   |
| 采集节点详情 `/managed-data-definitions/{id}`                     | 平台管理 → 采集配置      | tertiary  |
| 排班时间管理 `/shift-schedules`                                   | 业务配置 → 班组管理      | secondary |
| 排班日历 `/shift-calendar`                                        | 业务配置 → 班组管理      | info      |
| 能耗仪表板 `/`（别名 `/energy-dashboard`）                        | 数据指标 → 能耗指标      | success   |
| 历史指标 `/energy-history`                                        | 数据指标 → 能耗指标      | tertiary  |
| 班组横向对比 `/energy-comparison`                                 | 数据指标 → 能耗指标      | warning   |
| 班组能耗报表 `/energy-report`                                     | 数据指标 → 数据报表      | secondary |
| 园区能源总览 `/park-energy`                                       | 能源 → 能源总览          | secondary |
| 计量点台账 `/park-energy/meters`                                  | 能源 → 台账与配置        | secondary |
| 实时采集数据 `/collection-data`                                   | 数据查看 → 采集运行      | info      |
| 登录 `/login`、改密 `/change-password`                            | 不在菜单内（AuthLayout） | primary   |
| 无权限 `/no-access`、无访问权 `/forbidden`、未上线 `/coming-soon` | 不在菜单内（状态页）     | info      |

> 上表登记的是**卡片主色**（区块卡、抽屉、图表系列）；页面头部 hero 一律 primary，不再逐页登记。
> 「数据指标」与「能源」是两个并列的域，不要混：「数据指标」是钞纸工序视角（班组 × 四项指标 × 班次），
> 「能源」是园区经营视角（介质 × 组织 × 用途 × 系统）。两者都用到了「能耗」这个词，但口径与受众不同。
> 能源域统一取 `secondary`，与钞纸的 `success` 绿区分开，否则从顶栏切过去会以为没跳转。

新增页面时必须在本表中登记卡片主色（hero 无需登记，全站统一 primary）。

**桌面端（Avalonia）页面主色**：与 Web 同名页面保持一致，颜色由各页面视图模型的
`Accent` 属性声明（守卫测试 `Avalonia_Page_ViewModels_Should_Be_Registered_In_Navigation_Catalog`
保证每个页面都登记在导航目录里）。

| 桌面页面            | 抽屉归属             | 主色     |
|---------------------|----------------------|----------|
| 能耗仪表板          | 数据指标 → 能耗指标  | success  |
| 历史指标            | 数据指标 → 能耗指标  | tertiary |
| 班组横向对比        | 数据指标 → 能耗指标  | warning  |
| 班组能耗报表        | 数据指标 → 数据报表  | secondary |
| 实时采集数据        | 数据查看 → 采集运行  | info     |
| 排班日历            | 数据查看 → 班组排班  | info     |
| 登录 / 强制改密浮层 | 不在菜单内（覆盖层） | primary  |

### 2.4 指标卡片的 accent 取色

能耗类页面的 4 项指标需要各自取一个语义色，且顺序在全站保持一致
（info → success → warning → tertiary）。实现方式是代码后置中的白名单常量：

```csharp
private static readonly string[] MetricAccents =
    ["platform-accent-info", "platform-accent-success", "platform-accent-warning", "platform-accent-tertiary"];
```

标记中通过插值注入：`Class="@($"platform-stat-card platform-stat-card--tinted {MetricAccents[i]} pa-4")"`。

守卫测试 `Stat_Cards_Should_Declare_Accent_Class` 允许这种写法，
但要求代码后置中确实存在 `MetricAccents` 且含 `platform-accent-` 字面量，
防止有人借插值把任意色相塞进来。

**汇总卡不是第 5 项指标**：全宽头条卡（如能耗仪表板的「综合能耗合计」）是四项指标之和，
不属于任何一个指标色相，取**主色实底反白**（MD2 filled card）：

```razor
<MudPaper Class="platform-stat-card platform-stat-card--filled platform-accent-primary pa-4" Elevation="0">
```

头条卡与常规统计卡同为白卡，靠更大的数值字号（h3 27px 级）与琥珀圆形图标底块建立第一数据层级。
历史上头条卡曾取 soft 档底色（红金版金色 soft 铺满后像警示条，已否掉）；MD2 改版后统一为主色实底。
同理，「采集点总数」「班组数量」「对比明细」这类综合/计数/明细容器也取主色实底或中性 `secondary` tint，
不得混入四项指标的语义色相。

---

## 三、组件规范

### 3.1 统计卡片

```razor
<MudPaper Class="platform-stat-card platform-stat-card--tinted platform-accent-success pa-4" Elevation="0">
    <div class="platform-stat-header">
        <span class="platform-section-icon">
            <MudIcon Icon="@Icons.Material.Filled.WifiTethering" Size="Size.Small"/>
        </span>
        <MudText Typo="Typo.caption">在线采集点</MudText>
    </div>
    <MudText Typo="Typo.h3">@Count</MudText>
    <MudText Typo="Typo.body2" Class="home-stat-supporting">补充说明</MudText>
</MudPaper>
```

- 必须带 `--tinted` 与 accent 类；**全宽汇总头条卡改用 `--filled`**（主色实底反白）
- 必须有 `platform-stat-header` 图标行
- 数值文字颜色由样式统一为近黑，**不要再加 `Color="..."`**

### 3.2 数据表格

```razor
<MudTable Class="platform-data-table" Dense="true" Hover="true" Breakpoint="Breakpoint.Sm">
```

- 表头中性灰字 + 12% 黑下分隔线（示意稿定稿，不随 accent 上色）
- 编码、地址等标识文本用 `platform-inline-code` + `platform-table-primary`
- 行操作区用 `table-action-grid` + `table-action-button`，**统一 `Variant="Variant.Text"` + `Size="Size.Small"`**，
  按动作语义取色（详见 3.10）。primary 的文字/描边按钮直接用 base 色（红对白面 5.0:1，无需修正）
- 表格本身用白色表面（`--platform-surface`），与白卡同色，层次靠表头分隔线与斑马纹/hover 区分

### 3.2.1 区块卡片与面板

```razor
<MudPaper Class="platform-section-card platform-section-card--tinted platform-accent-success" Elevation="0">
```

- 示意稿定稿后区块卡/面板一律白卡 + MD2 投影：`--tinted` 仅作语义标记，**无 tint 底纹、无描边、无色条**
- `--banded`（顶部 3px 色带）已随示意稿改版退役，不得再写
- 卡片内的表格、抽屉、代码面板同为白色表面

### 3.3 空状态

```razor
<div class="platform-empty-state platform-accent-info">
    <span class="platform-empty-icon">
        <MudIcon Icon="@Icons.Material.Filled.Api" Size="Size.Large"/>
    </span>
    <MudText Typo="Typo.h6">还没有测试结果</MudText>
    <MudText Typo="Typo.body2" Color="Color.Secondary">引导文案</MudText>
</div>
```

禁止用 `MudStack Class="pa-10"` + 灰色 `MudIcon` 手搓空状态。

### 3.4 表单视图与对话框

- **新增/编辑表单一律整页编辑视图**（与采集服务器页一致）：列表与表单在同一页面按状态切换，
  表单包含 hero（标题 + 返回列表按钮）、提示/校验 alert、`platform-section-card--tinted`
  分区卡（各自不同 accent）与底部操作条（取消 / 次操作 / 主操作）。禁止新用 `MudDialog` 做表单抽屉
  （`platform-sheet` 体系已全站退役）。
- 对话框行为配置统一走 `Common/PlatformDialogs.cs`（现仅 `Confirm` 居中确认框），禁止页面内联声明 `DialogOptions`

### 3.5 代码/日志面板

```html
<MudPaper Class="platform-code-panel" Elevation="0">
    <div class="platform-code-heading">原始 JSON 响应</div>
    <pre class="platform-code-content">@Json</pre>
</MudPaper>
```

固定深色分层，不随 accent 变化——浅色主题下代码面板保留深色是刻意的：
原始报文与日志用深底等宽字最易读，也是全站唯一保留的深色区域。

### 3.6 图表容器

```razor
<div class="platform-chart-panel platform-chart-frame">
    <MudChart T="double" ChartType="ChartType.Line" ChartSeries="@BuildSeries(trend)" ChartLabels="@BuildLabels(trend)"
              ChartOptions="@BuildOptions(trend)" MatchBoundsToSize="true" Height="320px" Width="100%" />
</div>
```

图表呈现的统一约束（守卫测试 `Charts_Should_Fill_Container_And_Configure_Options` 强制）：

- **必须** `MatchBoundsToSize="true"` —— 否则 SVG 按固定 viewBox 居中，绘图区只占 79% 宽度；
- **必须** 显式传 `ChartOptions`，配置统一走 `PlatformChartDefaults`（数据点标记、加粗线条、
  网格线、悬停提示、轴标题），纵轴标题传指标单位、横轴标题传维度名；
- 图表底板是 `tint → 表面` 的渐变（上 42% 渐隐）+ `soft` 描边。
  这是「卡片要整块铺色」规则的**唯一例外**：绘图区铺满 tint 会让网格线与数据点难以辨认，
  因此只在顶部保留色调、向下渐隐为表面色。区块卡本身仍然整块铺色。

#### 图表系列颜色：跟随卡片 accent，禁止默认蓝

MudChart 把系列颜色硬编码为 Material 默认蓝（`#2979FF`），写进 SVG presentation attribute。
CSS 规则优先级高于 presentation attribute，因此 `app.css` 用 accent 变量覆盖：

- 折线 `path.mud-chart-line` / 数据点 `circle.mud-chart-point` → `--card-accent-strong`；
- 柱形 `path.mud-chart-bar` → `--card-accent-strong`，悬停提亮（`filter: brightness(1.25)`）；
- 网格线统一为 `--platform-border`（替换 MudBlazor 的 `#e0e0e0`）。

**系列取 strong 档而不是 base**：strong 是加深档，每个角色在浅底上都 ≥4.5:1。
「系列色 = 图例色块」恒成立。

**约束：图表必须放在带 `platform-accent-*` 的卡片内**，否则系列色会退回 MudBlazor 默认蓝（`#2979FF`）。
图例用 `platform-chart-legend` + `platform-chart-legend-swatch`，
色块同样取 `--card-accent-strong`，保证「图例颜色 = 折线/柱形颜色」恒成立，**不得直接写十六进制**。

#### 响应式

- 1600/1280/960/600/375px 实测：绘图区占 SVG 宽度 78–92%，轴文字全部可见；
- ≤600px 时容器降为 300px 高、内边距收紧，X 轴标签自动旋转 -35°（等距标签在窄屏水平排列会互相压字）。

#### MudChart 数据绑定的两个硬规则

MudChart 的语义是 **「系列（Series）× X 轴标签（Labels）」**：
每条系列是一组柱子/折线，标签决定它们的横向位置。这两点极易写反，且**编译能过、单测测不出，只有渲染后才暴露**。

**规则一：横轴要对比什么，标签就取什么。**
班组横向对比要「横轴为班组」，就应当 **1 条系列（系列名 = 指标名）+ N 个班组标签**。
若把每个班组建成一条系列，横轴只剩一个标签、所有柱子挤在同一位置，完全无法对比：

```csharp
// 正确：横轴是班组
new ChartSeries<double> { Name = "原辅材料 A", Data = teams.Select(t => t.ValueA).ToArray() }
// 错误：横轴只有 1 个标签，4 条系列叠在一起
teams.Select(t => new ChartSeries<double> { Name = t.TeamName, Data = [t.ValueA] })
```

**规则二：标签数量必须等于系列数据点长度。**
否则 MudChart 只渲染到较短的一方。

守卫测试 `MudChart_Series_And_Labels_Should_Share_Same_Source` 强制
`ChartSeries` 与 `ChartLabels` 的取值方法同源（同方法，或读取同一集合），
违反即构建失败。

此外 MudChart 会**等距绘制全部 X 轴标签**，数据点超过 12 个就会挤成黑带，
必须在 `BuildLabels` 中抽稀（保留标签位、其余置空）。

### 3.7 三级导航（顶栏模块 → 分组 → 页面）

导航分三层，唯一数据源是 `DAP.Presentation.BlazorWeb.Client/Layout/NavigationCatalog.cs`：

| 层级     | 交互                           | 实现位置                                |
|----------|--------------------------------|-----------------------------------------|
| 顶级模块 | 顶栏三个按钮，点击切换左侧菜单 | `MainLayout.razor` + `.platform-topnav` |
| 分组     | 左侧菜单的分组标题             | `.platform-nav-section`                 |
| 页面     | 左侧菜单条目，跳转路由         | `NavMenu.razor` + `NavMenuItem.razor`   |

当前分配：

| 顶级模块 | 分组         | 页面                               |
|----------|--------------|------------------------------------|
| 数据指标 | 能耗指标     | 能耗仪表板、历史指标、班组横向对比 |
| 数据指标 | 数据报表     | 班组能耗报表                       |
| 业务配置 | 班组管理     | 排班时间管理、排班日历             |
| 平台管理 | （直接条目） | 平台概览、服务器状态查询           |
| 平台管理 | 采集配置     | 点位配置、点位数据管理             |
| 平台管理 | 接口管理     | History API 测试、MQTT 测试        |
| 平台管理 | 权限管理     | 域与页面配置、部门与授权           |

约束：

- 顶栏与左侧菜单**共用同一份目录**，不得另写一份菜单数组，否则层级会漂移。
- 顶级模块的 Id 必须唯一；顶栏按 Id 判定激活态，重复会让两个模块同时高亮。
- 路由变化时按「最长前缀」反查所属模块（`/managed-data-definitions/{id}` 归到该列表页），
  保证刷新页面或直接输入地址后顶栏高亮与内容一致。
- 切换模块时若当前页面不属于该模块，跳转到该模块的首个页面，避免菜单与内容错位。
- **根路由 `/` 属于「数据指标」的能耗仪表板**（`@page "/"`，另保留 `/energy-dashboard` 别名），
  平台概览在 `/platform-overview`；`NavigationCatalog.DefaultSection` 为「数据指标」，
  启动与未知路由都默认选中它。调整首页指向时这三处要一起改。
- 图标取色用显式修饰类（`platform-nav-link--primary` 等），**不要用 `nth-child`**——
  分组后同一父级下有多个 `MudNavMenu`，`nth-child` 不再可靠。
- **顶栏与顶栏激活模块用主色实底反白**（MD2 彩色顶栏）；抽屉选中项取主色 tint 底 + strong 字（示意稿定稿后无色条）；
  非激活项图标按本页主色的 strong 档分区着色。
- 新增菜单项只需在目录中登记，守卫测试 `Navigation_Links_Should_Point_To_Existing_Pages`
  会校验每个 href 都有对应的 `@page` 路由，写错路由会直接失败。

### 3.8 月历网格

MudBlazor 9.12 **未提供** `MudCalendar` 组件（只有 `MudDatePicker` 内嵌的日历），
月历需用 `platform-calendar` + `platform-calendar-cell` 自建 7 列网格。

- 周一为每周首日，`.NET` 的 `DayOfWeek` 以周日为 0，需做偏移换算
- 前后补齐空白格保证列对齐，尾部整行落在本月之外时不再生成
- 单元格内色相全部来自父容器的 accent 变量，不写具体色值

### 3.9 登录页与统一登录入口

外部身份源（OIDC）的入口只在**确实配置了提供方**时渲染，未配置时整段不出现，不留空按钮。三个类名固定：

| 类名                           | 用途                                       |
|--------------------------------|--------------------------------------------|
| `.platform-auth-divider`       | 「或使用统一登录」分隔线，两侧细线取中性色 |
| `.platform-auth-external`      | 外部入口容器（纵向堆叠、等宽）             |
| `.platform-auth-external-link` | 单个入口，**描边次级按钮**样式             |

- 外部入口一律**次级样式**：主按钮只能有一个（账号密码登录的「登录」），否则用户不知道该点哪个。
- 提供方多于 3 家时优先考虑折叠，避免把登录表单挤出首屏（390px 高度下尤其明显）。
- 该区块与改密页共用 `AuthLayout` 外壳，主色沿用 `/login` 的 primary，不新增色相。

---

### 3.10 组件风格规范（按钮 / 输入 / 开关 / 徽章）

**原则：外观由动作角色决定，页面强调色不参与。** 同一个动作在全站长得一样——「刷新」在任何页面都是深灰蓝描边，
「保存」在任何对话框都是红色填充。此前的做法是各页面按自己的 accent 挑按钮色（新建用户=蓝、新建域=深灰蓝、
新建部门=紫、导出=琥珀），同一动作两个页面两种样子。

#### 按钮

| 角色             | 固定写法                                          | 用于                                             | 数量       |
|------------------|---------------------------------------------------|--------------------------------------------------|------------|
| 主操作           | `Color="Color.Primary"` + `Variant="Variant.Filled"`     | 新建、查询、保存、确认、上传/导入、生成报表       | 每屏 1~2 个 |
| 次操作           | `Color="Color.Secondary"` + `Variant="Variant.Outlined"` | 刷新、重置、导出、测试连通、预览、区块内的次级新增 | 不限       |
| 取消 / 关闭 / 返回 | `Color="Color.Secondary"` + `Variant="Variant.Text"`     | 对话框底部、返回上一级、清空筛选、「本月」        | —          |
| 破坏性确认       | `Color="Color.Error"` + `Variant="Variant.Filled"`       | 确认删除、确认清空、确认移交超管                 | 对话框底部 |
| 敏感操作确认     | `Color="Color.Warning"` + `Variant="Variant.Filled"`     | 确认重置密码                                     | 对话框底部 |
| 危险入口         | `Color="Color.Error"`（描边或文字）                | 工具栏「清空数据」、行内删除/停用/移交超管        | —          |
| 行内操作         | `Variant="Variant.Text"` + `Size="Size.Small"` + `Class="table-action-button"` | 表格行               | —          |

- **行内动作语义色**：详情 / 编辑 / 查看 = primary；调试 / 测试 = info；执行 / 入库 = success；重置密码 / 恢复默认 =
  warning；删除 / 停用 / 移交超管 = error。
- `secondary` / `info` / `success` / `tertiary` **不得做填充按钮**，按钮颜色不得省略，也不得用 `Default` / `Inherit`
  （守卫测试 `Components_Should_Follow_Unified_Style` 强制）。
- **成对的「选中 / 未选中」切换**（如仪表板的看板 / 数字孪生）：选中项 `Primary` 填充，未选中项 `Secondary` 描边。
  不要用 success / tertiary 给两个选项各配一种颜色。
- **语义成对的描边按钮保留语义色**：启用 = success、停用 = error；试读 = info、入库 = success——这里颜色本身承载
  「会产生什么结果」的含义，不属于页面强调色。
- 前往类文字按钮（查看完整历史、查看班组对比）用 `Primary` 文字；返回类用 `Secondary` 文字。
- 工具栏按钮带 `StartIcon`（`Icons.Material.Filled.*`）、Medium；行内 Small。错误态 / 空状态里的唯一动作
  （重新加载、去上传、立即新增、返回首页）是主操作，用 primary 填充。

#### 输入、开关、徽章

| 组件                       | 固定写法                                                   |
|----------------------------|------------------------------------------------------------|
| `MudTextField` / `MudSelect` / `MudNumericField` / `MudDatePicker` | `Variant="Variant.Outlined"` + `Margin="Margin.Dense"`（筛选栏与对话框表单一致） |
| `MudSwitch`                | `Color="Color.Primary"`（无 Variant 参数）                 |
| `MudChip` / `MudAlert`     | `Variant="Variant.Outlined"`，状态色写在 `Color` / `Severity` |
| `MudProgressLinear` / `MudProgressCircular` | 跟随所在卡片的 accent，不统一成一种颜色    |

筛选栏固定结构：`MudStack Row AlignItems="End"` 内依次是输入 / 选择 → 红色「查询」→ 文字「清空筛选」。
下划线形态（不写 `Variant` 的默认值）在浅色主题下边界太弱，已全部废弃。

#### 桌面端对应

`Button.Primary` = 主操作（生成报表、登录）；`Button.ghost` = 次操作（刷新、导出 CSV、上一月 / 下一月 / 本月）。

---

## 四、桌面端（Avalonia）

### 4.1 取色方式

```xml
<Border Background="{DynamicResource DapPrimaryBrush}" />
<TextBlock Foreground="{DynamicResource DapTextPrimaryBrush}" />
```

- **必须用 `DynamicResource`**（运行时主题切换需要），禁止 `StaticResource`
- 优先用 `Dap*Brush` 品牌键；需要控件默认行为色时用被覆盖后的 `SemiColor*`

### 4.2 半覆盖键（已核实存在于Semi.Avalonia 12.1.0.1）

`SemiColorPrimary` / `SemiColorPrimaryActive` / `SemiColorPrimaryDisabled` /
`SemiColorSuccess` / `SemiColorSuccessActive` / `SemiColorWarning` / `SemiColorWarningActive` /
`SemiColorDanger` / `SemiColorDangerActive` / `SemiColorLink`

> Semi 的Error 语义键叫 **`SemiColorDanger`**（无 `SemiColorError`）。

**新增覆盖键前必须先核实存在性**，否则覆盖静默失效：

```csharp
app.TryGetResource("SemiColorXxx", ThemeVariant.Light, out var v);
```

### 4.3 Axaml 编写注意

- XML 注释内**不得出现连续两个连字符 `--`**（如 `--platform-*`），会导致 `AVLN1001` 解析失败
- 引入资源字典用 `<ResourceDictionary.MergedDictionaries>`，不能直接把 `ResourceInclude` 放进
  `ResourceDictionary.Content`
- 品牌色覆盖必须放在 `Application.Resources`，**不能**放在 `Application.Styles`（那里要求 `IStyle`）

### 4.4 样式类（`Themes/DapStyles.axaml`）

`App.axaml` 在 Semi 主题之后引入这份样式，桌面端页面只用这些类，不写行内颜色：

| 类                                                                                 | 用途                                                                    | 对应 Web                                       |
|------------------------------------------------------------------------------------|-------------------------------------------------------------------------|------------------------------------------------|
| `page-hero`                                                                        | 页头容器（示意稿定稿：无色块，直排页面灰底）                          | `platform-page-hero`                           |
| `page-title` / `page-subtitle`                                                     | 页头标题与副标题                                                        | 同名                                           |
| `stat-card` / `stat-label` / `stat-value` / `stat-value-large` / `stat-supporting` | 统计卡：白卡投影、圆形实色图标底块、名称、数值、辅助文字                  | `platform-stat-card`                           |
| `section-card` / `card-title` / `card-description`                                 | 区块卡（白卡投影，无色条）                                                 | `platform-section-card`                        |
| `panel`                                                                            | 表面底容器（表格、明细、提示块）                                        | `platform-chart-frame` / `platform-data-table` |
| `chart-frame`                                                                      | 图表底板                                                                | `platform-chart-frame`                         |
| `chip`                                                                             | 状态标签                                                                | `platform-chip`                                |
| `empty-state` / `empty-title` / `empty-text`                                       | 空状态                                                                  | `platform-empty-state`                         |
| `field` / `field-label`                                                            | 表单控件与标签                                                          | 同 Web 的 `MudTextField`                       |
| `data-table` / `table-primary` / `table-cell` / `timestamp`                        | `DataGrid`、列头、主字段、次要字段、时间戳                              | `platform-data-table`                          |
| `nav-button` / `nav-item` / `nav-icon` / `nav-text`                                | 抽屉条目（`Classes.active` 表达选中）                                   | `platform-nav-link`                            |
| `module-button` / `accent-bar` / `divider` / `ghost`                               | 顶栏模块、色条、分隔线、次按钮                                          | —                                              |

**选中状态用 `Classes.active`**，不要用 `nth-child` 或按索引取色（与 Web 端「禁止 nth-child」同一条规则）。

### 4.5 图表

`Controls/LineTrendChart.cs`（面积折线）与 `Controls/BarCompareChart.cs`（单序列柱状）是自绘控件：

- 颜色来自 `Accent` / `LabelBrush` / `GridBrush` 三个 `StyledProperty`，默认值走 `TryFindResource` 取色板，
  **不得在控件里写死色值**；页面通过 `Accent` 传入本页主色资源键。
- 横轴标签超过 6 个自动抽稀，避免挤成黑带（与 Web 端「超过 12 个标签需抽稀」同理）。
- 柱状图是**一条系列 + N 个标签**，不是每班组一条系列——后者会全部挤在同一位置。

---

## 五、硬性禁止项

1. **禁止在页面/组件中硬编码十六进制色值**，一律走 CSS 变量或资源键
2. **禁止为具体色相新增 CSS 规则**，一律复用 `.platform-accent-*`
3. **禁止给 MudBlazor 的 `Color` 参数传入与语义不符的值**（如用 Success 表示"停用"）
4. **禁止在 MudText 上加 `Color` 覆盖 accent 已决定的值**
5. **禁止引入第 8 种色相**，七个角色色 + 中性色是封闭集合；退役的品牌红变量体系
   （`--platform-brand*` / `DapBrand*`）不得复活——其语义由 primary（同色值 #D32F2F）承接，
   避免同一红色出现两套变量
6. **禁止手搓空状态、抽屉、确认框**，统一用既有class 与 `PlatformDialogs`
7. **禁止直接改MudBlazor / Semi 主题源码**，只能通过 `PaletteLight` / `Application.Resources` 覆盖
8. **禁止给卡片铺色/渐变**：示意稿定稿后卡片一律白底 + MD2 投影，**不得再铺 tint 底纹、
   `linear-gradient` 渐变或左/顶色条**——历史上「要有色就整块铺满」的口径已随示意稿改版废止，
   色彩只由顶栏、圆形图标底块、徽章与图表系列承载。
9. **桌面端禁止用主色淡色底纹铺数据容器**：`DapPrimarySoftBrush` / `DapPrimaryTintBrush` 不得作为
   指标卡、区块卡的整卡底色（page-hero 已无底色）；
   桌面指标卡保持「白卡 + 圆形图标底块（`Accent` 实底 + `AccentOn` 图标）」样式（守卫
   `Desktop_Data_Containers_Should_Not_Use_Primary_Pale_Brushes` 强制）。
   主色实底只属于桌面主色实底头条卡与顶栏；Web 端 primary 参与正常 accent 轮换，不在此限。

---

## 六、新增页面检查清单

- [ ] 页面根容器为 `<MudStack Class="platform-page-shell" Spacing="2">`
- [ ] 头部为 `platform-page-hero`（示意稿定稿：无色块白底直排，不铺 tint/色条/渐变；accent 类名仅作标记）
- [ ] 统计卡片为白卡 + 圆形实色图标底块（accent 实底 + On 档图标），带 `--tinted` 标记；
      全宽头条 KPI 用 `--filled`（同为白卡，数值近黑最大号）
- [ ] 区块卡/面板为白卡 + 投影，无底纹无色条（`--banded` 已退役）
- [ ] 表格带 `platform-data-table`（表头中性灰字 + 12% 黑分隔线）
- [ ] 空状态用 `platform-empty-state`
- [ ] 新增/编辑表单为整页编辑视图（hero + 分区卡 + 操作条，参照采集服务器页）；确认对话框走 `PlatformDialogs`
- [ ] 全文件无硬编码色值，无 `--platform-brand` / `DapBrand` 引用（品牌红体系已退役，语义由 primary 承接）
- [ ] 按钮按 3.10 的角色写：主操作 Primary 填充、次操作 Secondary 描边、取消 Secondary 文字、危险 Error；行内 Text + Small
- [ ] 输入类组件 `Variant.Outlined` + `Margin.Dense`，开关 `Color.Primary`，徽章与提示条 `Variant.Outlined`
- [ ] 在本文档「页面主色分配」表中登记新页面的卡片主色

组件级样式（`Xxx.razor.css`）另需：

- [ ] 组件专属样式写在同名 `Xxx.razor.css`，不要往 `wwwroot/*.css` 里塞一次性样式
- [ ] 样式作用到**子组件渲染的元素**时选择器要加 `::deep`（例如 `SvgText` 渲染的 `<text>`），
  否则作用域属性 `[b-xxxx]` 不匹配，样式静默不生效
- [ ] 不要删 `App.razor` 里的 `DAP.Presentation.BlazorWeb.styles.css`

> **为什么最后一条不能删**：宿主页面只 link 了应用样式包，但这个包的**第一行**是
> `@import 'DAP.Presentation.BlazorWeb.Client.<指纹>.bundle.scp.css';`，
> 客户端项目的组件样式正是通过这条 `@import` 进入页面的（浏览器的 CSSOM 里能看到嵌套表）。
> 只读包体、不跟进 `@import`，会误判成「`.razor.css` 不生效」而把样式挪进全局文件——
> 这是本项目真实踩过的坑，`App_Should_Link_Application_Styles_Bundle` 就是为它加的守卫。

桌面端（Avalonia）页面另需：

- [ ] 视图模型继承 `PageViewModelBase`，声明 `Accent`，实现 `LoadCoreAsync`
- [ ] 视图与视图模型**同名配对**（`XxxPageView.axaml` ↔ `XxxPageViewModel.cs`），根上写 `x:DataType="vm:XxxPageViewModel"`
- [ ] 在 `Navigation/DesktopNavigationCatalog.cs` 登记（否则菜单里进不去，守卫测试会失败）
- [ ] 页面主色写进上表的「桌面端页面主色」分区

---

## 七、验证方式

改动配色后必须执行：

```bash
dotnet build DAP.slnx       # 期望 0 warning / 0 error
dotnet test DAP.Tests.UnitTests
```

涉及 Web 端配色的改动，还需启动应用逐页核对（`dotnet run --project DAP.Presentation.BlazorWeb`）。

---

**相关文档**：架构与运行说明见 `README.md`；变更记录见 `CHANGELOG.md`。
