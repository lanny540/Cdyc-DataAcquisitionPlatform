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

## 2026-11-26

### 文档

- 新增提交规则并写入 `AGENTS.md`「提交前更新文档（强制）」：每次提交必须以当日日期为
  `CHANGELOG.md` 小节标题，内容限于本次变更，描述优先说明「改了什么」与「影响哪里」；
  `## [未发布]` 累积模式停用。
- 移除 `docs/AUTH_DESIGN.md`（1140 行）与 `scripts/core-coverage.sh`，同步清理 82 处指向：
  `README.md` 两处超链接、`AGENTS.md` 一处设计基线说明、58 个源文件注释里的章节号；
  授权模型说明改由 `AGENTS.md` 与代码承担（`PageAccessResolver`、`AuthorizationCatalog`）。
- 部署脚本收敛到 `docs/sql/`：移除重复的 `scripts/sql/` 与 `docs/sql/README.md`，
  旧 `02_seed_authorization.sql` 更名为 `02_seed_reference.sql`，`01_schema.sql` 按当前迁移
  重新生成（12 个迁移 / 24 张业务表），四份脚本去掉全部说明性注释
  （`03_verify.sql` 的 `\echo` 分段标题除外）。
- 新增 `docs/sql/04_production_seed.sql`：预置 GE Historian 采集服务器（`10.9.3.54:8080`）
  与 10 个 Historian Tag 后台数据定义，服务器按（类型, 地址）、定义按 code 幂等。

### 优化

- `docs/sql/04_production_seed.sql` 加入 `.gitignore`：含 GE Historian 明文口令，
  只在本机与部署介质流转，不入库；该文件从未进入提交历史。
- 清理 3 张零引用截图（`managed-data-definitions`、`managed-data-editor-sheet`、
  `managed-data-definition-details`），截图目录由 23 张 / 13M 降为 20 张 / 11M；
  `.gitignore` 截图段注释修正为「已跟踪文件不受忽略规则影响」。
- 班组历史指标 `/energy-history` 与能耗仪表板 `/energy-dashboard`：去掉「四项指标趋势」
  区块卡，改为一行无底色工具条加右侧图标按钮，一键在「单独成行 / 每行两列」间切换。
  按钮只显示当前排布的图标（两条横杠＝单行、两根竖杠＝两列），文字说明放进悬停提示与
  `aria-label`；两页默认均为单行。
- 趋势图横轴标签上限随排布收紧：单行保留 12 个、两列保留 4 个。横轴是 `yyyy-MM-dd`
  十字符日期，半宽卡片放 10 个会互相压字。实测最小标签间距——单行 34px（1440 视口）/
  18px（1280 视口），两列 47px / 25px，两种排布、两个视口均零重叠。
- `MudChart` 增加 `@key="_trendSingleColumn"`：该组件只在实例创建时量一次容器宽度，
  不换实例时从两列切回单行，SVG 仍是半宽，标签会重新压字。
- `docs/screenshots/energy-history.png`、`dashboard.png` 按新版式重拍。
- 桌面端（Avalonia）能耗仪表板与历史指标两页同步同一套布局设置：去掉仪表板上的
  「四项指标趋势」tint 区块卡，改为同样的无底色工具条加纯图标切换按钮
  （`Button.icon-ghost` 新样式，ToolTip 承载说明）；趋势卡网格列数绑定
  `TrendColumns`（单行 1 列 / 两列 2 列）。`LineTrendChart` 新增 `MaxLabels`
  标签上限属性并与 Web 端口径同步（单行 12 个 / 两列 4 个），切换后立即重绘；
  共享状态放在 `EnergyPageViewModelBase`，两端行为一致。
- 应用版本号同步为最新提交的代码日期：根目录 `Directory.Build.props`（全解决方案唯一
  版本源）的 `Version` / `AssemblyVersion` / `FileVersion` 三属性由 2026.09.17 更新为
  `2026.11.26`。Web 顶栏右侧版本徽章自动显示「Web 管理端 v2026.11.26」，桌面侧栏底部
  自动显示「v2026.11.26」——显示代码读程序集版本，无需改动。
- 新增 `AppVersionGuardTests` 守卫测试 3 例：`Directory.Build.props` 三个版本属性
  必须一致且为日期制、csproj 不得重复声明 `<Version>`（会覆盖集中定义导致版本不一致）、
  两端版本显示代码必须读程序集版本不得硬编码。
- `AGENTS.md`「提交前更新文档（强制）」追加应用版本号同步规则：每次提交把
  `Directory.Build.props` 三属性更新为提交日期。
- 登录会话时长延长并取消绝对过期：`Auth:Cookie:SlidingHours` 由 8 调整为 10
  （两个 `appsettings` 与 `CookieSessionOptions` 默认值同步），`AbsoluteHours` 配置项与
  会话签发时间的绝对过期判定整体移除。会话改为纯滑动续期——活跃用户每次请求过半自动续期、
  无需再次登录，长期不操作超过 10 小时才要求重登。安全兜底不变：HttpOnly Cookie、
  安全戳比对（停用账号、管理员重置密码、改密立即作废旧会话）全部保留；
  车间只读终端仍为固定 12 小时会话（刻意不滑动）。能源审计服务同步移除签发时间窗口判定，
  前置校验只剩安全戳、启用状态与权限；对应测试反转为「久签会话仍可操作」。

### 修复

- 登出后再登录不出现大屏选择页：`LogoutAsync` 跳 `/login` 时不带 returnUrl，
  无 returnUrl 的登录会回落到落地页解析，其超管分支仍返回旧值 `/platform-overview`，
  跳过根路由的大屏导航枢纽。超管默认落地页改为 `/`（ScreensHub），每次登录先选入口；
  从业务页被闸门拦回登录页再登录时，仍由 returnUrl 优先回跳原页面。
  同步更新 `PlatformAuthServiceTests` 断言与 `auth.spec.mjs` 落地页断言。
