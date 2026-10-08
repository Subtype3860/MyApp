using MyApp.Application.DTO;

namespace MyApp.Infrastructure.Repositories;

public sealed partial class VehicleRepository
{
    public Task<Guid> AddDefectAsync(
        Guid vehicleId, VehicleDefectRequest request,
        Guid createdBy, CancellationToken cancellationToken) =>
        defectRepository.AddDefectAsync(vehicleId, request, createdBy, cancellationToken);

    public Task<bool> DefectExistsAsync(
        Guid defectId, CancellationToken cancellationToken) =>
        defectRepository.DefectExistsAsync(defectId, cancellationToken);

    public Task<bool> DefectBelongsToVehicleAsync(
        Guid defectId, Guid vehicleId, CancellationToken cancellationToken) =>
        defectRepository.DefectBelongsToVehicleAsync(defectId, vehicleId, cancellationToken);

    public Task<bool> ClaimDefectAsync(
        Guid defectId, Guid userId, CancellationToken cancellationToken) =>
        defectRepository.ClaimDefectAsync(defectId, userId, cancellationToken);

    public Task<bool> IsDefectCreatorAsync(
        Guid defectId, Guid userId, CancellationToken cancellationToken) =>
        defectRepository.IsDefectCreatorAsync(defectId, userId, cancellationToken);

    public Task<Guid?> CompleteDefectAsync(
        Guid defectId, VehicleWorkRequest request,
        Guid performedBy, bool administrator,
        CancellationToken cancellationToken) =>
        defectRepository.CompleteDefectAsync(
            defectId, request, performedBy, administrator, cancellationToken);

    public Task<IReadOnlyList<VehicleDefectResponse>> GetDefectsAsync(
        Guid vehicleId, DateOnly? from, DateOnly? to,
        CancellationToken cancellationToken) =>
        defectRepository.GetDefectsAsync(vehicleId, from, to, cancellationToken);
}
