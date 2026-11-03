# CDYC 数据采集平台 (Data Acquisition Platform)

## 项目简介

CDYC 数据采集平台是一个基于 **.NET 10** 构建的数据采集与监控系统。该项目支持通过 Modbus、MQTT、OPC DA 等多种工业协议进行数据采集，并提供
Web（Blazor）与跨平台桌面（Avalonia）双终端展示方案。

系统离线部署能力，并且在架构上充分考虑了分布式环境下的高可用性与负载均衡场景。

## 架构设计

项目严格遵循领域驱动设计（DDD）和整洁架构规范，以保证高内聚和低耦合。

- **合并部署单元**：将 WebApi 接口层与 Blazor Web 宿主合并部署，极大简化了运维复杂度，避免跨域资源共享（CORS）问题。
- **混合持久化方案 (CQRS 思想)**：
    - **写入与复杂事务**：使用 **Entity Framework Core**（搭配 PostgreSQL）处理强一致性的复杂写入操作。
    - **只读与高性能查询**：使用 **Dapper** 编写原生 SQL，满足报表统计与高并发查询的性能要求。
- **离线与边缘计算支持**：Web 端静态资源本地化（无需外部 CDN），桌面端内置 SQLite 进行离线数据同步与配置存储。

## 项目结构

```text
├── DAP.Core.Domain                 # 核心层：领域实体、业务接口规范
├── DAP.Core.Shared                 # 核心层：共享数据契约 (Contracts / DTOs)
├── DAP.Infrastructure              # 基础设施：PGSQL 数据库访问（EF Core 写入 + Dapper 只读）与 EF Core 迁移
├── DAP.Proxies.OpcDaProxy          # 代理服务：OPC DA 采集代理 Worker
├── DAP.Presentation.BlazorWeb      # 展示层：Blazor Web 服务端与 API 宿主 (Endpoints)
├── DAP.Presentation.BlazorWeb.Client # 展示层：Blazor WebAssembly 客户端
├── DAP.Presentation.AvaloniaApp    # 展示层：基于 Avalonia 12 的跨平台桌面应用
│   └── Themes/                     # 统一品牌色板（DapPalette.axaml）与 Semi 主题覆盖
├── DAP.Tests.UnitTests             # 测试：领域与基础设施单元测试
├── docs/DESIGN_SYSTEM.md           # 设计规范（强制）
└── AGENTS.md                       # AI 协作约束
```

## 技术栈

- **基础框架**: .NET 10.0
- **Web UI**: Blazor WebAssembly / Server，全部页面统一采用 [MudBlazor](https://mudblazor.com/) 9.12 组件库（Material Design 设计体系），抽屉与对话框均使用官方 `MudDialog`（含内置遮罩、ESC 关闭与焦点管理），间距与断点直接使用 MudBlazor 内置的 `pa-*` / `mt-*` 工具类；Tailwind仅保留少量布局辅助（`tw-p-4`、`tw-break-all`），不启用 preflight 以避免覆盖 MudBlazor 基础样式。
- **桌面 UI**: Avalonia
  12.1，采用 [CommunityToolkit.Mvvm](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/)
  ，结合 [Semi.Avalonia](https://semi.design/) 与 [Irihi.Ursa](https://irihi.tech/) 主题，提供精致的微观交互体验。
- **数据库**: PostgreSQL 10.x (Npgsql), SQLite (桌面端本地存储)
- **ORM**: EF Core 10.0, Dapper 2.1
- **架构模式**: Clean Architecture, Minimal APIs

## 设计规范

全项目 UI 采用 **Material Design 3** 设计体系，Web 端与桌面端共用同一套品牌色板。

| 文档 | 用途 |
| --- | --- |
| [`docs/DESIGN_SYSTEM.md`](docs/DESIGN_SYSTEM.md) | **强制设计规范**：色板定义、语义映射、组件规范、改动禁令、新增页面检查清单 |
| [`AGENTS.md`](AGENTS.md) | AI 协作约束，改动 UI 前必读 |

色板权威来源：

- Web：`DAP.Presentation.BlazorWeb/wwwroot/app.css` 的 `:root` 变量
- 桌面：`DAP.Presentation.AvaloniaApp/Themes/DapPalette.axaml`

**核心约定**：项目使用 Primary / Secondary / Tertiary / Success / Warning / Error / Info 七色封闭体系，每色分Base / Soft / Tint 三级深度。Web 端通过 `.platform-accent-*` 修饰类切换配色（加类即可，不写新CSS 规则），桌面端通过 `{DynamicResource Dap*Brush}` 取色。页面中不得硬编码十六进制色值。

## 本地运行指南

1. **环境准备**
    - 安装 [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
    - 安装 Node.js 22+ 与 npm（用于 Web 端 Tailwind CSS 构建）。
    - 准备 PostgreSQL 与 Redis 服务环境。
2. **编译解决方案**
   ```bash
   dotnet build DAP.slnx
   ```
3. **启动 Web 平台服务**
   ```bash
   # 将启动 Blazor Web 与内置 API
   dotnet run --project DAP.Presentation.BlazorWeb/DAP.Presentation.BlazorWeb.csproj
   ```
4. **启动 Avalonia 桌面客户端**
   ```bash
   dotnet run --project DAP.Presentation.AvaloniaApp/DAP.Presentation.AvaloniaApp.csproj
   ```

## 外部服务器配置与数据库迁移

- 外部 API 与数采服务器统一保存于 PostgreSQL 的 `server_connections` 表；地址、认证信息和 Mock 开关保存在服务器配置 JSON 中，不再依赖 `appsettings.json` 中的固定服务器地址。
- 保存后台数据定义时，平台会按“采集方式 + 连接地址”自动登记缺失服务器，并将节点绑定到对应服务器；已登记服务器的连接配置作为共享主数据，不会被任一节点覆盖。
- `20260917080800_AddServerConnections` 迁移会将已有后台数据定义中的服务器地址去重回填到 `server_connections`；后续关联迁移会回填节点的服务器外键。
- 更新数据库结构时执行：
  ```bash
  dotnet ef database update \
    --project DAP.Infrastructure/DAP.Infrastructure.csproj \
    --startup-project DAP.Presentation.BlazorWeb/DAP.Presentation.BlazorWeb.csproj
  ```

## 提交与更新日志约定

- 所有 Git 提交说明统一使用中文。
- 每次准备提交代码前，必须同步更新仓库根目录下的 `CHANGELOG.md`。
- 变更记录优先追加到 `未发布` 小节，并按 `新增 / 优化 / 修复 / 文档` 分类整理。
- 提交内容与更新日志应保持一致，避免出现“代码已改动但日志未记录”或“日志已记录但代码未提交”的情况。
