-- ============================================================================
-- 02_seed_reference.sql  CDYC 数据采集平台 —— 基础字典数据
-- ----------------------------------------------------------------------------
-- 前置条件：01_schema.sql 已执行成功
-- 幂等性　：全部按业务编码判重（ON CONFLICT DO NOTHING），可重复执行
-- 数据来源：DAP.Core.Shared/Catalog/AuthorizationCatalog.cs（职能域/角色/权限点）
--           DAP.Core.Shared/Catalog/EnergyDictionaryCatalog.cs（能源介质/用途/系统）
-- 执行方式：psql -h <主机> -p 5432 -U <账号> -d <库名> -v ON_ERROR_STOP=1 \
--                -f 02_seed_reference.sql
--
-- 不包含以下数据，原因如下：
--   1) app_users（初始超管）——由应用启动时按配置 Auth:BootstrapAdmin 创建，
--      口令使用 PBKDF2-SHA256 加盐散列，手工插入的散列若格式不符将无法登录。
--   2) domain_pages（页面归属）——由应用启动时按 NavigationCatalog 对账写入，
--      按 page_key 覆盖为代码默认值；手工插入只会被对账覆盖。
--   3) departments（部门）——按现场组织架构建立，脚本末尾给出模板。
--   4) energy_org_units（能源组织层级）——由应用启动时创建园区根节点。
--   5) screen_hub_cards（大屏导航卡）——由应用启动时（ScreenHubCardInitializer）
--      在空表时补入三张默认卡，只补不覆盖；手工插入只会在空表时生效。
-- ============================================================================

BEGIN;

-- ----------------------------------------------------------------------------
-- 1. 职能域：顶栏四个模块（平台管理为系统域，不参与授权矩阵）
-- ----------------------------------------------------------------------------
INSERT INTO business_domains (
    id, code, name, icon, sort_order, is_system, is_enabled,
    visible_when_empty, created_at, updated_at)
VALUES
    (gen_random_uuid(), 'banknote-paper',    '钞纸',     NULL,  10, false, true, true, now(), now()),
    (gen_random_uuid(), 'banknote-printing', '印钞',     NULL,  20, false, true, true, now(), now()),
    (gen_random_uuid(), 'energy',            '能源',     NULL,  30, false, true, true, now(), now()),
    (gen_random_uuid(), 'platform-admin',    '平台管理', NULL, 900, true,  true, true, now(), now())
ON CONFLICT (code) DO NOTHING;

-- ----------------------------------------------------------------------------
-- 2. 权限点：只为写操作建点，读操作不建
-- ----------------------------------------------------------------------------
INSERT INTO permissions (id, code, name, group_name, sort_order, created_at, updated_at)
VALUES
    (gen_random_uuid(), 'action.shift.manage',      '排班配置',       '班组管理', 10, now(), now()),
    (gen_random_uuid(), 'action.user.manage',       '用户管理',       '权限管理', 20, now(), now()),
    (gen_random_uuid(), 'action.domain.manage',     '域与页面管理',   '权限管理', 30, now(), now()),
    (gen_random_uuid(), 'action.department.manage', '部门与授权管理', '权限管理', 40, now(), now()),
    (gen_random_uuid(), 'action.system.manage',     '平台配置',       '平台运维', 50, now(), now())
ON CONFLICT (code) DO NOTHING;

-- ----------------------------------------------------------------------------
-- 3. 内置角色
--    super-admin 页面等级 99 = 短路，不参与域/等级/权限点判定
--    department-admin 默认停用，需要时在「用户与角色」里启用
-- ----------------------------------------------------------------------------
INSERT INTO roles (
    id, code, name, page_level, is_builtin, is_enabled,
    sort_order, created_at, updated_at)
VALUES
    (gen_random_uuid(), 'super-admin',      '超级管理员',  99, true, true,  10, now(), now()),
    (gen_random_uuid(), 'department-admin', '部门管理员',   2, true, false, 20, now(), now()),
    (gen_random_uuid(), 'technician',       '技术人员',     2, true, true,  30, now(), now()),
    (gen_random_uuid(), 'viewer',           '普通用户',     1, true, true,  40, now(), now())
ON CONFLICT (code) DO NOTHING;

-- ----------------------------------------------------------------------------
-- 4. 角色与权限点关联
-- ----------------------------------------------------------------------------
INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM roles r
JOIN permissions p
  ON p.code IN ('action.user.manage', 'action.shift.manage')
WHERE r.code = 'department-admin'
ON CONFLICT DO NOTHING;

INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM roles r
JOIN permissions p
  ON p.code = 'action.shift.manage'
WHERE r.code = 'technician'
ON CONFLICT DO NOTHING;

-- ----------------------------------------------------------------------------
-- 5. 能源介质
--    coal_factor 取 GB/T 2589 当量值。蒸汽、压缩空气、空调制冷量留空：
--    前者取决于焓值，后两者无固定系数，给默认值会让报表「看起来精确」但是错的。
-- ----------------------------------------------------------------------------
INSERT INTO energy_media (
    id, code, name, unit, is_primary_energy, coal_factor,
    sort_order, is_enabled, created_at, updated_at)
VALUES
    (gen_random_uuid(), 'ELEC',    '电',         'kWh', true,  0.1229, 10, true, now(), now()),
    (gen_random_uuid(), 'WATER',   '水',         't',   true,  0.2571, 20, true, now(), now()),
    (gen_random_uuid(), 'STEAM',   '蒸汽',       't',   true,  NULL,   30, true, now(), now()),
    (gen_random_uuid(), 'COOLING', '空调制冷量', 'GJ',  false, NULL,   40, true, now(), now()),
    (gen_random_uuid(), 'AIR',     '压缩空气',   'm³',  false, NULL,   50, true, now(), now())
ON CONFLICT (code) DO NOTHING;

-- ----------------------------------------------------------------------------
-- 6. 用能用途（五类对应五个责任主体：生产/辅助生产/管理/安防/公共设施）
-- ----------------------------------------------------------------------------
INSERT INTO energy_usage_types (
    id, code, name, sort_order, is_system, is_enabled, created_at, updated_at)
VALUES
    (gen_random_uuid(), 'PROD',     '生产',     10, true, true, now(), now()),
    (gen_random_uuid(), 'AUX',      '辅助生产', 20, true, true, now(), now()),
    (gen_random_uuid(), 'OFFICE',   '管理',     30, true, true, now(), now()),
    (gen_random_uuid(), 'SECURITY', '安防',     40, true, true, now(), now()),
    (gen_random_uuid(), 'PUBLIC',   '公共设施', 50, true, true, now(), now())
ON CONFLICT (code) DO NOTHING;

-- ----------------------------------------------------------------------------
-- 7. 设备系统（照明归系统不归用途：用途跟区域走，系统跟设备类型走）
-- ----------------------------------------------------------------------------
INSERT INTO energy_systems (
    id, code, name, sort_order, is_system, is_enabled, created_at, updated_at)
VALUES
    (gen_random_uuid(), 'PROCESS',  '工艺设备',  10, true, true, now(), now()),
    (gen_random_uuid(), 'LIGHTING', '照明',      20, true, true, now(), now()),
    (gen_random_uuid(), 'HVAC',     '空调通风',  30, true, true, now(), now()),
    (gen_random_uuid(), 'POWER',    '动力站房',  40, true, true, now(), now()),
    (gen_random_uuid(), 'IT',       'IT 与弱电', 50, true, true, now(), now())
ON CONFLICT (code) DO NOTHING;

COMMIT;

-- ============================================================================
-- 附：部门建立模板（按现场实际填写，非必需，也可在「部门与授权」页面维护）
-- ----------------------------------------------------------------------------
-- INSERT INTO departments (id, code, name, external_code, is_enabled, created_at, updated_at)
-- VALUES (gen_random_uuid(), 'DEPT-001', '生产一部', NULL, true, now(), now())
-- ON CONFLICT (code) DO NOTHING;
-- ============================================================================
