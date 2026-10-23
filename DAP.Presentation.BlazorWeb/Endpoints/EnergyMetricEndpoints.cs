using DAP.Core.Shared.Contracts;
using DAP.Presentation.BlazorWeb.Services;

namespace DAP.Presentation.BlazorWeb.Endpoints;

/// <summary>
/// 提供班组能耗指标相关 API 端点映射。
/// </summary>
public static class EnergyMetricEndpoints
{
    /// <summary>
    /// 映射班组能耗指标相关 API 端点。
    /// </summary>
    /// <param name="endpoints">端点路由构建器。</param>
    /// <returns>当前端点路由构建器。</returns>
    public static IEndpointRouteBuilder MapEnergyMetricEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/energy-metrics").WithTags("EnergyMetrics");

        group.MapGet(
                "/",
                async (IEnergyMetricService service, CancellationToken cancellationToken) =>
                {
                    IReadOnlyCollection<EnergyMetricDescriptorDto> metrics =
                        await service.GetMetricsAsync(cancellationToken);

                    return Results.Ok(metrics);
                })
            .WithName("GetEnergyMetrics");

        group.MapGet(
                "/dashboard",
                async (int? trendDays, string? teamCode, IEnergyMetricService service,
                    CancellationToken cancellationToken) =>
                {
                    EnergyDashboardDto dashboard =
                        await service.GetDashboardAsync(trendDays ?? 7, teamCode, cancellationToken);

                    return Results.Ok(dashboard);
                })
            .WithName("GetEnergyDashboard");

        group.MapGet(
                "/history",
                async (string? teamCode, string? granularity, DateOnly? from, DateOnly? to,
                    IEnergyMetricService service, CancellationToken cancellationToken) =>
                {
                    DateOnly today = DateOnly.FromDateTime(DateTime.Now);

                    EnergyHistoryDto history = await service.GetHistoryAsync(
                        teamCode ?? "TEAM-甲",
                        granularity ?? "day",
                        from ?? today.AddDays(-29),
                        to ?? today,
                        cancellationToken);

                    return Results.Ok(history);
                })
            .WithName("GetEnergyHistory");

        group.MapGet(
                "/comparison",
                async (string? granularity, DateOnly? date, IEnergyMetricService service,
                    CancellationToken cancellationToken) =>
                {
                    EnergyComparisonDto comparison = await service.GetComparisonAsync(
                        granularity ?? "day",
                        date ?? DateOnly.FromDateTime(DateTime.Now),
                        cancellationToken);

                    return Results.Ok(comparison);
                })
            .WithName("GetEnergyComparison");

        return endpoints;
    }
}
