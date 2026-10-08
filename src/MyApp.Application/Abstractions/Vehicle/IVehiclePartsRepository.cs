using MyApp.Application.DTO;

namespace MyApp.Application.Abstractions;

public interface IVehiclePartsRepository
{
    Task<bool> UpdatePartsRequestAsync(
        Guid workId,
        VehiclePartsRequest request,
        CancellationToken cancellationToken);

    Task<bool> DeletePartsRequestAsync(Guid workId, CancellationToken cancellationToken);

    Task<VehicleRequestFileContent?> GetPartsRequestFileAsync(
        Guid workId,
        CancellationToken cancellationToken);
}
