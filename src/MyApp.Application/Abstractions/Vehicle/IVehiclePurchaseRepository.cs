using MyApp.Application.DTO;

namespace MyApp.Application.Abstractions;

public interface IVehiclePurchaseRepository
{
    Task<Guid> AddPurchaseAsync(
        Guid vehicleId,
        VehiclePurchaseRequest request,
        Guid createdBy,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<VehiclePurchaseResponse>> GetPurchasesAsync(
        Guid vehicleId, DateOnly? from, DateOnly? to,
        CancellationToken cancellationToken);
}
