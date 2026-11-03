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
├── docs/AUTH_DESIGN.md             # 认证与权限设计基线
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

## 登录与初始账号

平台所有页面都需要登录，未登录访问任意地址会被拦到 `/login`，登录后回到原地址。会话是**服务端 Cookie**（`HttpOnly` + `SameSite=Lax`，滑动 8 小时 / 绝对 12 小时），浏览器端不保存令牌。

- 首次启动时，若库中没有任何启用状态的超级管理员，会按 `appsettings.json` 的 `Auth:BootstrapAdmin` 创建一个（默认 `admin / Admin@123456`），并置为**首次登录强制改密**。
- 库中已有超级管理员时该配置被忽略，改密后的密码不会被配置覆盖。
- **生产环境禁止**用内置默认密码创建超管：`ASPNETCORE_ENVIRONMENT=Production` 且密码仍是默认值时会拒绝启动，请用环境变量覆盖：
  ```bash
  Auth__BootstrapAdmin__Password='换成强口令'
  ```
- 连续输错 5 次锁定 15 分钟（`Auth:Lockout`）。超管全局唯一，启动自检发现数量不为 1 会拒绝启动。
- 设计基线见 [`docs/AUTH_DESIGN.md`](docs/AUTH_DESIGN.md)（三层收窄：职能域 → 页面等级 → 权限点）。**P0 / P1 / P2 / P3 / P4 均已落地**：登录认证、域授权矩阵（`/department-grants`）、页面等级与域配置（`/domain-config`）、配置类写操作审计（`audit_logs`）、领域服务授权装饰器、车间终端 IP 白名单免登录（`Auth:Board`，默认关闭）、用户与角色（`/user-management`）、系统审计（`/system-audit`）、部门管理员（`Auth:DepartmentAdmin`，默认关闭）、LDAP / AD 对接与目录同步（`Auth:Ldap`）、OIDC 统一登录（`Auth:Oidc`）。
- 账号与角色：账号必须对应员工个人，不得为班组设共享账号。角色变更与启停在**该用户下次登录时生效**（授权只认 Cookie 里的 claims，会话内不热切换）。超管全局唯一，更换只能走「移交超管」，移交后当前会话自动登出。
- 部门管理员（`Auth:DepartmentAdmin:Enabled=true`，默认关闭）只能管本部门账号、只能授予「普通用户 / 技术人员」，跨部门调动与移交超管只对超管开放；越权尝试同样写入审计（`result=failed`）。
- 审计只记录配置类写操作（排班、采集配置、域与页面、部门授权、账号与角色），不记查询与登录；默认保留 180 天，由后台任务分批清理（`Auth:Audit`）。
- 车间公用电脑按 IP 白名单免登录：`Auth:Board:Enabled=true` 且客户端 IP 命中 `IpWhitelist`（单机地址）时自动签发只读会话，只能查看数据指标下的查看类页面；写操作与平台管理一律拒绝。

## 域账号与统一登录（可选）

两条外部身份通道都默认关闭，配置不完整会**拒绝启动**（一次列出全部错误），不会带病上线。

**LDAP / AD**（`Auth:Ldap`）：

```jsonc
{
  "Auth": {
    "Ldap": {
      "Enabled": true,
      "Host": "dc01.cdyc.local",
      "Port": 636,
      "TransportSecurity": "Ssl",        // None 只允许回环地址，生产必须 Ssl 或 StartTls
      "BindDn": "cn=svc-dap,ou=service,dc=cdyc,dc=local",
      "BindPassword": "换成实际口令",
      "BaseDn": "dc=cdyc,dc=local",
      "UserFilter": "(sAMAccountName={0})",
      "LoginAttribute": "sAMAccountName",
      "UniqueIdAttribute": "objectGUID",  // 必须是不会变的属性
      "DisplayNameAttribute": "displayName",
      "DepartmentAttribute": "department", // 值为 departments.external_code
      "SyncFilter": "(objectClass=user)",
      "SyncIntervalMinutes": 60           // 0 = 只在手工触发时同步
    }
  }
}
```

- 平台**不保存域账号密码**：登录时用用户输入的密码回目录校验一次，改密页对该类账号提示「密码由目录统一管理」。
- 同步只更新姓名、邮箱、部门、启停，**不覆盖手工分配的角色**；目录中消失的账号只停用不删除；目录返回 0 条时整轮作废。
- 部门按 `departments.external_code` 匹配，匹配不到**不自动建部门**，进入「用户与角色」页底部的待处理表，由超管补登记后下次同步自动归属。

**OIDC**（`Auth:Oidc`，授权码 + PKCE）：

```jsonc
{
  "Auth": {
    "Oidc": {
      "Enabled": true,
      "Providers": [
        {
          "Code": "corp",                          // 回调路径的一部分：/api/auth/oidc/corp/callback
          "DisplayName": "集团统一登录",
          "Authority": "https://login.cdyc.com",
          "ClientId": "dap-web",
          "ClientSecret": "换成实际口令",
          "NameClaim": "name",
          "EmailClaim": "email",
          "UserNameClaim": "preferred_username",
          "DepartmentClaims": ["department"]
        }
      ]
    }
  }
}
```

- 唯一键只用 `sub`，不用邮箱或 `preferred_username`（会变、会被回收）。
- **新增第二家 IdP 只需加一段 `Providers`**，claim 名、部门前缀、默认角色全部可配，代码零改动。
- 多值部门 claim（如 `groups`）配 `DepartmentPrefix` 后按前缀挑，避免把 `staff` 当成部门。

## 生产部署要点

会话要能稳定工作，下面四项必须确认，缺一项的现象都像「系统故障」：

| 项 | 默认值 | 不配置的后果 |
|---|---|---|
| `Auth:DataProtection:KeyPath` | 内容根目录下 `data-protection-keys` | 重新部署/换容器后密钥丢失，**所有人同时掉线**；多实例各有一把钥匙，用户在两台之间反复被踢 |
| HTTPS（Kestrel 证书或反向代理终止 TLS） | 非 Development 强制 Secure Cookie | 纯 HTTP 部署时浏览器不回传会话 Cookie，表现为「登录成功后又回到登录页」 |
| `Auth:ForwardedHeaders`（有反向代理时） | 关闭 | 客户端 IP 全是代理地址：车间 IP 白名单失效、限流全站共用一个桶 |
| `Auth:Audit:RetentionDays` | 180 | 不配也有默认值；设为 0 表示不清理 |

密钥目录必须挂持久卷并**排除在版本库之外**（`.gitignore` 已排除 `**/data-protection-keys/`）。它等价于 Cookie 的签名私钥，拿到就能伪造会话。

同一把密钥还用于**采集服务器凭据的落库加密**（Historian 的 `ClientSecret`、设备密码等，密文带 `dp:v1:` 前缀）。因此它必须与数据库一起备份：只恢复数据库而丢掉密钥目录，等于所有采集服务器密码都被清空，采集会以「未配置凭据」失败。历史明文数据不需要迁移——没有前缀的值按明文使用，下一次在界面上保存时自动升级为密文。

```jsonc
{
  "Auth": {
    "DataProtection": { "KeyPath": "/var/lib/dap/keys" },
    "Cookie": { "RequireHttps": true },      // 仅物理隔离内网且无法上 TLS 时才设 false
    "ForwardedHeaders": {                     // 有反向代理时才启用，且必须列地址
      "Enabled": true,
      "KnownProxies": ["10.0.0.5"],
      "ForwardLimit": 1
    }
  }
}
```

登录与改密按客户端 IP 限流 20 次/分钟（`PlatformRateLimiting`），超限返回 429；单账号仍受「连续 5 次失败锁定 15 分钟」约束。变更类请求另有同源校验（`Origin`/`Referer`），跨站提交直接 403。

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
