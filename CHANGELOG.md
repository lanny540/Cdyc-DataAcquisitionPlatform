# 项目更新日志

本文档用于记录仓库内已实现或即将提交的变更内容。

## 记录规则

1. 所有提交说明、更新日志内容统一使用中文。
2. 每次提交以年月日为标题，内容为本次提交的变更内容。变更描述应简洁清晰，优先说明“改了什么”和“影响哪里”。
3. 如一次提交同时包含功能、修复、文档调整，可分别记录在不同分类下。

## 变更分类建议

- `新增`：全新功能、页面、模块或文档。
- `优化`：已有能力的界面、交互、性能、结构调整。
- `修复`：缺陷修复、兼容性处理、异常问题修正。
- `文档`：README、说明文档、注释、规范补充。

## [未发布]

### 新增

- 新增 `scripts/sql/` 数据库部署脚本，用于生产环境未安装 `dotnet ef` 时手工建库：
  - `01_schema.sql`：由 `dotnet ef migrations script --idempotent` 生成，覆盖 11 个迁移、23 张表，可重复执行。
  - `02_seed_reference.sql`：职能域、权限点、内置角色、角色权限、能源介质/用途/系统的幂等种子。
  - `03_verify.sql`：部署后核对迁移版本、表完整性、字典行数与外键悬空。

### 变更

- UI 视觉语言整体切换为 **Material Design 2 · 品牌红 + 琥珀（示意稿定稿）**（覆盖下方此前的浅色红金方案；
  用户目检后由企业蓝靛、经典紫两轮改定，最终以 `MD2_仪表板改版示意` 视觉稿为准）：
  - 七色色板再次整体换色：primary 改为亮红 `#D32F2F`（Red 700，配白字，strong `#A31515` / soft `#F4B5B5` /
    tint `#FFCDD2`），tertiary 强调色改为琥珀 `#FFB300`（strong `#8F4400` / soft `#FFD54F` / tint `#FFE082`，
    填充底配深褐字 `#3E2A00`），info 改 `#1976D2`、success 改 `#2E7D32`、secondary 改 `#455A64`；
    旧品牌红变量体系 `--platform-brand*` / `DapBrand*` 退役，语义由 primary（同色值）承接。
  - MD2 示意稿卡片语言：红色实底顶栏（主色+白字+4dp 投影）、页面底改中性灰 `#FAFAFA`、
    **灰底白卡加 MD2 投影**（统计卡/区块卡/面板不再铺 tint 底纹、不再加左侧色条与顶部色带）、
    页面头部改无色块白底直排标题（hero 不再有 tint 色块）、统计卡图标底块改**圆形实色 + On 档图标**、
    表格表头去底纹（中性灰字 + 12% 黑分隔线）、图表容器去渐变底纹、排班日历中性化、
    100 阶浅底徽章保留、黑色透明度阶文字（.87/.60/.54）、4dp 圆角。
  - 桌面端同步：`MetricCardModel` 新增 `AccentOn` 档（圆形图标底块前景，琥珀底为深褐），
    指标卡/趋势卡/柱状卡/报表汇总卡改白卡，六页 hero 去 tint 底，DataGrid 表头透明。
  - 页面头部不再一页一色：22 页 hero 统一 `platform-accent-primary`（桌面 6 页同步 `DapPrimaryTintBrush`），
    页面个性由卡片 accent 表达；`DESIGN_SYSTEM.md` 的「页面主色分配」表改为登记卡片主色。
  - 次要文字加深至 `#4F4F4F`（原 `#616161` 在 tint 底纹上对比不足 4.5:1），双端同步。
  - 守卫测试改版：颜色清单更新为新七色；`Neutral_And_Brand_Palette_...` 更名
    `Neutral_Palette_Should_Meet_Contrast_Baselines`（删除品牌红段）；`Error_Should_Be_Distinguishable_From_Brand_Red`
    改为 `Error_Should_Be_Distinguishable_From_Accent`（对 primary 与 tertiary 双查 ΔE ≥ 20，
    当前亮红 vs 深酒红 ΔE ≈ 73.8、琥珀 vs 深酒红 ΔE ≈ 192）；
    `Data_Containers_Should_Not_Use_Primary_Accent` 改为 `Desktop_Data_Containers_Should_Not_Use_Primary_Pale_Brushes`
    （MD2 下 primary 参与正常 accent 轮换，桌面保留淡色底纹禁令并豁免 page-hero）。
  - `docs/DESIGN_SYSTEM.md` 与 `AGENTS.md` 同步改写为 MD2 口径。
  - 示意稿定稿后 Web 端全站扫尾（用户确认满意后按同风格复查 25 页 + 全部 scoped CSS）：
    顶栏品牌标识由主色→强调色渐变改**白圆底 + 主色红图标**（与统计卡圆形图标底块同语言）、
    表单抽屉分节卡去左侧色条、深色代码/日志面板去渐变改纯色、登录卡去 1px 描边（保留顶部品牌红条）、
    数字孪生车间面板去 14px 圆角与描边改 4dp 白卡投影、受管数据详情趋势图面板去 accent 描边与
    tint 渐变底改白卡、重连弹窗图标底块圆形化；同步清理「主色紫/强调青」等历次改版残留注释。
- UI 整体由深色主题改为**浅色红金配色**（Material Design 3），解决深色下文字不明显、边框不好看的问题：
  - 七色色板整体换血并各增一档对比文字（`-on`）：primary 改为金色 `#F7B500`（配深字 `#3E2A00`），
    error 取深酒红 `#8E1B1E`（与品牌红 `#D32F2F` 拉开明度，色差 ΔE 25），strong 回归加深档，soft/tint 改为淡色容器；新增品牌红 `--platform-brand*`。
  - 红金分工：金色承担主按钮、开关、选中、输入焦点；品牌红仅用于顶栏激活、抽屉选中、品牌标识与登录入口，
    页面不得引用（守卫测试强制）；红色的内容语义完全留给 error。
  - 卡片恢复整卡铺色：统计卡/区块卡铺 tint 底纹并配本色 soft 描边，头条 KPI 铺 soft；卡内表格保持白色表面。
  - 中性色提亮对比：正文 `#1A1D21`（16.9:1）、次要文字 6.9:1、弱化文字 4.56:1、输入框边框 3.04:1。
  - Web 端 `IsDarkMode="false"`，`PlatformTheme` 只保留浅色面板；`app.css` 新增「金色主色的文字态修正」，
    把 MudBlazor 直接取 primary 当前景色的描边/文字按钮、聚焦态输入标签与边框改为加深档。
  - 桌面端同步：`App.axaml` 改 `RequestedThemeVariant="Light"`，`DapPalette.axaml`/`SemiBrandOverride.axaml`
    换浅色色板并新增 `Dap*OnBrush` 与 `DapBrand*Brush`，指标卡/区块卡铺 tint 底，导航选中取品牌红，
    主按钮补金底深字前景。
  - 守卫测试同步：对比文字改读 `-on` 档，新增中性色/品牌红对比度守卫与「保持浅色主题、品牌红不进内容区」守卫；
    `DESIGN_SYSTEM.md`、`AGENTS.md` 与 README 截图同步更新。
  - 错误色重新定稿为深酒红 `#8E1B1E`（Strong `#5C0E10`）：第一版取 M3 基线 `#B3261E`，与品牌红 `#D32F2F`
    色差只有 ΔE 10；现靠明度拉开（ΔE ≈ 25），亮红＝导航、暗红＝错误。新增守卫 `Error_Should_Be_Distinguishable_From_Brand_Red`（ΔE ≥ 20）。
  - **组件风格全站统一**（外观由动作角色决定，页面强调色不再参与；规范见 `DESIGN_SYSTEM.md` 3.10）：
  - 按钮：主操作＝Primary 填充（新建/查询/保存/导入），次操作＝Secondary 描边（刷新/重置/导出/测试），取消/关闭/返回＝
      Secondary 文字，破坏性确认＝Error 填充；清掉各页面自定的蓝/紫/深灰蓝/琥珀填充按钮，仪表板视图切换改为
      「选中金色填充 / 未选中深灰蓝描边」，表格行内操作统一为小号文字按钮。
  - 输入类组件统一为 Outlined + Dense（权限管理五页原为下划线形态）；开关统一 Primary；徽章统一 Outlined；
      筛选栏宽度口径统一，MudSelect 外层补 `:has()` 约束使其不再撑满整行。
  - 桌面端：五处「刷新」由金色主按钮改为 `ghost` 次按钮（次色文字），「生成报表 / 登录」保持金色主按钮。
  - 新增守卫 `Components_Should_Follow_Unified_Style`；`docs/screenshots` 全部重拍为浅色版（20 张，移除 `dark-*.png`），
      README 新增「界面预览」图集并引用年度口径总览。
  - **数据容器不再取金色**：能耗仪表板「综合能耗合计」头条卡原铺金色 soft（`#FFD67A`），在蓝/绿/橙/紫四张淡色
      指标卡上方显得突兀；改为中性蓝灰 secondary，并把全站同色的统计卡、区块卡、面板一并改为 secondary
      （平台概览「采集点总数」、排班「班组数量」、对比「综合能耗最高 / 对比明细」、报表汇总、采集点管理、
      排班日历、History API 测试、采集服务器等 18 处）。桌面端仪表板汇总卡、报表/对比汇总区块与指标卡同步。
      平台概览、排班时间管理、班组能耗报表 3 页的标题带改为 secondary，采集节点详情改为 tertiary（随所属的「点位数据管理」），
      抽屉导航图标主色与桌面端报表页同步；全站页面主色不再有 primary（仅登录入口保留），空状态图标块保留金色。
      新增守卫 `Data_Containers_Should_Not_Use_Primary_Accent`。
- 取消「生产环境默认初始口令拒启」闸门：`AuthDataInitializer` 不再在 Production 下因
  `Auth:BootstrapAdmin:Password` 为内置默认值（`Admin@123456`）而拒绝启动，允许生产直接以
  初始口令创建超管，由「首登强制改密」（`MustChangePassword=true`）兜底。口令复杂度校验保留不变。
- 采集服务器「新增/编辑」由抽屉弹窗改为整页表单视图：列表与表单在同一页面按状态切换，表单占满内容区
  （字段按大屏三列布局），切换后自动回到页顶；同时修复保存/删除成功提示被列表刷新立即清空的问题。

### 修复

- 采集服务器自动生成的编码含小写字符：`ResolveCode` 以地址拼接后缀时保留了 `http` 等小写字母，
  违反 `server_connections` 的全大写 CHECK 约束（`ck_server_connections_code_upper`），
  导致新增服务器时 INSERT 被数据库拒绝，页面只显示兜底文案「服务端处理失败，请查看服务端日志」。
  现生成结果统一转大写，手工录入的编码本就会转大写，不受影响。
- `20261117000000_AddShiftScheduleSnapshot` 的原生 SQL 缺少结尾分号，导致幂等脚本在 `DO` 块内生成 `) END IF;`，
  手工执行建表脚本会语法中断。补上分号后重新生成脚本。
