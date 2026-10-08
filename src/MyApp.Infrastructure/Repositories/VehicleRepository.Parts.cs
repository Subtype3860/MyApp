using MyApp.Application.DTO;

namespace MyApp.Infrastructure.Repositories;

public sealed partial class VehicleRepository
{
    public Task<bool> UpdatePartsRequestAsync(
        Guid workId, VehiclePartsRequest request, CancellationToken cancellationToken) =>
        partsRepository.UpdatePartsRequestAsync(workId, request, cancellationToken);

    public Task<VehicleRequestFileContent?> GetPartsRequestFileAsync(
        Guid workId, CancellationToken cancellationToken) =>
        partsRepository.GetPartsRequestFileAsync(workId, cancellationToken);

    public Task<bool> DeletePartsRequestAsync(
        Guid workId, CancellationToken cancellationToken) =>
        partsRepository.DeletePartsRequestAsync(workId, cancellationToken);
}
