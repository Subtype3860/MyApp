using MyApp.Application.DTO;

namespace MyApp.Application.Abstractions;

public interface IVehicleHoursRepository
{
    Task<Guid> AddHoursAsync(
        Guid vehicleId,
        VehicleHoursRequest request,
        Guid createdBy,
        CancellationToken cancellationToken);

    Task ImportHoursAsync(
        DateOnly readingDate,
        IReadOnlyList<VehicleHoursImportItem> items,
        Guid createdBy,
        CancellationToken cancellationToken);
}
