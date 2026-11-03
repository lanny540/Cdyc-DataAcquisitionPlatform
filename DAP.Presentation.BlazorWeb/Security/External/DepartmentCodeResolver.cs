using DAP.Infrastructure.DataAccess.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DAP.Presentation.BlazorWeb.Security.External;

/// <summary>
/// 按外部编码匹配平台部门。LDAP 的 <c>department</c> 属性与 OIDC 的组名都走这一条路径，
/// 保证「同一套 <c>departments.external_code</c>」是唯一映射入口。
/// </summary>
public sealed class DepartmentCodeResolver(DataAcquisitionPlatformDbContext db)
{
    /// <summary>
    /// 解析部门标识。匹配不到时返回 null——不自动建部门：
    /// 目录里的脏数据会让组织树膨胀，而且新建的部门没有任何域授权，
    /// 只会把「看不到数据」从显式问题变成隐式问题。
    /// </summary>
    /// <param name="externalCode">外部编码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>部门标识。</returns>
    public async Task<Guid?> ResolveAsync(string? externalCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(externalCode))
        {
            return null;
        }

        var code = externalCode.Trim();

        return await db.Departments
            .Where(department => department.ExternalCode == code)
            .Select(department => (Guid?)department.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
