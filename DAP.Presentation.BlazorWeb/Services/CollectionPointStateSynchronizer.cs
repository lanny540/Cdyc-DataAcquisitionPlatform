using DAP.Core.Domain.Entities;
using DAP.Infrastructure.DataAccess.Persistence;
using DAP.Infrastructure.DataAccess.Repositories;

namespace DAP.Presentation.BlazorWeb.Services;

/// <summary>
/// 采集点运行状态的统一写入口。
///
/// **为什么不复用 <c>IDataAcquisitionPlatformService.UpsertCollectionPointAsync</c>**：
/// 那条路径属于「管理员修改配置」，要求 <c>action.system.manage</c> 权限点并写审计，
/// 由 <c>ServiceAuditGuard</c> 把关。而采集执行是**数据面**动作——定时调度器没有操作者，
/// 手动采集也只是把上游读数落库，两者都不该、也无法提供配置权限点。
/// 早先执行服务直接调配置写入口，结果是每次采集都在守卫处抛
/// 「会话没有可识别的操作者」：数据一条都进不来，审计表却被每 5 秒刷一批失败记录。
///
/// 因此这里只做「把定义上的名称/协议/地址/启停同步到对应采集点」，
/// 不涉及任何授权判定；需要授权的配置修改仍然只走上面那条路径。
/// </summary>
public sealed class CollectionPointStateSynchronizer(
    ICollectionPointRepository repository,
    DataAcquisitionPlatformDbContext db)
{
    /// <summary>
    /// 按定义同步采集点的展示字段与运行状态；不存在时创建。
    /// </summary>
    /// <param name="code">采集点编码（与数据定义编码一致）。</param>
    /// <param name="name">名称。</param>
    /// <param name="protocol">协议，取采集方式。</param>
    /// <param name="endpoint">端点，取连接地址。</param>
    /// <param name="isEnabled">是否启用。</param>
    /// <param name="communicationStatus">通信状态：在线 / 离线 / 停用。</param>
    /// <param name="lastError">最近一次错误，成功时为空。</param>
    /// <param name="updatedAt">更新时间。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>同步后的采集点。</returns>
    public async Task<CollectionPoint> SyncAsync(
        string code,
        string name,
        string protocol,
        string endpoint,
        bool isEnabled,
        string communicationStatus,
        string? lastError,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        CollectionPoint? point = await repository.GetByCodeAsync(code, cancellationToken);

        if (point is null)
        {
            point = new CollectionPoint
            {
                Id = Guid.NewGuid(),
                Code = code,
                Name = name,
                Protocol = protocol,
                Endpoint = endpoint,
                Source = "Server",
                UpdatedAt = updatedAt
            };

            await repository.AddAsync(point, cancellationToken);
        }

        point.Name = name;
        point.Protocol = protocol;
        point.Endpoint = endpoint;
        point.IsEnabled = isEnabled;
        // 停用的点位状态固定为「停用」：写「离线」会让仪表板把「人工关闭」算成「故障」。
        point.CommunicationStatus = isEnabled ? communicationStatus : "停用";
        point.LastError = isEnabled ? lastError : null;
        point.Source = "Server";
        point.UpdatedAt = updatedAt;

        await db.SaveChangesAsync(cancellationToken);
        return point;
    }
}
