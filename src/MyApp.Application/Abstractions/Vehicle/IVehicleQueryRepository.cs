using MyApp.Application.DTO;

namespace MyApp.Application.Abstractions;

public interface IVehicleQueryRepository
{
    Task<IReadOnlyList<VehicleResponse>> GetVehiclesAsync(
        CancellationToken cancellationToken);

    Task<VehicleJournalResponse?> GetJournalAsync(
        Guid vehicleId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken);

    Task<bool> VehicleExistsAsync(Guid vehicleId, CancellationToken cancellationToken);
}
