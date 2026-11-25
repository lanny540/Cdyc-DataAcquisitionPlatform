-- ============================================================================
-- 03_verify.sql  CDYC 数据采集平台 —— 部署后核对
-- ----------------------------------------------------------------------------
-- 只读脚本，不改数据。逐段执行，或整体执行看多组结果。
-- 执行方式：psql -h <主机> -p 5432 -U <账号> -d <库名> -f 03_verify.sql
-- ============================================================================

\echo '=== 1. 迁移版本（期望 11 行，末行为 20261119034448_SoftDeleteManagedDataDefinitions）==='
SELECT "MigrationId", "ProductVersion"
FROM "__EFMigrationsHistory"
ORDER BY "MigrationId";

\echo '=== 2. 业务表完整性（期望 23 张全部为 t）==='
-- 若部署在非 public schema，请把下面的 'public.' 换成实际 schema 名
SELECT tbl AS "表名", (to_regclass('public.' || tbl) IS NOT NULL) AS "已存在"
FROM unnest(ARRAY[
    'collection_points',
    'collection_data_records',
    'managed_data_definitions',
    'server_connections',
    'audit_logs',
    'business_domains',
    'departments',
    'permissions',
    'roles',
    'domain_pages',
    'app_users',
    'department_domain_grants',
    'role_permissions',
    'external_identities',
    'user_roles',
    'directory_sync_issues',
    'energy_media',
    'energy_org_units',
    'energy_systems',
    'energy_usage_types',
    'energy_meters',
    'energy_readings',
    'shift_schedule_snapshot'
]) AS x(tbl)
ORDER BY tbl;

\echo '=== 3. 基础字典行数（期望：域4 权限点5 角色4 角色权限3 介质5 用途5 系统5）==='
SELECT 'business_domains' AS "表", count(*) AS "行数" FROM business_domains
UNION ALL SELECT 'permissions',   count(*) FROM permissions
UNION ALL SELECT 'roles',         count(*) FROM roles
UNION ALL SELECT 'role_permissions', count(*) FROM role_permissions
UNION ALL SELECT 'energy_media',       count(*) FROM energy_media
UNION ALL SELECT 'energy_usage_types', count(*) FROM energy_usage_types
UNION ALL SELECT 'energy_systems',     count(*) FROM energy_systems
ORDER BY 1;

\echo '=== 4. 角色与权限点对应关系 ==='
SELECT r.code AS "角色", r.page_level AS "页面等级", r.is_enabled AS "启用",
       coalesce(string_agg(p.code, ', ' ORDER BY p.code), '(无)') AS "权限点"
FROM roles r
LEFT JOIN role_permissions rp ON rp.role_id = r.id
LEFT JOIN permissions p ON p.id = rp.permission_id
GROUP BY r.code, r.page_level, r.is_enabled
ORDER BY r.code;

\echo '=== 5. 超级管理员检查（期望 1 个；为 0 时应用首次启动会自动创建）==='
SELECT u.user_name AS "账号", u.display_name AS "姓名",
       u.is_enabled AS "启用", u.must_change_password AS "首登改密", u.created_at AS "创建时间"
FROM app_users u
JOIN user_roles ur ON ur.user_id = u.id
JOIN roles r ON r.id = ur.role_id
WHERE r.code = 'super-admin';

\echo '=== 6. 页面归属（首次启动后才有数据，期望 17 行、is_orphaned 全为 f）==='
SELECT count(*) AS "页面数",
       count(*) FILTER (WHERE is_orphaned) AS "已下线",
       count(*) FILTER (WHERE access_level = 2) AS "配置类"
FROM domain_pages;

\echo '=== 7. 外键悬空检查（四组期望均为 0）==='
SELECT 'collection_data_records 指向不存在的采集点' AS "检查项", count(*) AS "悬空行数"
FROM collection_data_records d
LEFT JOIN collection_points p ON p.id = d.collection_point_id
WHERE p.id IS NULL
UNION ALL
SELECT 'domain_pages 指向不存在的域', count(*)
FROM domain_pages pg
LEFT JOIN business_domains bd ON bd.id = pg.domain_id
WHERE bd.id IS NULL
UNION ALL
SELECT 'energy_meters 指向不存在的介质', count(*)
FROM energy_meters m
LEFT JOIN energy_media md ON md.id = m.medium_id
WHERE md.id IS NULL
UNION ALL
SELECT 'energy_readings 指向不存在的表计', count(*)
FROM energy_readings rd
LEFT JOIN energy_meters m ON m.id = rd.meter_id
WHERE m.id IS NULL;

\echo '=== 8. 排班快照表约束（单行表：id 必须恒为 1）==='
SELECT conname AS "约束名", pg_get_constraintdef(oid) AS "定义"
FROM pg_constraint
WHERE conrelid = 'shift_schedule_snapshot'::regclass;
