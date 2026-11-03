using DAP.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DAP.Infrastructure.DataAccess.Persistence;

/// <summary>
/// 表示数据采集平台的 PostgreSQL 上下文。
/// </summary>
public sealed class DataAcquisitionPlatformDbContext : DbContext
{
    /// <summary>
    /// 初始化一个新的 <see cref="DataAcquisitionPlatformDbContext"/> 实例。
    /// </summary>
    /// <param name="options">上下文配置。</param>
    public DataAcquisitionPlatformDbContext(DbContextOptions<DataAcquisitionPlatformDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// 获取采集点集合。
    /// </summary>
    public DbSet<CollectionPoint> CollectionPoints => Set<CollectionPoint>();

    /// <summary>
    /// 获取采集数据记录集合。
    /// </summary>
    public DbSet<CollectionDataRecord> CollectionDataRecords => Set<CollectionDataRecord>();

    /// <summary>
    /// 获取后台数据定义集合。
    /// </summary>
    public DbSet<ManagedDataDefinition> ManagedDataDefinitions => Set<ManagedDataDefinition>();

    /// <summary>
    /// 获取外部服务器配置集合。
    /// </summary>
    public DbSet<ServerConnection> ServerConnections => Set<ServerConnection>();

    /// <summary>
    /// 获取部门集合。
    /// </summary>
    public DbSet<Department> Departments => Set<Department>();

    /// <summary>
    /// 获取平台账号集合。
    /// </summary>
    public DbSet<AppUser> AppUsers => Set<AppUser>();

    /// <summary>
    /// 获取外部身份绑定集合。
    /// </summary>
    public DbSet<ExternalIdentity> ExternalIdentities => Set<ExternalIdentity>();

    /// <summary>
    /// 获取职能域集合。
    /// </summary>
    public DbSet<BusinessDomain> BusinessDomains => Set<BusinessDomain>();

    /// <summary>
    /// 获取域内页面集合。
    /// </summary>
    public DbSet<DomainPage> DomainPages => Set<DomainPage>();

    /// <summary>
    /// 获取部门域授权集合。
    /// </summary>
    public DbSet<DepartmentDomainGrant> DepartmentDomainGrants => Set<DepartmentDomainGrant>();

    /// <summary>
    /// 获取角色集合。
    /// </summary>
    public DbSet<Role> Roles => Set<Role>();

    /// <summary>
    /// 获取权限点集合。
    /// </summary>
    public DbSet<Permission> Permissions => Set<Permission>();

    /// <summary>
    /// 获取角色权限关联集合。
    /// </summary>
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    /// <summary>
    /// 获取用户角色关联集合。
    /// </summary>
    public DbSet<UserRole> UserRoles => Set<UserRole>();

    /// <summary>
    /// 获取审计日志集合。
    /// </summary>
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    /// <summary>
    /// 获取目录同步待处理项集合。
    /// </summary>
    public DbSet<DirectorySyncIssue> DirectorySyncIssues => Set<DirectorySyncIssue>();


    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DataAcquisitionPlatformDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
