using MyApp.Application.DTO;

namespace MyApp.Application.Abstractions;

public interface IVehicleWorkRepository
{
    Task<Guid> AddWorkAsync(
        Guid vehicleId,
        VehicleWorkRequest request,
        Guid createdBy,
        CancellationToken cancellationToken);

    Task<bool> WorkExistsAsync(Guid workId, CancellationToken cancellationToken);

    Task<bool> IsWorkPerformerAsync(
        Guid workId, Guid userId, CancellationToken cancellationToken);
}
