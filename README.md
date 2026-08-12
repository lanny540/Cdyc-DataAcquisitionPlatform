# CDYC 数据采集平台 (Data Acquisition Platform)

## 项目简介

CDYC 数据采集平台是一个基于 **.NET 10** 构建的数据采集与监控系统。该项目支持通过 Modbus、MQTT、OPC DA 等多种工业协议进行数据采集，并提供 Web（Blazor）与跨平台桌面（Avalonia）双终端展示方案。

系统离线部署能力，并且在架构上充分考虑了分布式环境下的高可用性与负载均衡场景。

## 架构设计

项目严格遵循领域驱动设计（DDD）和整洁架构规范，以保证高内聚和低耦合。

- **合并部署单元**：将 WebApi 接口层与 Blazor Web 宿主合并部署，极大简化了运维复杂度，避免跨域资源共享（CORS）问题。
- **混合持久化方案 (CQRS 思想)**：
  - **写入与复杂事务**：使用 **Entity Framework Core**（搭配 PostgreSQL）处理强一致性的复杂写入操作。
  - **只读与高性能查询**：使用 **Dapper** 编写原生 SQL，满足报表统计与高并发查询的性能要求。
- **高可用与分布式**：通过 Redis 实现分布式缓存与会话状态保持，确保在多节点负载均衡（HA）下，单一服务器下线不会导致用户登录状态丢失。
- **离线与边缘计算支持**：Web 端静态资源本地化（无需外部 CDN），桌面端内置 SQLite 进行离线数据同步与配置存储。

## 项目结构

```text
├── DAP.Core.Domain                 # 核心层：领域实体、业务接口规范
├── DAP.Core.Shared                 # 核心层：共享数据契约 (Contracts / DTOs)
├── DAP.Infrastructure.DataAccess   # 基础设施：基于 EF Core & Dapper 的 PGSQL 数据库访问
├── DAP.Infrastructure.Redis        # 基础设施：Redis 分布式缓存实现
├── DAP.Infrastructure.TimeSeries   # 基础设施：时序数据库集成 (用于海量采集数据存储)
├── DAP.Collectors.CollectorBase    # 采集引擎：核心采集基类抽象
├── DAP.Collectors.Modbus           # 采集引擎：Modbus 协议支持
├── DAP.Collectors.Mqtt             # 采集引擎：MQTT 协议支持
├── DAP.Proxies.OpcDaProxy          # 代理服务：OPC DA 采集代理 Worker
├── DAP.Presentation.BlazorWeb      # 展示层：Blazor Web 服务端与 API 宿主 (Endpoints)
├── DAP.Presentation.BlazorWeb.Client # 展示层：Blazor WebAssembly 客户端
├── DAP.Presentation.AvaloniaApp    # 展示层：基于 Avalonia 12 的跨平台桌面应用
└── DAP.Tests.UnitTests             # 测试：领域与基础设施单元测试
```

## 技术栈

- **基础框架**: .NET 10.0
- **Web UI**: Blazor WebAssembly / Server, 采用 [MudBlazor](https://mudblazor.com/) 9.x 组件库与 Tailwind CSS 构建现代紧凑布局。
- **桌面 UI**: Avalonia 12.1，采用 [CommunityToolkit.Mvvm](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/)，结合 [Semi.Avalonia](https://semi.design/) 与 [Irihi.Ursa](https://irihi.tech/) 主题，提供精致的微观交互体验。
- **数据库**: PostgreSQL 10.x (Npgsql), SQLite (桌面端本地存储)
- **ORM**: EF Core 10.0, Dapper 2.1
- **架构模式**: Clean Architecture, Minimal APIs

## 本地运行指南

1. **环境准备**
   - 安装 [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
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

