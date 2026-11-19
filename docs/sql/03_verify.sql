-- =============================================================================
-- CDYC 数据采集平台 · 部署后自检
--
-- 只读脚本，可反复执行。用于确认「建表是否完整」「基础字典是否就位」
-- 「应用启动后是否完成了对账与超管创建」。
--
-- 执行：psql ... -f 03_verify.sql
-- 建议加 -P pager=off 以便直接看结果：psql -P pager=off ... -f 03_verify.sql
-- =============================================================================

\echo '== 1. 迁移记录（部署脚本应为 10 条，且与脚本中出现的迁移一致） =='
SELECT "MigrationId", "ProductVersion"
FROM "__EFMigrationsHistory"
ORDER BY "MigrationId";

\echo ''
\echo '== 2. 业务表清单（应为 22 张，另有 __EFMigrationsHistory） =='
SELECT table_name
FROM information_schema.tables
WHERE table_schema = 'public'
  AND table_type = 'BASE TABLE'
ORDER BY table_name;

\echo ''
\echo '== 3. 索引与约束数量（粗查建表是否完整；缺失通常意味着脚本中断） =='
SELECT (SELECT count(*) FROM pg_indexes WHERE schemaname = 'public') AS index_count,
       (SELECT count(*)
        FROM pg_constraint c
                 JOIN pg_namespace n ON n.oid = c.connamespace
        WHERE n.nspname = 'public')                                  AS constraint_count;

\echo ''
\echo '== 4. 授权字典计数（执行 02_seed_authorization.sql 后：4 / 5 / 4） =='
SELECT (SELECT count(*) FROM business_domains) AS domains,
       (SELECT count(*) FROM permissions)      AS permissions,
       (SELECT count(*) FROM roles)            AS roles;

\echo ''
\echo '== 5. 职能域明细（平台管理应为 is_system = true） =='
SELECT code, name, sort_order, is_system, is_enabled, visible_when_empty
FROM business_domains
ORDER BY sort_order;

\echo ''
\echo '== 6. 角色与权限点映射（超级管理员不挂权限点属正常） =='
SELECT r.code                                                     AS role_code,
       r.name                                                     AS role_name,
       r.page_level,
       r.is_enabled,
       coalesce(string_agg(p.code, ', ' ORDER BY p.code), '(无)') AS permission_codes
FROM roles r
         LEFT JOIN role_permissions rp ON rp.role_id = r.id
         LEFT JOIN permissions p ON p.id = rp.permission_id
GROUP BY r.id, r.code, r.name, r.page_level, r.is_enabled, r.sort_order
ORDER BY r.sort_order;

\echo ''
\echo '== 7. 领域页面（应用首次启动后由 DomainPageReconciler 写入，应为 20 条） =='
SELECT count(*)                            AS page_count,
       count(*) FILTER (WHERE is_orphaned) AS orphaned_count
FROM domain_pages;

\echo ''
\echo '== 8. 超管账号（应用首次启动后由 AuthDataInitializer 创建） =='
SELECT u.user_name,
       u.display_name,
       u.is_enabled,
       u.must_change_password,
       (u.password_hash IS NOT NULL) AS has_local_password,
       (ur.role_id IS NOT NULL)      AS has_super_admin_role
FROM app_users u
         LEFT JOIN user_roles ur
                   ON ur.user_id = u.id
                       AND ur.role_id = (SELECT id FROM roles WHERE code = 'super-admin')
ORDER BY u.user_name;

\echo ''
\echo '== 9. 关键唯一索引是否齐全（缺一条都可能让登录或对账行为异常） =='
SELECT indexname
FROM pg_indexes
WHERE schemaname = 'public'
  AND indexname IN (
                    'ux_business_domains_code',
                    'ux_permissions_code',
                    'ux_roles_code',
                    'ux_app_users_user_name',
                    'ux_domain_pages_page_key',
                    'ux_departments_code',
                    'ux_collection_points_code',
                    'ux_managed_data_definitions_code',
                    'ux_server_connections_code',
                    'ux_energy_media_code',
                    'ux_energy_usage_types_code',
                    'ux_energy_systems_code',
                    'ux_energy_org_units_code',
                    'ux_energy_meters_code',
                    'ux_energy_readings_meter_read_at')
ORDER BY indexname;

\echo ''
\echo '== 10. 后台节点软删除（应存在 deleted_at，唯一索引仅约束未归档行） =='
SELECT c.column_name,
       c.is_nullable,
       c.data_type,
       i.indexdef
FROM information_schema.columns c
         LEFT JOIN pg_indexes i
                   ON i.schemaname = 'public'
                       AND i.tablename = 'managed_data_definitions'
                       AND i.indexname = 'ux_managed_data_definitions_code'
WHERE c.table_schema = 'public'
  AND c.table_name = 'managed_data_definitions'
  AND c.column_name = 'deleted_at';
