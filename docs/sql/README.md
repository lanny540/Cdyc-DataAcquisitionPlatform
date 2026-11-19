# 生产部署 SQL 说明

本目录面向「生产环境未安装 `dotnet ef`」的部署场景，提供纯 SQL 的建表与基础数据脚本。

## 一、文件清单与执行顺序

| 顺序 | 文件                        | 是否必执行 | 作用                                                            |
|------|-----------------------------|------------|-----------------------------------------------------------------|
| 1    | `01_schema.sql`             | **必须**   | 建 16 张业务表、索引与 CHECK 约束，附带 `__EFMigrationsHistory` |
| 2    | `02_seed_authorization.sql` | 推荐       | 职能域 4、权限点 5、内置角色 4、角色权限关联 3                  |
| 3    | `03_verify.sql`             | 建议       | 只读自检，确认表结构与字典完整                                  |
| —    | 应用启动                    | —          | 自动写入领域页面 17 条、创建初始超管                            |

```bash
export PGPASSWORD='<口令>'
PSQL='psql -h <主机> -p 5432 -U <账号> -d <库名> -v ON_ERROR_STOP=1 -P pager=off'

$PSQL -f 01_schema.sql
$PSQL -f 02_seed_authorization.sql
$PSQL -f 03_verify.sql
```

前置条件：PostgreSQL **13 及以上**（脚本使用内置函数 `gen_random_uuid()`），
库与客户端编码均为 UTF-8（约束里含 `'在线'`/`'离线'` 等中文取值）。

```sql
SHOW server_encoding;  -- 期望 UTF8
SHOW client_encoding;  -- 期望 UTF8
```

## 二、三个刻意的「不插入」

部署脚本里最容易出错的地方是「把不该手工插的数据插了」。以下三类数据由应用在启动时写入， **不要手工 INSERT**。

### 1. 初始超管（`app_users`）不插

`AuthDataInitializer` 在启动时按 `Auth:BootstrapAdmin` 配置创建首个超级管理员：
口令用 `PasswordHasher`（PBKDF2-SHA256，210000 次迭代，随机盐）哈希，
`must_change_password = true`，首次登录强制改密。

手工插入的代价：

- 需要自己算出 `PBKDF2-SHA256$210000$<base64盐>$<base64散列>` 格式的串，格式错一个字符就无法登录，且报错只说「用户名或密码错误」。
- 若把默认口令的散列写进脚本，等于把「默认口令」固化到生产库，绕过了「Production 下默认口令拒绝启动」这道闸门，也让首登强制改密形同虚设。

`02_seed_authorization.sql` 已把 `super-admin` 角色准备好，初始化器建号后直接挂上该角色。

### 2. 领域页面（`domain_pages`）不插

`DomainPageReconciler` 在启动时按 `NavigationCatalog.cs` 声明的 17 个页面做对账：
按 `page_key` 新增或刷新，代码中已删除的页面标记 `is_orphaned` 而不是删除。

手工插入的问题：`icon` 列宽 `varchar(100)`，而 `Icons.Material.*` 是整段 SVG path，
落库会被截断；代码本身也刻意不写这一列。页面归属必须与代码保持同步，
否则新版本部署后菜单会指向不存在的路由。

### 3. 业务数据（采集服务器、点位、后台数据定义）不插

这些是现场实际配置，脚本无法凭空生成。需要从开发/试点库导出，见第四节。

## 三、生产环境配置清单

建表只是第一步，以下配置缺失会导致「能建库但登不上」或「重启后全员掉线」。

| 配置项                                      | 说明                                                                              |
|---------------------------------------------|-----------------------------------------------------------------------------------|
| `ConnectionStrings:PostgreSql`              | 指向生产库                                                                        |
| `Auth:BootstrapAdmin:UserName` / `Password` | 初始超管。**口令不得留默认值**，Production 下会用默认口令直接拒绝启动             |
| `Auth:DataProtection:KeyPath`               | 数据保护密钥目录。默认在内容根目录的 `data-protection-keys`，容器部署须指向持久卷 |
| `Auth:Board`                                | 车间只读终端 IP 白名单，不需要就不配                                              |
| `Auth:DepartmentAdmin:Enabled`              | 部门管理员角色开关，默认关闭                                                      |

**密钥目录必须备份。** 它同时用于 Cookie 加密与采集服务器凭据（ClientSecret 等）的落库加密。
密钥环丢失的后果有两个：所有已签发会话立即作废（表现为「全员同时被踢下线」），
以及库中已加密的服务器凭据再也解不开，只能重新录入。
多实例部署时所有实例必须共用同一目录，否则用户会在实例之间反复掉登录。

应用启动时**不会**执行 `Database.Migrate()`，所以本脚本是建表的唯一途径；
将来若改为自动迁移，本脚本的幂等写法仍然安全，不会重复建表。

## 四、业务数据迁移（如需保留现场配置）

主键是 `uuid`，没有序列，不存在 `setval` 的问题；只需按外键依赖顺序导入。
逐表导出比一次导全库更稳妥，可避免 `pg_dump` 按表名排序导致的「子表先于父表插入」外键报错。

```bash
SRC='-h <源主机> -U <账号> -d <源库>'
DUMP='pg_dump --data-only --inserts --no-owner --no-privileges'

# 1) 采集服务器（被点位数据的 server_connection_id 引用）
$DUMP $SRC -t server_connections         > 10_server_connections.sql

# 2) 采集点（被采集记录引用）
$DUMP $SRC -t collection_points          > 11_collection_points.sql

# 3) 后台数据定义（server_connection_id 可空）
$DUMP $SRC -t managed_data_definitions   > 12_managed_data_definitions.sql

# 4) 历史采集记录（数据量大时单独评估，可不迁）
$DUMP $SRC -t collection_data_records    > 13_collection_data_records.sql
```

按 10 → 11 → 12 → 13 的顺序导入。导出的文件是纯 `INSERT`，若目标库已有部分数据会主键冲突，
因此导入前确认目标表为空：

```sql
SELECT 'server_connections' AS t, count(*) FROM server_connections
UNION ALL SELECT 'collection_points',        count(*) FROM collection_points
UNION ALL SELECT 'managed_data_definitions', count(*) FROM managed_data_definitions
UNION ALL SELECT 'collection_data_records',  count(*) FROM collection_data_records;
```

不需要的整表重来（**会连带删除依赖数据**，仅用于空库初始化阶段）：

```sql
TRUNCATE collection_data_records, collection_points, managed_data_definitions, server_connections CASCADE;
```

## 五、重复执行与升级

- `01_schema.sql` 的全部语句以 `__EFMigrationsHistory` 中的迁移记录为开关，
  重复执行不会报错，也不会重复建表；版本升级时可整份重跑。
- `02_seed_authorization.sql` 按 `code` 做 `ON CONFLICT DO NOTHING`，
  且**不覆盖**既有值。管理员在「域与页面配置」里改过的名称、排序、启停不会被脚本回滚。
- 脚本不含任何 `DROP`，不会破坏已有数据。

## 六、常见故障对照

| 现象                                                 | 原因                                                         |
|------------------------------------------------------|--------------------------------------------------------------|
| 执行时报 `function gen_random_uuid() does not exist` | PostgreSQL 低于 13，需安装 `pgcrypto` 扩展或升级             |
| 启动后菜单空白 / 只有顶栏                            | `business_domains` 为空，页面被对账跳过；检查第 2 步是否执行 |
| 登录提示密码错误                                     | 手工插过 `app_users`，散列格式不符；删掉该行让初始化器重建   |
| 重启后所有人掉登录                                   | `Auth:DataProtection:KeyPath` 未持久化，密钥环随部署丢失     |
| 提示「请先修改初始密码」并拒绝其它操作               | 首登强制改密，属预期行为                                     |
| 中文状态约束插入失败                                 | 数据库或客户端编码不是 UTF-8                                 |
