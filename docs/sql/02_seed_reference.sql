BEGIN;

INSERT INTO business_domains (
    id, code, name, icon, sort_order, is_system, is_enabled,
    visible_when_empty, created_at, updated_at)
VALUES
    (gen_random_uuid(), 'banknote-paper',    '钞纸',     NULL,  10, false, true, false, now(), now()),
    (gen_random_uuid(), 'banknote-printing', '印钞',     NULL,  20, false, true, false, now(), now()),
    (gen_random_uuid(), 'energy',            '能源',     NULL,  30, false, true, false, now(), now()),
    (gen_random_uuid(), 'platform-admin',    '平台管理', NULL, 900, true,  true, false, now(), now())
ON CONFLICT (code) DO NOTHING;

INSERT INTO permissions (id, code, name, group_name, sort_order, created_at, updated_at)
VALUES
    (gen_random_uuid(), 'action.shift.manage',      '排班配置',       '班组管理', 10, now(), now()),
    (gen_random_uuid(), 'action.user.manage',       '用户管理',       '权限管理', 20, now(), now()),
    (gen_random_uuid(), 'action.domain.manage',     '域与页面管理',   '权限管理', 30, now(), now()),
    (gen_random_uuid(), 'action.department.manage', '部门与授权管理', '权限管理', 40, now(), now()),
    (gen_random_uuid(), 'action.system.manage',     '平台配置',       '平台运维', 50, now(), now())
ON CONFLICT (code) DO NOTHING;

INSERT INTO roles (
    id, code, name, page_level, is_builtin, is_enabled,
    sort_order, created_at, updated_at)
VALUES
    (gen_random_uuid(), 'super-admin',      '超级管理员',  99, true, true,  10, now(), now()),
    (gen_random_uuid(), 'department-admin', '部门管理员',   2, true, false, 20, now(), now()),
    (gen_random_uuid(), 'technician',       '技术人员',     2, true, true,  30, now(), now()),
    (gen_random_uuid(), 'viewer',           '普通用户',     1, true, true,  40, now(), now())
ON CONFLICT (code) DO NOTHING;

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

INSERT INTO energy_usage_types (
    id, code, name, sort_order, is_system, is_enabled, created_at, updated_at)
VALUES
    (gen_random_uuid(), 'PROD',     '生产',     10, true, true, now(), now()),
    (gen_random_uuid(), 'AUX',      '辅助生产', 20, true, true, now(), now()),
    (gen_random_uuid(), 'OFFICE',   '管理',     30, true, true, now(), now()),
    (gen_random_uuid(), 'SECURITY', '安防',     40, true, true, now(), now()),
    (gen_random_uuid(), 'PUBLIC',   '公共设施', 50, true, true, now(), now())
ON CONFLICT (code) DO NOTHING;

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
