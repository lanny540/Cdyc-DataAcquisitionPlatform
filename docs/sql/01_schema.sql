-- =============================================================================
-- CDYC 数据采集平台 · PostgreSQL 建表脚本
--
-- 生成方式：dotnet ef migrations script --idempotent
--           （EF Core 10.0.14 / Npgsql，迁移快照 DataAcquisitionPlatformDbContextModelSnapshot）
-- 适用版本：PostgreSQL 13 及以上（脚本用到内置函数 gen_random_uuid()）
--
-- 特性：
--   1. 幂等。全部语句以 __EFMigrationsHistory 中的迁移记录为开关，重复执行不会报错，
--      也不会重复建表；可在升级部署时直接重跑。
--   2. 按迁移分事务。单个迁移内的语句在同一事务中，失败即整体回滚，不会留下半张表。
--   3. 含 __EFMigrationsHistory 迁移历史表，保留未来继续用 dotnet ef 演进的能力。
--
-- 执行前请确认：
--   - 已创建目标数据库与业务账号，并具备建表、建索引、建约束权限。
--   - 使用 UTF-8 客户端编码（constraint 中含中文取值，如 '在线'/'离线'）。
--     检查：SHOW client_encoding;  必须为 UTF8。
--
-- 执行：
--   psql "host=<主机> port=5432 dbname=<库名> user=<账号>" -v ON_ERROR_STOP=1 -f 01_schema.sql
--
-- 本脚本只建表，不插入业务数据。基础授权字典见 02_seed_authorization.sql，
-- 执行后的自检见 03_verify.sql，部署顺序与注意事项见 README.md。
-- =============================================================================
CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917031558_InitialCreate') THEN
    CREATE TABLE collection_points (
        id uuid NOT NULL,
        code character varying(64) NOT NULL,
        name character varying(200) NOT NULL,
        protocol character varying(100) NOT NULL,
        endpoint character varying(500) NOT NULL,
        is_enabled boolean NOT NULL,
        communication_status character varying(50) NOT NULL,
        source character varying(50) NOT NULL,
        last_error text,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_collection_points" PRIMARY KEY (id),
        CONSTRAINT ck_collection_points_code_not_blank CHECK (btrim(code) <> ''),
        CONSTRAINT ck_collection_points_code_upper CHECK (code = upper(code)),
        CONSTRAINT ck_collection_points_communication_status CHECK (communication_status IN ('在线', '离线', '停用', '未知')),
        CONSTRAINT ck_collection_points_endpoint_not_blank CHECK (btrim(endpoint) <> ''),
        CONSTRAINT ck_collection_points_name_not_blank CHECK (btrim(name) <> ''),
        CONSTRAINT ck_collection_points_protocol_not_blank CHECK (btrim(protocol) <> ''),
        CONSTRAINT ck_collection_points_source CHECK (source IN ('Server', 'Local'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917031558_InitialCreate') THEN
    CREATE TABLE managed_data_definitions (
        id uuid NOT NULL,
        code character varying(64) NOT NULL,
        name character varying(200) NOT NULL,
        acquisition_type character varying(100) NOT NULL,
        connection_address character varying(500) NOT NULL,
        identifier character varying(200) NOT NULL,
        department character varying(100) NOT NULL,
        process_code character varying(100) NOT NULL,
        data_category character varying(100) NOT NULL,
        unit character varying(50) NOT NULL DEFAULT '',
        description character varying(1000) NOT NULL DEFAULT '',
        configuration_json jsonb NOT NULL DEFAULT ('{}'::jsonb),
        business_tags_json jsonb NOT NULL DEFAULT ('{}'::jsonb),
        collection_interval_seconds integer NOT NULL DEFAULT 300,
        is_enabled boolean NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_managed_data_definitions" PRIMARY KEY (id),
        CONSTRAINT ck_managed_data_definitions_acquisition_type_not_blank CHECK (btrim(acquisition_type) <> ''),
        CONSTRAINT ck_managed_data_definitions_code_not_blank CHECK (btrim(code) <> ''),
        CONSTRAINT ck_managed_data_definitions_code_upper CHECK (code = upper(code)),
        CONSTRAINT ck_managed_data_definitions_connection_address_not_blank CHECK (btrim(connection_address) <> ''),
        CONSTRAINT ck_managed_data_definitions_data_category_not_blank CHECK (btrim(data_category) <> ''),
        CONSTRAINT ck_managed_data_definitions_department_not_blank CHECK (btrim(department) <> ''),
        CONSTRAINT ck_managed_data_definitions_identifier_not_blank CHECK (btrim(identifier) <> ''),
        CONSTRAINT ck_managed_data_definitions_interval_seconds_positive CHECK (collection_interval_seconds > 0),
        CONSTRAINT ck_managed_data_definitions_name_not_blank CHECK (btrim(name) <> ''),
        CONSTRAINT ck_managed_data_definitions_process_code_not_blank CHECK (btrim(process_code) <> '')
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917031558_InitialCreate') THEN
    CREATE TABLE collection_data_records (
        id uuid NOT NULL,
        collection_point_id uuid NOT NULL,
        metric_name character varying(100) NOT NULL,
        value numeric(18,4) NOT NULL,
        unit character varying(50) NOT NULL,
        collected_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_collection_data_records" PRIMARY KEY (id),
        CONSTRAINT ck_collection_data_records_metric_name_not_blank CHECK (btrim(metric_name) <> ''),
        CONSTRAINT ck_collection_data_records_unit_not_blank CHECK (btrim(unit) <> ''),
        CONSTRAINT collection_data_records_collection_point_id_fkey FOREIGN KEY (collection_point_id) REFERENCES collection_points (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917031558_InitialCreate') THEN
    CREATE INDEX ix_collection_data_records_collected_at ON collection_data_records (collected_at DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917031558_InitialCreate') THEN
    CREATE INDEX ix_collection_data_records_collection_point_id ON collection_data_records (collection_point_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917031558_InitialCreate') THEN
    CREATE INDEX ix_collection_data_records_point_metric_collected_at ON collection_data_records (collection_point_id, metric_name, collected_at DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917031558_InitialCreate') THEN
    CREATE INDEX ix_collection_points_source_updated_at ON collection_points (source, updated_at DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917031558_InitialCreate') THEN
    CREATE INDEX ix_collection_points_status_updated_at ON collection_points (communication_status, updated_at DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917031558_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_collection_points_code ON collection_points (code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917031558_InitialCreate') THEN
    CREATE INDEX ix_managed_data_definitions_department_process_code ON managed_data_definitions (department, process_code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917031558_InitialCreate') THEN
    CREATE INDEX ix_managed_data_definitions_type_enabled_updated_at ON managed_data_definitions (acquisition_type, is_enabled, updated_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917031558_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_managed_data_definitions_code ON managed_data_definitions (code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917031558_InitialCreate') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260917031558_InitialCreate', '10.0.14');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917080800_AddServerConnections') THEN
    CREATE TABLE server_connections (
        id uuid NOT NULL,
        code character varying(64) NOT NULL,
        display_name character varying(200) NOT NULL,
        acquisition_type character varying(100) NOT NULL,
        address character varying(500) NOT NULL,
        configuration_json jsonb NOT NULL DEFAULT ('{}'::jsonb),
        is_enabled boolean NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_server_connections" PRIMARY KEY (id),
        CONSTRAINT ck_server_connections_acquisition_type_not_blank CHECK (btrim(acquisition_type) <> ''),
        CONSTRAINT ck_server_connections_address_not_blank CHECK (btrim(address) <> ''),
        CONSTRAINT ck_server_connections_code_not_blank CHECK (btrim(code) <> ''),
        CONSTRAINT ck_server_connections_code_upper CHECK (code = upper(code)),
        CONSTRAINT ck_server_connections_display_name_not_blank CHECK (btrim(display_name) <> '')
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917080800_AddServerConnections') THEN
    CREATE INDEX ix_server_connections_type_enabled_updated_at ON server_connections (acquisition_type, is_enabled, updated_at DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917080800_AddServerConnections') THEN
    CREATE UNIQUE INDEX ux_server_connections_code ON server_connections (code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917080800_AddServerConnections') THEN
    CREATE UNIQUE INDEX ux_server_connections_type_address ON server_connections (acquisition_type, address);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917080800_AddServerConnections') THEN
    INSERT INTO server_connections (
        id,
        code,
        display_name,
        acquisition_type,
        address,
        configuration_json,
        is_enabled,
        updated_at)
    SELECT DISTINCT ON (d.acquisition_type, rtrim(d.connection_address, '/'))
        (
            substr(md5(d.acquisition_type || '|' || rtrim(d.connection_address, '/')), 1, 8) || '-' ||
            substr(md5(d.acquisition_type || '|' || rtrim(d.connection_address, '/')), 9, 4) || '-' ||
            substr(md5(d.acquisition_type || '|' || rtrim(d.connection_address, '/')), 13, 4) || '-' ||
            substr(md5(d.acquisition_type || '|' || rtrim(d.connection_address, '/')), 17, 4) || '-' ||
            substr(md5(d.acquisition_type || '|' || rtrim(d.connection_address, '/')), 21, 12)
        )::uuid,
        'SRV_' || upper(substr(md5(d.acquisition_type || '|' || rtrim(d.connection_address, '/')), 1, 60)),
        coalesce(
            nullif(d.configuration_json ->> 'serverName', ''),
            d.acquisition_type || ' 服务器'),
        d.acquisition_type,
        rtrim(d.connection_address, '/'),
        d.configuration_json,
        d.is_enabled,
        d.updated_at
    FROM managed_data_definitions d
    WHERE btrim(d.connection_address) <> ''
    ORDER BY
        d.acquisition_type,
        rtrim(d.connection_address, '/'),
        d.is_enabled DESC,
        d.updated_at DESC;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917080800_AddServerConnections') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260917080800_AddServerConnections', '10.0.14');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917082256_LinkManagedDefinitionsToServerConnections') THEN
    ALTER TABLE managed_data_definitions ADD server_connection_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917082256_LinkManagedDefinitionsToServerConnections') THEN
    CREATE INDEX ix_managed_data_definitions_server_connection_id ON managed_data_definitions (server_connection_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917082256_LinkManagedDefinitionsToServerConnections') THEN
    ALTER TABLE managed_data_definitions ADD CONSTRAINT managed_data_definitions_server_connection_id_fkey FOREIGN KEY (server_connection_id) REFERENCES server_connections (id) ON DELETE SET NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917082256_LinkManagedDefinitionsToServerConnections') THEN
    UPDATE managed_data_definitions d
    SET server_connection_id = s.id
    FROM server_connections s
    WHERE d.acquisition_type = s.acquisition_type
      AND rtrim(d.connection_address, '/') = s.address;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917082256_LinkManagedDefinitionsToServerConnections') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260917082256_LinkManagedDefinitionsToServerConnections', '10.0.14');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261019092152_ExtendAcquisitionServerConfiguration') THEN
    ALTER TABLE server_connections ADD created_at timestamp with time zone NOT NULL DEFAULT (now());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261019092152_ExtendAcquisitionServerConfiguration') THEN
    ALTER TABLE server_connections ADD description character varying(1000) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261019092152_ExtendAcquisitionServerConfiguration') THEN
    ALTER TABLE server_connections ADD last_probe_message character varying(1000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261019092152_ExtendAcquisitionServerConfiguration') THEN
    ALTER TABLE server_connections ADD last_probe_status character varying(50) NOT NULL DEFAULT '未测试';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261019092152_ExtendAcquisitionServerConfiguration') THEN
    ALTER TABLE server_connections ADD last_probed_at timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261019092152_ExtendAcquisitionServerConfiguration') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261019092152_ExtendAcquisitionServerConfiguration', '10.0.14');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103084302_AddAuthenticationAndAuthorization') THEN
    CREATE TABLE audit_logs (
        id uuid NOT NULL,
        occurred_at timestamp with time zone NOT NULL,
        user_id uuid,
        user_name character varying(100),
        department_name character varying(200),
        action_code character varying(100) NOT NULL,
        target_type character varying(100),
        target_id character varying(100),
        target_name character varying(200),
        result character varying(20) NOT NULL,
        detail jsonb,
        ip character varying(64),
        user_agent character varying(500),
        CONSTRAINT "PK_audit_logs" PRIMARY KEY (id),
        CONSTRAINT ck_audit_logs_action_code_not_blank CHECK (btrim(action_code) <> ''),
        CONSTRAINT ck_audit_logs_result CHECK (result IN ('success', 'failed'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103084302_AddAuthenticationAndAuthorization') THEN
    CREATE TABLE business_domains (
        id uuid NOT NULL,
        code character varying(64) NOT NULL,
        name character varying(100) NOT NULL,
        icon character varying(100),
        sort_order integer NOT NULL,
        is_system boolean NOT NULL,
        is_enabled boolean NOT NULL,
        visible_when_empty boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_business_domains" PRIMARY KEY (id),
        CONSTRAINT ck_business_domains_code_not_blank CHECK (btrim(code) <> ''),
        CONSTRAINT ck_business_domains_name_not_blank CHECK (btrim(name) <> '')
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103084302_AddAuthenticationAndAuthorization') THEN
    CREATE TABLE departments (
        id uuid NOT NULL,
        code character varying(64) NOT NULL,
        name character varying(200) NOT NULL,
        external_code character varying(200),
        is_enabled boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_departments" PRIMARY KEY (id),
        CONSTRAINT ck_departments_code_not_blank CHECK (btrim(code) <> ''),
        CONSTRAINT ck_departments_name_not_blank CHECK (btrim(name) <> '')
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103084302_AddAuthenticationAndAuthorization') THEN
    CREATE TABLE permissions (
        id uuid NOT NULL,
        code character varying(100) NOT NULL,
        name character varying(100) NOT NULL,
        group_name character varying(100),
        sort_order integer NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_permissions" PRIMARY KEY (id),
        CONSTRAINT ck_permissions_code_not_blank CHECK (btrim(code) <> ''),
        CONSTRAINT ck_permissions_code_prefix CHECK (code LIKE 'action.%')
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103084302_AddAuthenticationAndAuthorization') THEN
    CREATE TABLE roles (
        id uuid NOT NULL,
        code character varying(64) NOT NULL,
        name character varying(100) NOT NULL,
        page_level integer NOT NULL,
        is_builtin boolean NOT NULL,
        is_enabled boolean NOT NULL,
        sort_order integer NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_roles" PRIMARY KEY (id),
        CONSTRAINT ck_roles_code_not_blank CHECK (btrim(code) <> ''),
        CONSTRAINT ck_roles_name_not_blank CHECK (btrim(name) <> ''),
        CONSTRAINT ck_roles_page_level CHECK (page_level >= 1)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103084302_AddAuthenticationAndAuthorization') THEN
    CREATE TABLE domain_pages (
        id uuid NOT NULL,
        page_key character varying(100) NOT NULL,
        domain_id uuid NOT NULL,
        route character varying(200) NOT NULL,
        title character varying(100) NOT NULL,
        group_title character varying(100),
        icon character varying(100),
        accent character varying(100),
        access_level integer NOT NULL,
        sort_order integer NOT NULL,
        is_enabled boolean NOT NULL,
        source character varying(20) NOT NULL,
        is_orphaned boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_domain_pages" PRIMARY KEY (id),
        CONSTRAINT ck_domain_pages_access_level CHECK (access_level IN (1, 2)),
        CONSTRAINT ck_domain_pages_page_key_not_blank CHECK (btrim(page_key) <> ''),
        CONSTRAINT ck_domain_pages_route_not_blank CHECK (btrim(route) <> ''),
        CONSTRAINT ck_domain_pages_source CHECK (source IN ('code', 'override')),
        CONSTRAINT ck_domain_pages_title_not_blank CHECK (btrim(title) <> ''),
        CONSTRAINT fk_domain_pages_business_domains FOREIGN KEY (domain_id) REFERENCES business_domains (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103084302_AddAuthenticationAndAuthorization') THEN
    CREATE TABLE app_users (
        id uuid NOT NULL,
        user_name character varying(100) NOT NULL,
        display_name character varying(100) NOT NULL,
        department_id uuid,
        email character varying(200),
        is_enabled boolean NOT NULL,
        must_change_password boolean NOT NULL,
        password_hash character varying(500),
        access_failed_count integer NOT NULL,
        lockout_end_at timestamp with time zone,
        last_login_at timestamp with time zone,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_app_users" PRIMARY KEY (id),
        CONSTRAINT ck_app_users_access_failed_count CHECK (access_failed_count >= 0),
        CONSTRAINT ck_app_users_display_name_not_blank CHECK (btrim(display_name) <> ''),
        CONSTRAINT ck_app_users_user_name_lower CHECK (user_name = lower(user_name)),
        CONSTRAINT ck_app_users_user_name_not_blank CHECK (btrim(user_name) <> ''),
        CONSTRAINT fk_app_users_departments FOREIGN KEY (department_id) REFERENCES departments (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103084302_AddAuthenticationAndAuthorization') THEN
    CREATE TABLE department_domain_grants (
        department_id uuid NOT NULL,
        domain_id uuid NOT NULL,
        granted_at timestamp with time zone NOT NULL,
        granted_by uuid,
        CONSTRAINT "PK_department_domain_grants" PRIMARY KEY (department_id, domain_id),
        CONSTRAINT fk_department_domain_grants_business_domains FOREIGN KEY (domain_id) REFERENCES business_domains (id) ON DELETE CASCADE,
        CONSTRAINT fk_department_domain_grants_departments FOREIGN KEY (department_id) REFERENCES departments (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103084302_AddAuthenticationAndAuthorization') THEN
    CREATE TABLE role_permissions (
        role_id uuid NOT NULL,
        permission_id uuid NOT NULL,
        CONSTRAINT "PK_role_permissions" PRIMARY KEY (role_id, permission_id),
        CONSTRAINT fk_role_permissions_permissions FOREIGN KEY (permission_id) REFERENCES permissions (id) ON DELETE CASCADE,
        CONSTRAINT fk_role_permissions_roles FOREIGN KEY (role_id) REFERENCES roles (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103084302_AddAuthenticationAndAuthorization') THEN
    CREATE TABLE external_identities (
        id uuid NOT NULL,
        user_id uuid NOT NULL,
        provider character varying(50) NOT NULL,
        external_id character varying(200) NOT NULL,
        employee_no character varying(64),
        last_synced_at timestamp with time zone,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_external_identities" PRIMARY KEY (id),
        CONSTRAINT fk_external_identities_app_users FOREIGN KEY (user_id) REFERENCES app_users (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103084302_AddAuthenticationAndAuthorization') THEN
    CREATE TABLE user_roles (
        user_id uuid NOT NULL,
        role_id uuid NOT NULL,
        CONSTRAINT "PK_user_roles" PRIMARY KEY (user_id, role_id),
        CONSTRAINT fk_user_roles_app_users FOREIGN KEY (user_id) REFERENCES app_users (id) ON DELETE CASCADE,
        CONSTRAINT fk_user_roles_roles FOREIGN KEY (role_id) REFERENCES roles (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103084302_AddAuthenticationAndAuthorization') THEN
    CREATE INDEX ix_app_users_department_id ON app_users (department_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103084302_AddAuthenticationAndAuthorization') THEN
    CREATE UNIQUE INDEX ux_app_users_user_name ON app_users (user_name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103084302_AddAuthenticationAndAuthorization') THEN
    CREATE INDEX ix_audit_logs_occurred_at ON audit_logs (occurred_at DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103084302_AddAuthenticationAndAuthorization') THEN
    CREATE INDEX ix_audit_logs_target ON audit_logs (target_type, target_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103084302_AddAuthenticationAndAuthorization') THEN
    CREATE INDEX ix_audit_logs_user_occurred_at ON audit_logs (user_id, occurred_at DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103084302_AddAuthenticationAndAuthorization') THEN
    CREATE INDEX ix_business_domains_sort_order ON business_domains (sort_order);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103084302_AddAuthenticationAndAuthorization') THEN
    CREATE UNIQUE INDEX ux_business_domains_code ON business_domains (code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103084302_AddAuthenticationAndAuthorization') THEN
    CREATE INDEX "IX_department_domain_grants_domain_id" ON department_domain_grants (domain_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103084302_AddAuthenticationAndAuthorization') THEN
    CREATE UNIQUE INDEX ux_departments_code ON departments (code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103084302_AddAuthenticationAndAuthorization') THEN
    CREATE UNIQUE INDEX ux_departments_external_code ON departments (external_code) WHERE external_code IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103084302_AddAuthenticationAndAuthorization') THEN
    CREATE INDEX ix_domain_pages_domain_sort_order ON domain_pages (domain_id, sort_order);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103084302_AddAuthenticationAndAuthorization') THEN
    CREATE INDEX ix_domain_pages_route ON domain_pages (route);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103084302_AddAuthenticationAndAuthorization') THEN
    CREATE UNIQUE INDEX ux_domain_pages_page_key ON domain_pages (page_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103084302_AddAuthenticationAndAuthorization') THEN
    CREATE INDEX ix_external_identities_employee_no ON external_identities (employee_no);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103084302_AddAuthenticationAndAuthorization') THEN
    CREATE INDEX "IX_external_identities_user_id" ON external_identities (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103084302_AddAuthenticationAndAuthorization') THEN
    CREATE UNIQUE INDEX ux_external_identities_provider_external_id ON external_identities (provider, external_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103084302_AddAuthenticationAndAuthorization') THEN
    CREATE UNIQUE INDEX ux_permissions_code ON permissions (code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103084302_AddAuthenticationAndAuthorization') THEN
    CREATE INDEX "IX_role_permissions_permission_id" ON role_permissions (permission_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103084302_AddAuthenticationAndAuthorization') THEN
    CREATE UNIQUE INDEX ux_roles_code ON roles (code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103084302_AddAuthenticationAndAuthorization') THEN
    CREATE INDEX "IX_user_roles_role_id" ON user_roles (role_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103084302_AddAuthenticationAndAuthorization') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261103084302_AddAuthenticationAndAuthorization', '10.0.14');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103135618_AddDirectorySyncIssues') THEN
    CREATE TABLE directory_sync_issues (
        id uuid NOT NULL,
        provider character varying(50) NOT NULL,
        issue_type character varying(50) NOT NULL,
        external_code character varying(200) NOT NULL,
        sample_user_name character varying(100),
        occurrences integer NOT NULL,
        first_seen_at timestamp with time zone NOT NULL,
        last_seen_at timestamp with time zone NOT NULL,
        resolved_at timestamp with time zone,
        resolved_by uuid,
        CONSTRAINT "PK_directory_sync_issues" PRIMARY KEY (id),
        CONSTRAINT ck_directory_sync_issues_external_code_not_blank CHECK (btrim(external_code) <> ''),
        CONSTRAINT ck_directory_sync_issues_occurrences CHECK (occurrences >= 1)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103135618_AddDirectorySyncIssues') THEN
    CREATE INDEX ix_directory_sync_issues_last_seen_at ON directory_sync_issues (last_seen_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103135618_AddDirectorySyncIssues') THEN
    CREATE UNIQUE INDEX ux_directory_sync_issues_pending ON directory_sync_issues (provider, issue_type, external_code) WHERE resolved_at IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103135618_AddDirectorySyncIssues') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261103135618_AddDirectorySyncIssues', '10.0.14');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103145155_AddUserDisabledSource') THEN
    ALTER TABLE app_users ADD disabled_source character varying(20);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103145155_AddUserDisabledSource') THEN
    ALTER TABLE app_users ADD CONSTRAINT ck_app_users_disabled_source CHECK (disabled_source is null or disabled_source in ('manual', 'directory'));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103145155_AddUserDisabledSource') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261103145155_AddUserDisabledSource', '10.0.14');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103150120_AddUserSecurityStamp') THEN
    ALTER TABLE app_users ADD security_stamp uuid NOT NULL DEFAULT (gen_random_uuid());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261103150120_AddUserSecurityStamp') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261103150120_AddUserSecurityStamp', '10.0.14');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261114103550_AddEnergyDomain') THEN
    CREATE TABLE energy_media (
        id uuid NOT NULL,
        code character varying(32) NOT NULL,
        name character varying(64) NOT NULL,
        unit character varying(16) NOT NULL,
        is_primary_energy boolean NOT NULL,
        coal_factor numeric(18,6),
        sort_order integer NOT NULL,
        is_enabled boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_energy_media" PRIMARY KEY (id),
        CONSTRAINT ck_energy_media_coal_factor CHECK (coal_factor is null or coal_factor >= 0),
        CONSTRAINT ck_energy_media_code_not_blank CHECK (btrim(code) <> ''),
        CONSTRAINT ck_energy_media_name_not_blank CHECK (btrim(name) <> ''),
        CONSTRAINT ck_energy_media_unit_not_blank CHECK (btrim(unit) <> '')
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261114103550_AddEnergyDomain') THEN
    CREATE TABLE energy_org_units (
        id uuid NOT NULL,
        code character varying(64) NOT NULL,
        name character varying(200) NOT NULL,
        level character varying(16) NOT NULL,
        parent_id uuid,
        department_id uuid,
        sort_order integer NOT NULL,
        is_enabled boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_energy_org_units" PRIMARY KEY (id),
        CONSTRAINT ck_energy_org_units_code_not_blank CHECK (btrim(code) <> ''),
        CONSTRAINT ck_energy_org_units_level CHECK (level in ('park', 'department', 'process')),
        CONSTRAINT ck_energy_org_units_name_not_blank CHECK (btrim(name) <> ''),
        CONSTRAINT ck_energy_org_units_parent_by_level CHECK ((level = 'park' and parent_id is null) or (level <> 'park' and parent_id is not null)),
        CONSTRAINT fk_energy_org_units_departments FOREIGN KEY (department_id) REFERENCES departments (id) ON DELETE RESTRICT,
        CONSTRAINT fk_energy_org_units_parent FOREIGN KEY (parent_id) REFERENCES energy_org_units (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261114103550_AddEnergyDomain') THEN
    CREATE TABLE energy_systems (
        id uuid NOT NULL,
        code character varying(32) NOT NULL,
        name character varying(64) NOT NULL,
        sort_order integer NOT NULL,
        is_system boolean NOT NULL,
        is_enabled boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_energy_systems" PRIMARY KEY (id),
        CONSTRAINT ck_energy_systems_code_not_blank CHECK (btrim(code) <> ''),
        CONSTRAINT ck_energy_systems_name_not_blank CHECK (btrim(name) <> '')
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261114103550_AddEnergyDomain') THEN
    CREATE TABLE energy_usage_types (
        id uuid NOT NULL,
        code character varying(32) NOT NULL,
        name character varying(64) NOT NULL,
        sort_order integer NOT NULL,
        is_system boolean NOT NULL,
        is_enabled boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_energy_usage_types" PRIMARY KEY (id),
        CONSTRAINT ck_energy_usage_types_code_not_blank CHECK (btrim(code) <> ''),
        CONSTRAINT ck_energy_usage_types_name_not_blank CHECK (btrim(name) <> '')
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261114103550_AddEnergyDomain') THEN
    CREATE TABLE energy_meters (
        id uuid NOT NULL,
        code character varying(64) NOT NULL,
        name character varying(200) NOT NULL,
        medium_id uuid NOT NULL,
        org_unit_id uuid NOT NULL,
        usage_type_id uuid NOT NULL,
        system_type_id uuid NOT NULL,
        parent_meter_id uuid,
        is_summary boolean NOT NULL,
        ratio numeric(18,6) NOT NULL,
        is_cumulative boolean NOT NULL,
        is_enabled boolean NOT NULL,
        remark character varying(500),
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_energy_meters" PRIMARY KEY (id),
        CONSTRAINT ck_energy_meters_code_not_blank CHECK (btrim(code) <> ''),
        CONSTRAINT ck_energy_meters_name_not_blank CHECK (btrim(name) <> ''),
        CONSTRAINT ck_energy_meters_ratio_positive CHECK (ratio > 0),
        CONSTRAINT fk_energy_meters_energy_media FOREIGN KEY (medium_id) REFERENCES energy_media (id) ON DELETE RESTRICT,
        CONSTRAINT fk_energy_meters_energy_org_units FOREIGN KEY (org_unit_id) REFERENCES energy_org_units (id) ON DELETE RESTRICT,
        CONSTRAINT fk_energy_meters_energy_systems FOREIGN KEY (system_type_id) REFERENCES energy_systems (id) ON DELETE RESTRICT,
        CONSTRAINT fk_energy_meters_energy_usage_types FOREIGN KEY (usage_type_id) REFERENCES energy_usage_types (id) ON DELETE RESTRICT,
        CONSTRAINT fk_energy_meters_parent FOREIGN KEY (parent_meter_id) REFERENCES energy_meters (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261114103550_AddEnergyDomain') THEN
    CREATE TABLE energy_readings (
        id uuid NOT NULL,
        meter_id uuid NOT NULL,
        read_at timestamp with time zone NOT NULL,
        value numeric(18,6) NOT NULL,
        source character varying(16) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_energy_readings" PRIMARY KEY (id),
        CONSTRAINT ck_energy_readings_source CHECK (source in ('manual', 'collection', 'import')),
        CONSTRAINT fk_energy_readings_energy_meters FOREIGN KEY (meter_id) REFERENCES energy_meters (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261114103550_AddEnergyDomain') THEN
    CREATE UNIQUE INDEX ux_energy_media_code ON energy_media (code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261114103550_AddEnergyDomain') THEN
    CREATE INDEX ix_energy_meters_medium_id ON energy_meters (medium_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261114103550_AddEnergyDomain') THEN
    CREATE INDEX ix_energy_meters_org_unit_id ON energy_meters (org_unit_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261114103550_AddEnergyDomain') THEN
    CREATE INDEX ix_energy_meters_parent_meter_id ON energy_meters (parent_meter_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261114103550_AddEnergyDomain') THEN
    CREATE INDEX ix_energy_meters_system_type_id ON energy_meters (system_type_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261114103550_AddEnergyDomain') THEN
    CREATE INDEX ix_energy_meters_usage_type_id ON energy_meters (usage_type_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261114103550_AddEnergyDomain') THEN
    CREATE UNIQUE INDEX ux_energy_meters_code ON energy_meters (code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261114103550_AddEnergyDomain') THEN
    CREATE INDEX ix_energy_org_units_department_id ON energy_org_units (department_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261114103550_AddEnergyDomain') THEN
    CREATE INDEX ix_energy_org_units_parent_id ON energy_org_units (parent_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261114103550_AddEnergyDomain') THEN
    CREATE UNIQUE INDEX ux_energy_org_units_code ON energy_org_units (code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261114103550_AddEnergyDomain') THEN
    CREATE UNIQUE INDEX ux_energy_readings_meter_read_at ON energy_readings (meter_id, read_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261114103550_AddEnergyDomain') THEN
    CREATE UNIQUE INDEX ux_energy_systems_code ON energy_systems (code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261114103550_AddEnergyDomain') THEN
    CREATE UNIQUE INDEX ux_energy_usage_types_code ON energy_usage_types (code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261114103550_AddEnergyDomain') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261114103550_AddEnergyDomain', '10.0.14');
    END IF;
END $EF$;
COMMIT;

