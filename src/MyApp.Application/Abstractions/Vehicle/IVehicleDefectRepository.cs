using MyApp.Application.DTO;

namespace MyApp.Application.Abstractions;

public interface IVehicleDefectRepository
{
    Task<Guid> AddDefectAsync(
        Guid vehicleId,
        VehicleDefectRequest request,
        Guid createdBy,
        CancellationToken cancellationToken);

    Task<bool> DefectExistsAsync(Guid defectId, CancellationToken cancellationToken);

    Task<bool> DefectBelongsToVehicleAsync(
        Guid defectId,
        Guid vehicleId,
        CancellationToken cancellationToken);

    Task<bool> ClaimDefectAsync(
        Guid defectId,
        Guid userId,
        CancellationToken cancellationToken);

    Task<bool> IsDefectCreatorAsync(
        Guid defectId, Guid userId, CancellationToken cancellationToken);

    Task<Guid?> CompleteDefectAsync(
        Guid defectId,
        VehicleWorkRequest request,
        Guid performedBy,
        bool administrator,
        CancellationToken cancellationToken);
}
