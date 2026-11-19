-- =============================================================================
-- CDYC 数据采集平台 · 基础授权字典（可选）
--
-- 内容：职能域 4 条、权限点 5 条、内置角色 4 条、角色权限关联 3 条。
-- 取值与 DAP.Core.Shared/Catalog/AuthorizationCatalog.cs 逐条一致；
-- 该文件是这些编码的唯一权威来源，改代码后本脚本需同步。
--
-- 何时需要执行：
--   - 推荐：空库建表后执行一次，使库在应用启动前就具备完整授权骨架，
--     便于先用 SQL 核对，也便于离线审计「库里到底有什么」。
--   - 可跳过：应用启动时的 AuthDataInitializer 会按 code 对账写入同一批数据，
--     不执行本脚本也能正常登录。二者不冲突，因为都按 code 幂等。
--
-- 幂等性：
--   - 编码表按唯一索引 (code) 做 ON CONFLICT DO NOTHING，重复执行不报错。
--   - 角色权限关联不写死 UUID，而是按 code 反查两张表的主键后再插入，
--     因此即使库中已有随机 UUID 的同名记录，也不会插错或触发外键错误。
--   - 全部 DO NOTHING 不覆盖既有值：管理员在「域与页面配置」里改过的
--     名称、排序、启停不会被本脚本回滚（对账职责归应用启动流程）。
--
-- 不在此脚本内的数据，以及原因见 README.md：
--   domain_pages、app_users、departments、business 表。
-- =============================================================================

BEGIN;

-- -----------------------------------------------------------------------------
-- 1. 职能域
--    平台管理是系统域（is_system = true），不参与授权矩阵，固定归超级管理员。
--    visible_when_empty 一律 false：空域不向普通用户展示，避免点进空白板块。
--    icon 刻意留空：MudBlazor 的 Icons.Material.* 是整段 SVG path，
--    短字符列容纳不下，菜单渲染改用代码常量。
-- -----------------------------------------------------------------------------
INSERT INTO business_domains
(id, code, name, icon, sort_order, is_system, is_enabled, visible_when_empty, created_at, updated_at)
VALUES ('91443ef3-e4e5-5bc7-9281-1423a3ceaefc', 'banknote-paper', '钞纸', NULL, 10, false, true, false, now(), now()),
       ('872522d1-8c02-5b7a-b33c-ad07c327643b', 'banknote-printing', '印钞', NULL, 20, false, true, false, now(),
        now()),
       ('7c38960a-2a91-5eda-aa21-18aca849000a', 'energy', '能源', NULL, 30, false, true, false, now(), now()),
       ('17daee05-644d-576c-83e2-741a391071c6', 'platform-admin', '平台管理', NULL, 900, true, true, false, now(),
        now())
ON CONFLICT (code) DO NOTHING;

-- -----------------------------------------------------------------------------
-- 2. 权限点
--    只为写操作建点（code 必须形如 action.*，库里有 CHECK 约束）。
-- -----------------------------------------------------------------------------
INSERT INTO permissions
    (id, code, name, group_name, sort_order, created_at, updated_at)
VALUES ('65e52660-78a8-5b2b-9fa7-518bacb5a6ce', 'action.shift.manage', '排班配置', '班组管理', 10, now(), now()),
       ('b37445fe-941b-5dc3-80ab-6e0dba252e8e', 'action.user.manage', '用户管理', '权限管理', 20, now(), now()),
       ('c3341d12-bfd8-5f91-b572-1fb0e3c1bef5', 'action.domain.manage', '域与页面管理', '权限管理', 30, now(), now()),
       ('9465487a-8ed9-51fe-85d4-eaad5e1fb1a9', 'action.department.manage', '部门与授权管理', '权限管理', 40, now(),
        now()),
       ('a7471eaf-7465-517c-aedb-60383cde29be', 'action.system.manage', '平台配置', '平台运维', 50, now(), now())
ON CONFLICT (code) DO NOTHING;

-- -----------------------------------------------------------------------------
-- 3. 内置角色
--    page_level：1 只看查看类页面，2 含配置类页面，99 超级管理员（短路，不参与判定）。
--    department-admin 出厂即停用（is_enabled = false）：它是「临时分担超管职责」的
--    备用通道，只在需要时打开开关，避免默认就多出一批能改人和改排班的账号。
-- -----------------------------------------------------------------------------
INSERT INTO roles
(id, code, name, page_level, is_builtin, is_enabled, sort_order, created_at, updated_at)
VALUES ('d1461b91-8579-5d19-9acc-a11755f12788', 'super-admin', '超级管理员', 99, true, true, 10, now(), now()),
       ('3579a343-4a9d-557d-a2eb-75688ee66fff', 'department-admin', '部门管理员', 2, true, false, 20, now(), now()),
       ('87aecbd6-0e41-5661-8ed8-2d4c6e83b0c0', 'technician', '技术人员', 2, true, true, 30, now(), now()),
       ('6df810c3-b1f1-5a62-804a-78411fbeabbe', 'viewer', '普通用户', 1, true, true, 40, now(), now())
ON CONFLICT (code) DO NOTHING;

-- -----------------------------------------------------------------------------
-- 4. 角色权限关联
--    按 code 反查主键，不依赖上面写死的 UUID。
--    超级管理员不挂权限点：它的放行来自 claims 里的 super_admin 短路，与权限点无关。
-- -----------------------------------------------------------------------------
INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM (VALUES ('department-admin', 'action.user.manage'),
             ('department-admin', 'action.shift.manage'),
             ('technician', 'action.shift.manage')) AS mapping (role_code, permission_code)
         JOIN roles r ON r.code = mapping.role_code
         JOIN permissions p ON p.code = mapping.permission_code
ON CONFLICT (role_id, permission_id) DO NOTHING;

COMMIT;

-- 执行结果应为：职能域 4、权限点 5、角色 4、角色权限 3（与库中既有数据合并后）。
-- 详细自检见 03_verify.sql。
