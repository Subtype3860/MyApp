using MyApp.Application.DTO;

namespace MyApp.Application.Abstractions;

public interface IVehicleRepository
{
    Task<IReadOnlyList<VehicleResponse>> GetVehiclesAsync(
        CancellationToken cancellationToken);

    Task<VehicleJournalResponse?> GetJournalAsync(
        Guid vehicleId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken);

    Task<bool> VehicleExistsAsync(Guid vehicleId, CancellationToken cancellationToken);

    Task<Guid> AddPurchaseAsync(
        Guid vehicleId,
        VehiclePurchaseRequest request,
        Guid createdBy,
        CancellationToken cancellationToken);

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

    Task<Guid> AddWorkAsync(
        Guid vehicleId,
        VehicleWorkRequest request,
        Guid createdBy,
        CancellationToken cancellationToken);

    Task<Guid?> CompleteDefectAsync(
        Guid defectId,
        VehicleWorkRequest request,
        Guid performedBy,
        bool administrator,
        CancellationToken cancellationToken);

    Task<bool> WorkExistsAsync(Guid workId, CancellationToken cancellationToken);
    Task<bool> IsWorkPerformerAsync(
        Guid workId, Guid userId, CancellationToken cancellationToken);
    Task<bool> UpdatePartsRequestAsync(
        Guid workId,
        VehiclePartsRequest request,
        CancellationToken cancellationToken);
    Task<bool> DeletePartsRequestAsync(Guid workId, CancellationToken cancellationToken);
    Task<VehicleRequestFileContent?> GetPartsRequestFileAsync(
        Guid workId,
        CancellationToken cancellationToken);

    Task<bool> DeleteEntryAsync(
        string category,
        Guid id,
        CancellationToken cancellationToken);
}
