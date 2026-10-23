# 项目更新日志

本文档用于记录仓库内已实现或即将提交的变更内容。

## 记录规则

1. 所有提交说明、更新日志内容统一使用中文。
2. 每次准备提交代码前，先在 `未发布` 小节补充本次变更记录，再执行提交。
3. 变更描述应简洁清晰，优先说明“改了什么”和“影响哪里”。
4. 如一次提交同时包含功能、修复、文档调整，可分别记录在不同分类下。

## 变更分类建议

- `新增`：全新功能、页面、模块或文档。
- `优化`：已有能力的界面、交互、性能、结构调整。
- `修复`：缺陷修复、兼容性处理、异常问题修正。
- `文档`：README、说明文档、注释、规范补充。

## [未发布]

### 优化

- **全端消除英文框架文案泄漏**（数据库恢复后的全页面检查发现）：兜底 catch 把 `ex.Message` 直接拼进用户提示时，`No such host is known. (host:443)`、`The operation was canceled.`、`Response status code does not indicate success: 400` 这类英文文案会出现在界面上，还可能顺带泄漏内网地址。本次系统性修复：
  1. `PlatformApiClient` 的 11 处 `EnsureSuccessStatusCode()` 改为 `EnsureSuccessAsync()`——从端点返回体的 `{ "message": "中文原因" }` 提取业务错误并抛 `InvalidOperationException`，不再把英文状态码抛给页面；
  2. 新增 `UserFacingErrors.Describe(ex)` 助手（Web 端与 Avalonia 桌面端各一份）：超时 → 「操作超时，请稍后重试」、业务异常 → 服务端中文消息、网络不可达 → 「网络请求失败」、其余 → 「请查看服务端日志」；
  3. Web 端 12 个文件共 31 处、桌面端 4 处 `ex.Message` 插值全部改为走助手；
  4. 服务端 `ConnectionDiagnosticsService.ProbeAddressAsync`、`AcquisitionServerProbeService`（TCP + HTTP 两处）、`AcquisitionServerEndpoints`（两处）、`ManagedDataExecutionService` 的兜底分支改为中文结论，原始异常只进日志；
  5. 新增守卫测试 **`Catch_Blocks_Should_Not_Leak_Raw_Exception_Message`**：扫描四类源码目录中 `{ex.Message}` 插值与 `ErrorMessage = ex.Message` 赋值两种形态，已用「注入缺陷 → 确认拦截 → 恢复」验证有效性。
- **图表系列颜色接入设计色板**：MudChart 把折线/柱状图系列颜色硬编码为 Material 默认蓝（`#2979FF`），与七色体系完全脱节（蓝卡绿卡里的折线全是同一个蓝）。由于该颜色写在 SVG presentation attribute 上、优先级低于 CSS，现于 `app.css` 按所在卡片的 `--card-accent` 覆盖：折线、数据点、柱形与卡片色条同色相，柱形悬停加深一档，网格线统一为中性边框色；图例色块同源取色，「图例颜色 = 折线颜色」恒成立。
- **图表底板着色与描边**：图表容器由纯白改为 `tint → 白` 渐变（上 42% 渐隐）+ `soft` 描边，绘图区带上所在卡片的色调，同时网格线与数据点仍清晰可辨（区块卡整块铺色规则的唯一例外）。
- **修复 28 处 `--tinted` 无效类名**：`Class="... pa-4 --tinted"` 里的 `--tinted` 不是任何已定义的 CSS 类，实为 `platform-section-card--tinted` / `platform-panel--tinted` 的残缺写法——受影响的区块卡从未真正上过底色。已按基类分别修复，涉及 12 个页面。
- **图表纵轴标题带上单位**：仪表板/历史指标按趋势单位、横向对比按指标单位、节点详情按最近记录单位动态生成 `YAxisTitle`，读图不再需要猜量纲。
- **图表响应式适配**：1600/1280/960/600/375px 五档实测绘图区占 SVG 宽度 78–92%、轴文字全部可见；≤600px 时容器降至 300px 高并收紧内边距，X 轴标签自动旋转 -35°（等距标签窄屏水平排列会互相压字）。
- `PlatformChartDefaults.CreateBarOptions` 注明 `BarChartOptions` 无 `YAxisRequireZeroPoint`（仅折线选项有），避免后续误加导致编译失败。

### 新增

- **班组排班时间管理页** `/shift-schedules`：上传 Excel 台账（自然日、班组名称、上班时间、下班时间）导入四班三运转排班，导入后自动执行校验并输出报告（覆盖天数、轮休分布、逐条问题）；支持清空重导与按班组筛选明细。
- **排班日历页** `/shift-calendar`：自建月历网格展示每天班次，标注在岗班组、轮休班组与「未完整覆盖 24 小时」；右侧联动当日明细，并可切换到单个班组查看其当月出勤班次、累计工时与轮休天数。
- **能耗仪表板页** `/energy-dashboard`：上方为当前班组 4 项原辅材料/能源指标合计（含折算标准煤的综合能耗），下方为 4 个独立趋势图，并附趋势明细表。
- **班组历史指标页** `/energy-history`：按日/周/月查看指定班组四项指标的历史趋势与均值、峰值、走势方向，附明细宽表。
- **班组横向对比页** `/energy-comparison`：按日/周/月对比全部班组的 4 项指标，自动标出综合能耗最高与最低班组及极差，附含出勤班次与工时的对比明细。
- 新增领域契约 `ShiftSchedulingContracts.cs`、`EnergyMetricContracts.cs`，端点 `ShiftSchedulingEndpoints`、`EnergyMetricEndpoints`，以及服务端 `ShiftScheduleExcelParser`、`ShiftSchedulingService`、`EnergyMetricService`。
- 新增 **`ShiftRotationValidator`**：四班三运转校验器，校验每班 8 小时、班次连续无空档覆盖 24 小时、同日同班组唯一、轮休均衡（各组差异不超过 1 天），并提供按日分组与班组工时汇总。
- 新增单元测试：四班三运转校验（7）、Excel 解析（8）、能耗服务（8）、共享编辑组件与守卫（5）。

### 新增

- 新增单元测试 **55 项**（87 → 142），覆盖此前完全没测到的核心服务：
  - `InMemoryShiftSchedulingServiceTests`（17 项）—— 排班服务导入/查询/清空全链路，重点验证**校验不通过的文件不得覆盖已有可用排班**这一关键约束；
  - `ShiftScheduleExcelParserBoundaryTests`（12 项）—— 解析器边界：文件超限、空工作表、空班组名、时间格式变体（`8:00`/`8时30分`/Excel 小数序列号）、轮休标记、损坏文件；
  - `ServerConnectionSettingsTests`（16 项）—— 连接参数解析：凭据、布尔开关、超时数字在缺失/空白/类型不符时的行为，重点验证**非法值不能默默变成默认值**（否则生产环境会意外切到 mock 响应或证书校验被关闭）；
  - `HistoryApiProxyRequestTests`（10 项）—— 通过注入 `HttpMessageHandler` 验证请求 URL 拼接、位号转义、上游 5xx/超时/网络不可达时的中文提示。
- 新增 `DAP.Tests.UnitTests/coverage.runsettings` 与 `scripts/core-coverage.sh`：一键采集覆盖率并**按口径分两组汇总**——「核心逻辑」是可脱离数据库独立验证的业务代码，「端点/仓储层」需要真实宿主与数据库。单一数字会把 3900 行未真正测到的仓储代码混进来，让覆盖率虚高，因此刻意分开呈现；核心逻辑低于 70% 时脚本返回非 0。

### 修复

- **网络异常时泄露英文框架文案**：History API 代理的兜底 catch 把 `ex.Message` 直接拼进用户可见提示，界面会显示 `无法连接到采集服务器：No such host is known. (hist.invalid:443)`——既不中文，也可能顺带泄漏内网地址。现将异常分为三类处理：`HttpRequestException`（网络不可达）只给中文结论、`InvalidOperationException`（配置/令牌问题，消息本身已是中文）可安全展示、其余异常统一提示查看日志。
- **`24:00` 导致导入崩溃**：用户把跨天班次写成 `24:00` 时，Excel 存为序列号 1.0（正好 24 小时），而 `TimeOnly` 取值上限到 24 小时为止，`TimeOnly.FromTimeSpan` 会抛 `ArgumentOutOfRangeException` 打断整个导入。现把 24:00 显式归一化为 `00:00`，不再报错。
- **表头带空格时无法识别**：现场台账表头常从 PDF/网页粘贴而来（如「自 然 日」），`Contains("自然日")` 匹配失败导致整表导入失败。现匹配前先剥离表头中的全部空白字符（含全角空格）。
- **横向对比图表错位**：班组横向对比此前把「每个班组建成一条数据系列」，而 MudChart 的横轴是标签位置而非系列——结果是 X 轴只剩一个班组标签、4 根柱子挤在同一位置，横向对比反而看不清。现改为「1 条系列（系列名=指标名）+ N 个班组标签」，横轴正确显示各班组并可直接比高低。
- **空状态与加载圈并存**：排班管理页的 `NoRecordsContent` 在 `Loading=true` 时也会被 MudTable 渲染，首屏因此同时出现进度圈与「还没有排班数据」这种错误空态。现让空状态一并受 `_isLoading` 约束。
- **上传示例给出无效时刻**：抽屉内的填写示例把跨天班次写成 `16:00-24:00`，而 `24:00` 在部分 Excel 版本中会被存成日期序列号导致解析失败。示例改为 `16:00-00:00`，并在说明与时间解析错误提示中明确告知这一要求。
- **历史指标页缺少主色区块**：该页只有 4 个插值注入 accent 的图表卡片，页面主色（tertiary）几乎不出现，视觉重心与仪表板页不一致。现补上与仪表板同构的「四项指标趋势」区块卡。
- **Excel 时刻解析失败**：部分写库工具（如 openpyxl）会把时间列写成 `TimeSpan` 型单元格，而 ClosedXML 的 `TimeSpan` 转换会带回 Excel 日期序列号（实测得到 46315 天），导致 `TimeOnly.FromTimeSpan` 抛异常、整个文件导入报「解析失败」。现对 `TimeSpan` 取值先做 0~24 小时合理性判断，超界时回退按 `DateTime` 解析，日期列同样加固。
- **能耗看板忽略指定班组**：`GetDashboardAsync` 接收 `teamCode` 参数却完全未使用，导致页面「查看班组」选择器形同虚设、切换班组数值不变。现显式指定班组时按指定班组展示，未指定时才推断当前在岗班组。
- **历史指标区间被悄悄放大**：日粒度下跨月的查询区间会被强行扩成两个完整自然月，30 天查询变成 61 天，数据点翻倍且合计口径与用户所选范围不符。现改为尊重调用方区间，仅在区间过长时收敛桶宽。
- **趋势曲线末端虚假归零**：分桶算法早期按「等分跳过」取桶，导致最后一个桶吞掉整段区间，曲线尾部出现与业务无关的归零跌落。现改为按桶宽整齐切分，末桶截断到区间末尾。
- **Razor 表达式未求值**：图例与卡片副标题中 `@($"{trend.MetricName}（峰值 @FormatValue(trend.Peak) ...）")` 这类嵌套插值、以及纯文本中的 `@Method()` 调用不会被 Razor 求值，会把表达式原样打印到页面上（界面直接显示 `@($"{FormatValue(_highestTotal)} ...")`）。现统一改为在代码后置拼好文案后整体输出。
- **DateOnly 格式化失效**：`@value.BucketStart:yyyy-MM-dd` 对 `DateOnly` 不生效，界面显示为 `10/20/2026yyyy-MM-dd`。现一律在代码后置用插值字符串格式化。
- **历史指标 X 轴标签重叠**：30 个以上数据点时 MudChart 会等距绘制全部标签，界面上挤成一条黑色横带。现对标签抽稀，最多保留 12 个。
- **排班日历误报「不完整」**：未排班的日期被当作「未完整覆盖 24 小时」标红，整月出现大片红色噪音。现仅对已排班且覆盖不足的日期标注状态。

### 优化

- **全站配色强化（解决「整体太素」）**：诊断出「素」的根因不是颜色不够艳，而是**底色层与白色表面的对比度只有 1.05~1.09 倍**——肉眼完全分辨不出，于是所有卡片实际都呈现为白色，再鲜艳的 base 色也只出现在 3~4px 的色条上。三项针对性改动：
  1. **色板饱和度与层级整体加深**（同色相内调整，未新增任何色相）：Base 提高饱和度（如 primary `#1A5FB4` → `#0B4FC4`），Tint 加深到与白底对比 ≥ 1.20、Soft 加深到 ≥ 1.25，深色档（strong）同步调整以保证卡片数字在有色底上仍可读；
  2. **底色改为整块铺满**：`platform-stat-card--tinted` 由 `linear-gradient(tint → white 78px)` 改为整块 `soft` 底，`platform-page-hero` 与新增的 `platform-section-card--tinted` / `platform-panel--tinted` 整块 `tint` 底——早期渐变让卡片九成面积仍是白色，视觉上等于没上色；
  3. **新增区块底纹变体**：`platform-section-card--tinted` / `platform-panel--tinted` 提供「左侧 4px 色条 + tint 底」，12 个页面的区块卡统一应用；卡内的表格与图表区保持白色底板形成层次。
- **次要文字加深**：`--platform-text-muted` 由 `#56637A` 改为 `#4E5A70`——原值在 tint 底上对比度仅 4.16~4.41，低于 WCAG AA 的 4.5:1，加深后对全部 14 个底色档均 ≥ 4.77:1。
- **日历网格融入容器底色**：日历单元格由固定白色改为继承所在区块底色，网格线跟随 accent soft 色而非中性灰；状态标签改用 base 档底 + 白字，在 tint 底上层次分明。
- 导航按业务域重组为「采集运维 / 班组排班 / 能耗指标」三组；导航图标取色由 `nth-child` 改为 `platform-nav-link--*` 修饰类——分组后同一父级下存在多个 `MudNavMenu`，`nth-child` 已不再可靠。
- `DesignSystemGuardTests.Stat_Cards_Should_Declare_Accent_Class` 支持指标卡片在插值中注入 accent（要求代码后置存在 `MetricAccents` 白名单常量），使守卫测试既能拦住硬编码色相，又不误伤按指标顺序取色的规范用法。
- 新增守卫测试 **`MudChart_Series_And_Labels_Should_Share_Same_Source`**：强制 `ChartSeries` 与 `ChartLabels` 的取值方法同源（同方法，或读取同一集合）。MudChart 按「系列 × 标签」定位，标签与数据不同源时图表会退化成单标签、多系列重叠，而这类缺陷编译与单测都发现不了，只有渲染后才暴露。
- 排班日历页加载态统一为 `platform-panel` 容器，与其余页面保持一致（此前是裸 `MudStack` 手搓）。
- 完成全站设计一致性核查：13 个页面的主色登记、accent 分布、卡片/表格/空状态用法、图表尺寸、内容溢出与 JS 错误逐项核对；并以 10 项交互用例验证统计口径切换、班组切换、粒度切换、月份导航、日期联动与上传抽屉。

### 文档

- `docs/DESIGN_SYSTEM.md` 登记 5 个新页面的主色（排班管理=primary、排班日历=info、能耗仪表板=success、历史指标=tertiary、横向对比=warning），并补充「指标卡片 accent 取色」「图表容器」「导航分组」「月历网格」四节规范；新增「MudChart 数据绑定的两个硬规则」，写明系列与标签的对应关系及写反后的症状。
- `docs/DESIGN_SYSTEM.md` 与 `AGENTS.md` 同步更新色板表与**四条对比度底线**（白字/base ≥ 4.5、tint/白底 ≥ 1.20、soft/白底 ≥ 1.25、strong/底色 ≥ 4.5），并写入「禁止用渐变把底色稀释回白色」这一条硬性禁止项。
- 新增守卫测试 **`Palette_Should_Meet_Contrast_Baselines`**（7 项）与 **`DesignSystemDoc_Should_Match_AppCss_Palette`**：前者把四条对比度底线固化为断言，防止日后「改配色」改成「变淡」或「看不清」；后者确保规范文档的色板表与 `app.css` 权威变量不漂移。两项均已用「注入缺陷 → 确认拦截 → 恢复正常」验证有效性。

### 新增

- 新增共享组件 **`Components/ManagedDataDefinitionEditor.razor`**：采集节点编辑抽屉，封装「先选采集服务器、再补点位信息」的完整表单（基础信息、目录驱动的专属参数、扩展协议字段、业务标识、连接测试、双 JSON 预览），供节点列表页与节点详情页共用，避免两处表单逻辑各自漂移。
- 采集节点详情页 `/managed-data-definitions/{id}` 新增**「编辑节点」按钮**：直接唤起共享编辑抽屉，保存后自动刷新当前值、历史趋势与配置摘要，无需再返回列表页操作。

### 优化

- 节点列表页移除内嵌编辑对话框与约 260 行表单标记/逻辑，改用共享组件；删除随之失效的字段（`_servers`、`_serverTypes`、`_nodeConfiguration`、`_businessTagRows`、`_extraConfigurationRows` 等）与辅助方法。
- 共享编辑器补齐 `保存中/测试中` 按钮禁用与加载态，表单校验失败由页内消息改为抽屉内提示，不再抢占页面级消息条。

### 修复

- **设计规范违规**：节点停用状态此前用 `Color.Default`（灰）表达，违反 `docs/DESIGN_SYSTEM.md` 1.4「不得用灰色表达业务状态」；列表页与详情页的停用标签统一改为 `Color.Warning`（琥珀）。
- 节点详情页「未找到该采集节点」空状态缺少引导出口，现补充说明文案与「返回后台数据管理」按钮；同时改用规范的 `platform-section-card` + `platform-accent-error` + `platform-empty-state` 组合，替换未定义样式的 `platform-panel` / `node-details-loading`。
- **首屏被上游超时阻塞**：节点详情页此前在渲染前同步等待一次实时读取，采集服务器不可达时需等满超时（约 30 秒）才出内容，页面全程停在「正在加载」。现改为先渲染详情、再后台补实时读取，实测首屏从约 30 秒降至 **77ms**。
- **框架文案外泄**：采集服务器不可达时，界面直接显示英文 `A task was canceled.`。根因是 `HistoryApiProxyService` 用超时令牌发起请求，抛出的 `TaskCanceledException` 被通用 `catch` 捕获并把 `ex.Message` 原样返回。现区分「自身超时 / 调用方取消 / 其他异常」三种情况，分别给出可读中文提示。

### 新增

- 新增 **`docs/DESIGN_SYSTEM.md`** 作为全项目 UI 设计的强制规范：定义 Material Design 3 七色封闭体系（Primary / Secondary / Tertiary / Success / Warning / Error / Info，每色 Base / Soft / Tint 三级）、语义色映射表、Web 端 accent 修饰类机制、组件规范（Avalonia 的半覆盖键清单与编写注意）、七条硬性禁止项、新增页面检查清单。
- 新增 **`AGENTS.md`** 作为 AI 协作约束入口：要求改动 UI 前必读设计规范，明确禁止硬编码色值、禁止为具体色相新增 CSS 规则、规定改色板需同步的四处文件，并记录「组件 API 不可臆测」等踩坑经验。
- 新增 `DAP.Presentation.AvaloniaApp/Themes/DapPalette.axaml`：桌面端统一品牌色板（`Dap*Brush` / `Dap*Color`），与 Web 端 `app.css` 的 `--md-*` 一一对应，作为双端配色对齐的载体。
- 新增 `DAP.Presentation.AvaloniaApp/Themes/SemiBrandOverride.axaml`：覆盖 Semi 主题默认调色板（默认亮蓝 `#0064fa`），使桌面端与 Web 端视觉统一；经`TryGetResource` 核实 10 个覆盖键均真实存在于 Semi.Avalonia 12.1.0.1。
- 新增 `DAP.Tests.UnitTests/DesignSystemGuardTests.cs`（20 项断言）：把设计规范固化为自动化守卫——检测页面硬编码十六进制色值、`app.css` 与 `MudTheme` 色值漂移、Web 与桌面端色板不一致、Axaml 注释非法双连字符、页面根容器与统计卡片的 accent 缺失、以及出现白名单外的第8 种色相。

### 修复

- 桌面端此前使用 Semi 主题默认亮蓝 `#0064fa`，与 Web 端品牌蓝 `#1A5FB4` 不一致，双终端观感割裂；现已通过品牌色覆盖层统一。
- `NotFound.razor` 根容器为裸 `div`，未使用统一的 `platform-page-shell`，且图标显式指定 `Color.Warning` 与 `not-found-avatar` 的警告色底重复；现改为统一根容器 + `platform-accent-warning` 强调，图标色交由 CSS 决定。

### 文档

- `README.md` 新增「设计规范」章节，指向 `docs/DESIGN_SYSTEM.md` 与 `AGENTS.md`，并说明色板权威来源与核心约定；项目结构树补充 `Themes/` 目录与两个规范文档。

### 清理

- 删除空壳项目 `DAP.Collectors`：整个项目仅有 3 个模板 `Class1.cs`，零业务实现、零项目引用，已从 `DAP.slnx` 移除。
- 删除 `DAP.Infrastructure` 下`Redis/` 与 `TimeSeries/` 的模板 `Class1.cs` 及空目录；全项目无 Redis 实现，README 中「通过 Redis 实现分布式缓存」的描述已同步修正。
- 删除 `IPlatformApiClient` 中两个无调用方的方法 `GetServerConnectionStatusesAsync`、`GetAcquisitionServerAsync`（含客户端实现与服务端扩展方法），底层 `ConnectionDiagnosticsService` / `AcquisitionServerService` 对应方法仍被 API 端点使用，予以保留。
- 清理样式冗余：移除 27 个在 CSS 中无任何定义的无效类名引用，删除 `app.css` 中的死规则 `.platform-empty-state`、`.platform-field-row`、`.node-server-hint`，删除已无引用的 `ManagedDataDefinitions.razor.css`。
- 删除自建的 `Common/ConfirmDialog.razor`：其遮罩、按钮布局与等待态已由各页面内联的 `MudDialog` + `DialogActions` 承接，通用组件反而成为调用方必须绕开 `IsOpen` 的额外抽象。
- 删除随 `@key` 强制重建表单一并失效的 `_editorRenderKey` 字段与 `ResetEditorRenderKey()` 方法（迁移到 `MudDialog` 后已无写入方读取）。

### 优化

- 全站配色升级为完整 Material Design 3 色板：在 `app.css` 建立 7 组角色色（`--md-primary` / `--md-secondary` / `--md-tertiary` / `--md-success` / `--md-warning` / `--md-error` / `--md-info`），每组提供 base / soft / tint 三级，取代此前零散的十六进制硬编码。
- 新增 8 个 `.platform-accent-*` 语义修饰类：仅向容器注入 `--card-accent` 系列变量，页面头部、统计卡片、表头、抽屉分区、摘要块、空状态等样式统一读取该变量，实现「加一个类即换一整套配色」，后续新增配色无需再写样式规则。
- 页面头部升级：主色左色条 + 105° 渐层背景 + 右上角色块装饰；各页分配独立主色（概览=蓝、采集点=信息蓝、后台数据=紫、采集服务器=绿、History API=琥珀），提升页面辨识度。
- 统计卡片统一加 3px 左语义色条与极浅同色底纹，并新增 `.platform-stat-header` 图标行，为 20 余张指标卡补上语义图标。
- 数据表头底色与文字色、表头下划线、悬停行高亮改为跟随所在卡片的主色；编码列等宽文本改用主色深阶，与普通文本拉开层次。
- 服务器连接状态页的卡片配色改为随探测结果实时联动：`GetStatusCardClass` 由返回 `server-status-card--online/offline/...` 改为返回 `platform-accent-success/warning/info/neutral`，可连接为绿、不可连接为橙、检测中为蓝、未配置为中性灰。
- 抽屉与对话框：头部改为浅色渐变并染上页面主色，表单分区（基础信息 / 连接配置 / 采集信息 / 业务标识 / 协议配置）按语义分配蓝、绿、橙、紫左色条，替代此前全部分区同色的单调观感。
- 4 处空状态统一为 `.platform-empty-state` + 圆形图标底色（替代 `MudStack` + `pa-10` + 默认色图标）。
- 深色代码 / 日志面板改为分层深色（顶部标题栏微亮、正文浅蓝灰文字），与浅色页面形成明确对比。
- `MudTheme.PaletteLight` 补齐 `Tertiary`、`Divider`、`TableLines`、`TableHover`、`Skeleton`、`TextDisabled` 等字段，与 CSS 色板同源，修复组件默认色与自定义样式不匹配的问题。
- 侧边导航图标按功能分区着色（蓝 / 信息蓝 / 紫 / 绿 / 琥珀），当前项图标与文字同步高亮。

- 升级 MudBlazor 至NuGet 最新稳定版 **9.11.0**（此前 9.10.0）；经反射核实该版本无新增或移除的公开组件类型，属无破坏性升级。
- 抽屉与对话框全面改用 MudBlazor 官方组件：将 4 处 `MudOverlay` + 原生 `div` 手搓右侧抽屉（采集点、采集服务器、采集定义编辑，节点配置详情）与 3 处 `MudOverlay` 手搓确认框，统一替换为 `MudDialog` + `DialogContent` / `DialogActions`，借助组件内置的遮罩、ESC 关闭、焦点管理与过渡动画，替代自行维护的定位与层级样式。
- 新增 `Common/PlatformDialogs.cs` 集中声明 `DialogOptions`：`EditorSheet` / `DetailSheet`（右侧抽屉，编辑态禁止点击遮罩关闭以防误触丢失表单）与 `Confirm`（居中二次确认），消除各页面重复配置对话框行为。
- 确认框改用内置 `DialogActions` 承载按钮区，等待态由 `HourglassTop` 图标 + 按钮文案反馈替代自定义样式。
- 统一页面根容器：全部改为 `<MudStack Class="platform-page-shell" Spacing="2">`，消除此前 `MudStack` 与原生 `div + tw-flex` 两种写法并存、且与 `Spacing` 重复声明 `tw-gap-4` 的不一致。
- 修正无障碍焦点导航：`FocusOnNavigate` 的选择器由失效的 `h1` 改为 `.mud-typography-h4`（项目使用 MudText 渲染标题，无 `h1` 标签，原设置完全失效）。
- 修复跨组件样式作用域问题：新增全局 `.platform-monospace` 承载等宽文本，替代仅在 `ManagedDataDefinitions` 作用域内有效、导致「采集服务器配置」页等宽字体失效的 `managed-data-monospace`。
- 修复 `ManagedDataDefinitionDetails.razor.css` 中 `.node-details-drawer-card ::deep .mud-typography` 的非法 `::deep` 位置（整条规则被浏览器丢弃），并补齐 `.node-details-metric-support` 的定义，使统计卡辅助文字正确灰化。
- 修正节点配置页 MudSelectItem 的全角括号插值写法，改为 `@($"{server.DisplayName}（{server.ServerTypeName}）")`，避免 Razor 隐式表达式与全角符号混排。

### 新增

- 新增采集服务器配置功能：支持登记 GE Historian、InfluxDB、Modbus、MQTT、OPC DA、HTTP API 六类采集服务器，集中维护基础地址、鉴权凭据与接口路径。
- 新增 `AcquisitionServerCatalog` 服务器类型目录，以元数据方式描述各类型的服务器级字段与节点级字段，界面按类型动态渲染表单并驱动必填校验。
- 新增「采集服务器配置」页面（`/acquisition-servers`），支持列表、筛选、新增、编辑、删除与逐台连通性测试，并展示测试方式、状态码、耗时与返回报文。
- 新增通用接口探测服务 `AcquisitionServerProbeService`，支持网络连通性探测与 HTTP 接口读取；Historian 类型复用既有 OAuth2 代理，接口路径可由服务器配置覆盖。
- 新增 `/api/acquisition-servers` 系列端点，提供服务器列表、类型目录、详情、保存、连通性测试与删除能力。

### 优化

- 节点配置改为「先选采集服务器、再填采集信息」：选定服务器后自动复用其地址与鉴权配置，采集方式与连接地址不再由节点重复填写。
- `ManagedDataDefinition` DTO 与查询补充 `ServerConnectionId` 与 `ServerName`，节点列表与详情可直接展示所绑定的服务器。
- 节点「测试并读取返回值」升级为真实接口调用，展示访问目标、状态码、解析数值与原始报文；Historian 最新值按 `tagName` 精确匹配当前点位。
- 密钥字段统一掩码回显，保存时识别掩码占位并保留库中真实凭据，避免编辑其他配置时意外覆盖密钥。

### 修复

- 修复 `HistoryApiProxyService` 凭据回退逻辑：服务器连接配置中 `clientId` / `clientSecret` 为空字符串或纯空白时不再覆盖全局配置，改为回退到 `appsettings.json` 的 `HistoryApi` 凭据，避免 Historian 采集因空凭据而失败。
- 修复接口路径重复拼接：服务器基础地址若误填 `/historian-rest-api` 等接口路径，运行时将与接口路径叠加导致 404，现已在保存前直接拦截并提示。
- 修复通用探测服务未设置 `HttpClient.BaseAddress` 导致 HTTP 接口测试无法发出请求的问题。

### 新增

- 新增 `server_connections` 外部服务器主数据表、`AddServerConnections` 与 `LinkManagedDefinitionsToServerConnections` EF Core 迁移，并回填已有服务器记录及节点外键关联。
- 保存后台数据定义时自动登记服务器并绑定节点，避免后续新增服务器无法出现在连接状态页；共享服务器连接配置不再被单个节点覆盖。
- 新增 History API 代理单元测试，覆盖多标签拆分与空凭据回退场景。
- 新增 `ExtendAcquisitionServerConfiguration` 迁移，为采集服务器补充说明、创建时间与最近一次连通性测试结果等字段。
- 新增采集服务器类型目录与接口探测单元测试，覆盖类型解析、必填校验、地址拦截与返回值提取。

### 优化

- 统一 Blazor Web 管理端页面视觉风格，补齐布局、导航、卡片、表格和状态页样式。
- 精修 `/managed-data-definitions` 页面信息层级与操作区布局，修复操作列图标与文字显示不协调的问题。
- 服务器连接状态页改为异步卡片监控视图，支持逐台网络探测并展示协议、地址、凭据、执行模式和关联节点数量。
- Historian 数据定义支持将地址、ClientId、ClientSecret 与 Mock 开关保存到数据库配置中。
- 补全 `HistoryApi` 全局连接配置（Historian 服务器地址、OAuth 端点与凭据），并将开发环境 `UseMockResponses` 关闭，便于验证真实数据链路。

### 文档

- 新增 `CHANGELOG.md`，明确后续提交说明与变更记录统一使用中文。
- README 补充外部服务器数据库配置与 EF Core 迁移执行说明。
