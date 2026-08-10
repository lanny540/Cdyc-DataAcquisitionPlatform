using CdycDataAcquisitionPlatform.Core.Shared.Contracts;

namespace CdycDataAcquisitionPlatform.Presentation.BlazorWeb.Client.Services;

public interface IPlatformApiClient
{
    Task<DashboardOverviewDto> GetDashboardOverviewAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CollectionPointDto>> GetCollectionPointsAsync(CancellationToken cancellationToken = default);

    Task<CollectionPointDto> UpsertCollectionPointAsync(CollectionPointUpsertRequest request, CancellationToken cancellationToken = default);

    Task<bool> DeleteCollectionPointAsync(Guid id, CancellationToken cancellationToken = default);
}
