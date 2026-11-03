# CDYC 数据采集平台 · 设计规范

本文档是**全项目 UI 设计的强制规则**，适用于 Web 端（Blazor + MudBlazor）与桌面端（Avalonia + Semi.Avalonia）双终端。

违反本文档的代码视为缺陷，评审时应直接驳回。

---

## 一、色彩体系（Material Design 3）

### 1.1 唯一权威来源

| 端 | 权威文件 | 说明 |
|---|---|---|
| Web | `DAP.Presentation.BlazorWeb/wwwroot/app.css` 的 `:root` 变量 | `--md-*` / `--platform-*` |
|桌面 | `DAP.Presentation.AvaloniaApp/Themes/DapPalette.axaml` | `Dap*Brush` / `Dap*Color` |
| 桌面主题对接 | `DAP.Presentation.AvaloniaApp/Themes/SemiBrandOverride.axaml` | 覆盖 Semi 默认调色板 |
| Web 组件对接 | `DAP.Presentation.BlazorWeb.Client/Layout/PlatformTheme.cs` 的 `MudTheme.PaletteLight` | 传给 MudBlazor；主框架与登录页共用同一份 |

**改色流程**：先改权威文件 → 再同步到对接文件 → 验证双端一致。禁止只改其中一处。

### 1.2 七组角色色

每组角色色固定三级深度，**不得新增其他色相**：

| 角色 | 变量 | Base（主色） | Soft（浅填充） | Tint（极浅底） |
|---|---|---|---|---|
| Primary 主色 | `--md-primary` | `#0B4FC4` | `#C9DCF8` | `#DCE8FA` |
| Secondary 次色 | `--md-secondary` | `#33475C` | `#D3DDE8` | `#DFE6EE` |
| Tertiary 第三色 | `--md-tertiary` | `#7B2F9E` | `#ECDCF6` | `#ECDEF7` |
| Success 成功/在线 | `--md-success` | `#0D7A3C` | `#C3E8D2` | `#D7F0E1` |
| Warning 警告/停用 | `--md-warning` | `#B15600` | `#FFDDB0` | `#FFE4BC` || Error 错误/危险 | `--md-error` | `#B32318` | `#F7CCC7` | `#FBE0DC` |
| Info 信息 | `--md-info` | `#0F6FBD` | `#C2DDF6` | `#D5E8FA` |

> Warning 的 soft 精确值为 `#FFDDB0`（无空格），本表中的写法仅为示意；
> **一切以 `app.css` 的 `:root` 变量为准**，改色时先改那里。

**深度用途（不可混用）**：
- **Base**：色条、强调文字、主按钮、图标本身
- **Soft**：**统计卡片底色**、图标底色块、标签底、选中态背景
- **Tint**：**区块卡片底色**、页面头部底色、区域底纹、hover 背景

#### 1.2.1 为什么底色要压到这个深度（可读性底线）

「整站看上去很素」的根因**不是颜色不够艳，而是底色层与白色表面太接近**。
早期 tint 与白底的对比度只有 1.05~1.09 倍，肉眼完全分辨不出，
于是所有卡片实际都呈现为白色，再鲜艳的 base 色也只出现在 3~4px 的色条上。

当前色板已核验三条底线（由守卫测试 `Palette_Should_Meet_Contrast_Baselines` 强制）：

| 约束 | 阈值 | 含义 |
|---|---|---|
| 白字 / base | ≥ 4.5:1 | WCAG AA 正文级，主按钮与徽章可读 |
| tint / 白底 | ≥ 1.20:1 | 区块底纹必须肉眼可辨，否则等于没上色 |
| soft / 白底 | ≥ 1.25:1 | 统计卡底色用 soft 档，比区块更跳脱 |
| strong / 底色 | ≥ 4.5:1 | 卡片数值在有色底上仍清晰 |

**改动色板时必须重新核验这四条**，否则「改配色」很容易改成「变淡」或「看不清」。

### 1.2.2 卡片底色的分配

| 容器 | 底色 | 修饰类 |
|---|---|---|
| 页面头部 | tint 全幅 | `platform-page-hero` |
| 统计卡片 | soft 全幅 | `platform-stat-card--tinted` |
| 区块卡片 | tint 全幅 + 左侧 4px 色条 | `platform-section-card--tinted` |
| 面板 | tint 全幅 + 左侧 4px 色条 | `platform-panel--tinted` |
| 图表容器 | tint→白渐变（上 42% 渐隐） | `platform-chart-frame` |
| 数据表格 | 白色底板 | `platform-data-table` |

**不要用渐变把底色稀释回白色**——早期实现用 `linear-gradient(tint → white 78px)`，
结果卡片九成面积仍是白色，视觉上等于没上色。要有色就整块铺满。

### 1.3 中性色

| 用途 | 变量 | 色值 |
|---|---|---|
| 页面背景 | `--platform-bg` | `#F4F7FA` |
| 卡片表面 | `--platform-surface` | `#FFFFFF` |
| 次级表面 | `--platform-surface-muted` | `#F8FAFC` |
| 下沉表面 | `--platform-surface-sunken` | `#EEF2F7` |
| 常规边框 | `--platform-border` | `#E1E7EE` |
| 强边框 | `--platform-border-strong` | `#C7D0DA` |
| 主要文字 | `--platform-text` | `#16202B` |
| 次要文字 | `--platform-text-muted` | `#4E5A70` |
| 弱化文字 | `--platform-text-subtle` | `#8593A6` |

> 次要文字从 `#56637A` 加深到 `#4E5A70`：原值在 tint 底色上对比度只有 4.16~4.41，
> 低于 WCAG AA 的 4.5:1。加深后对全部 14 个底色档（7 色 × tint/soft）均 ≥ 4.77:1。

### 1.4 语义色映射（强制）

| 语义 | 颜色 | 用例 |
|---|---|---|
| 在线 / 成功 / 已启用 | `success` 绿 | 在线采集点、连通、启用态 |
| 离线 / 失败 / 危险 | `error` 红 | 离线采集点、删除按钮、失败态 |
| 停用 / 待处理 / 进行中 | `warning` 琥珀 | 停用、检测中、不可连接 |
| 提示 / 中性信息 | `info` 信息蓝 | 来源标识、空状态、提示条 |
| 主操作 / 当前页 | `primary` 蓝 | 主按钮、当前导航、编码文本 |

**禁用**：不得用`default`（灰）表达业务状态。灰只用于「未配置」这类无状态信息的场景。

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
    --card-accent: var(--md-primary);          /* 色条、图标底 */
    --card-accent-strong: var(--md-primary-strong); /* 强调文字 */
    --card-accent-soft: var(--md-primary-soft);    /* 边框 */
    --card-accent-tint: var(--md-primary-tint);    /* 底纹 */
}
```

其余样式**一律读 `var(--card-accent*)`**，不得为具体色相写独立规则。

### 2.2 强制用法

```razor
<MudPaper Class="platform-stat-card platform-stat-card--tinted platform-accent-success pa-4">
```

- `platform-stat-card` + `platform-stat-card--tinted`：左侧色条 + 极浅底纹
- `platform-page-hero platform-accent-<色>`：页面头部，自动获得色条 + 渐层 + 装饰圆
- `platform-section-card--banded`：区块顶部 3px 色带
- `platform-empty-state platform-accent-<色>`：空状态
- `platform-sheet platform-accent-<色>`：抽屉（子分区可各自再取色）

### 2.3 页面主色分配

**一个页面一个主色**，用于页面头部与抽屉；区块/卡片在主色体系内再取细分色。

| 页面 | 菜单归属 | 主色 |
|---|---|---|
| 平台概览 `/platform-overview` | 平台管理 | primary |
| 点位配置 `/collection-points` | 平台管理 → 采集配置 | info |
| 点位数据管理 `/managed-data-definitions` | 平台管理 → 采集配置 | tertiary |
| 采集服务器配置 `/acquisition-servers` | 未纳入菜单 | success |
| 服务器状态查询 `/server-connection-status` | 平台管理 | info |
| History API 测试 `/history-api-test` | 平台管理 → 接口管理 | warning |
| MQTT 测试 `/mqtt-test` | 平台管理 → 接口管理 | tertiary |
| 采集节点详情 `/managed-data-definitions/{id}` | 平台管理 → 采集配置 | primary |
| 排班时间管理 `/shift-schedules` | 业务配置 → 班组管理 | primary |
| 排班日历 `/shift-calendar` | 业务配置 → 班组管理 | info |
| 能耗仪表板 `/`（别名 `/energy-dashboard`） | 数据指标 → 能耗指标 | success |
| 历史指标 `/energy-history` | 数据指标 → 能耗指标 | tertiary |
| 班组横向对比 `/energy-comparison` | 数据指标 → 能耗指标 | warning |
| 班组能耗报表 `/energy-report` | 数据指标 → 数据报表 | primary |
| 登录 `/login`、改密 `/change-password` | 不在菜单内（AuthLayout） | primary |
| 无权限 `/no-access`、无访问权 `/forbidden`、未上线 `/coming-soon` | 不在菜单内（状态页） | info |

新增页面时必须在本表中登记主色。

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

- 必须带 `--tinted` 与accent 类
- 必须有 `platform-stat-header` 图标行
- 数值文字颜色由 accent 自动决定，**不要再加 `Color="..."`**

### 3.2 数据表格

```razor
<MudTable Class="platform-data-table" Dense="true" Hover="true" Breakpoint="Breakpoint.Sm">
```

- 表头底色/文字色跟随所在卡片 accent
- 编码、地址等标识文本用 `platform-inline-code` + `platform-table-primary`
- 行操作区用 `table-action-grid` + `table-action-button`，按动作语义取色（详情=primary、调试=info、入库=success、编辑=primary、删除=error）
- 表格本身用白色表面（`--platform-surface`），与有色卡片底形成层次，不要继承底色

### 3.2.1 区块卡片与面板

```razor
<MudPaper Class="platform-section-card platform-section-card--banded platform-accent-success --tinted" Elevation="0">
```

- `--banded`：顶部 3px 色带
- `--tinted`：卡片底铺 tint 档 + 左侧 4px 色条，与统计卡同源但更柔和（内容区需要沉静）
- 两者可叠加使用；只加 `--tinted` 不加 `--banded` 也成立
- 卡片内的表格、图表、抽屉仍用白色表面，不要继承底色

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

### 3.4 抽屉与对话框

- 抽屉容器加 `platform-sheet platform-accent-<色>`
- 表单分区用 `platform-sheet-section` + **各自不同的** accent 类
- 对话框行为配置统一走 `Common/PlatformDialogs.cs`，禁止页面内联声明 `DialogOptions`

### 3.5 深色代码/日志面板

```html
<MudPaper Class="platform-code-panel" Elevation="0">
    <div class="platform-code-heading">原始 JSON 响应</div>
    <pre class="platform-code-content">@Json</pre>
</MudPaper>
```

固定深色分层，不随 accent 变化。

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
- 图表底板是 `tint → 白` 的渐变（上 42% 渐隐）+ `soft` 描边。
  这是「卡片要整块铺色」规则的**唯一例外**：绘图区铺满 tint 会让网格线与数据点难以辨认，
  因此只在顶部保留色调、向下渐隐为白。区块卡本身仍然整块铺色。

#### 图表系列颜色：跟随卡片 accent，禁止默认蓝

MudChart 把系列颜色硬编码为 Material 默认蓝（`#2979FF`），写进 SVG presentation attribute。
CSS 规则优先级高于 presentation attribute，因此 `app.css` 用 accent 变量覆盖：

- 折线 `path.mud-chart-line` / 数据点 `circle.mud-chart-point` → `--card-accent`；
- 柱形 `path.mud-chart-bar` → `--card-accent`，悬停加深为 `--card-accent-strong`；
- 网格线统一为 `--platform-border`（替换 MudBlazor 的 `#e0e0e0`）。

**约束：图表必须放在带 `platform-accent-*` 的卡片内**，否则系列色会退回 Primary 蓝。
图例用 `platform-chart-legend` + `platform-chart-legend-swatch`，
色块同样取 `--card-accent`，保证「图例颜色 = 折线/柱形颜色」恒成立，**不得直接写十六进制**。

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

| 层级 | 交互 | 实现位置 |
|---|---|---|
| 顶级模块 | 顶栏三个按钮，点击切换左侧菜单 | `MainLayout.razor` + `.platform-topnav` |
| 分组 | 左侧菜单的分组标题 | `.platform-nav-section` |
| 页面 | 左侧菜单条目，跳转路由 | `NavMenu.razor` + `NavMenuItem.razor` |

当前分配：

| 顶级模块 | 分组 | 页面 |
|---|---|---|
| 数据指标 | 能耗指标 | 能耗仪表板、历史指标、班组横向对比 |
| 数据指标 | 数据报表 | 班组能耗报表 |
| 业务配置 | 班组管理 | 排班时间管理、排班日历 |
| 平台管理 | （直接条目） | 平台概览、服务器状态查询 |
| 平台管理 | 采集配置 | 点位配置、点位数据管理 |
| 平台管理 | 接口管理 | History API 测试、MQTT 测试 |

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
- 新增菜单项只需在目录中登记，守卫测试 `Navigation_Links_Should_Point_To_Existing_Pages`
  会校验每个 href 都有对应的 `@page` 路由，写错路由会直接失败。

### 3.8 月历网格

MudBlazor 9.12 **未提供** `MudCalendar` 组件（只有 `MudDatePicker` 内嵌的日历），
月历需用 `platform-calendar` + `platform-calendar-cell` 自建 7 列网格。

- 周一为每周首日，`.NET` 的 `DayOfWeek` 以周日为 0，需做偏移换算
- 前后补齐空白格保证列对齐，尾部整行落在本月之外时不再生成
- 单元格内色相全部来自父容器的 accent 变量，不写具体色值

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
- 引入资源字典用 `<ResourceDictionary.MergedDictionaries>`，不能直接把 `ResourceInclude` 放进 `ResourceDictionary.Content`
- 品牌色覆盖必须放在 `Application.Resources`，**不能**放在 `Application.Styles`（那里要求 `IStyle`）

---

## 五、硬性禁止项

1. **禁止在页面/组件中硬编码十六进制色值**，一律走 CSS 变量或资源键
2. **禁止为具体色相新增 CSS 规则**，一律复用 `.platform-accent-*`
3. **禁止给 MudBlazor 的 `Color` 参数传入与语义不符的值**（如用 Success 表示"停用"）
4. **禁止在 MudText 上加 `Color` 覆盖 accent 已决定的值**
5. **禁止引入第 8 种色相**，七个角色色 + 中性色是封闭集合
6. **禁止手搓空状态、抽屉、确认框**，统一用既有class 与 `PlatformDialogs`
7. **禁止直接改MudBlazor / Semi 主题源码**，只能通过 `PaletteLight` / `Application.Resources` 覆盖
8. **禁止用渐变把底色稀释回白色**：`linear-gradient(tint → white 78px)` 这类写法会让卡片
   九成面积仍是白色，视觉上等于没上色。要有色就整块铺满（`background: var(--card-accent-tint)`），
   层次靠卡片内的白色表格/图表底板来区分，而不是靠稀释底色。

---

## 六、新增页面检查清单

- [ ] 页面根容器为 `<MudStack Class="platform-page-shell" Spacing="2">`
- [ ] 头部为 `platform-page-hero` + 已登记的 accent 类
- [ ] 统计卡片带 `--tinted` + accent + 图标行
- [ ] 表格带 `platform-data-table`
- [ ] 空状态用 `platform-empty-state`
- [ ] 抽屉用 `platform-sheet` + 分区 accent；对话框走 `PlatformDialogs`
- [ ] 全文件无硬编码色值
- [ ] 在本文档「页面主色分配」表中登记新页面主色

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