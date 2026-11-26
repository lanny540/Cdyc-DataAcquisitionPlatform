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
