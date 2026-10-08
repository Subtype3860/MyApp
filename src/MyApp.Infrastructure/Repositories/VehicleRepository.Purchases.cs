using MyApp.Application.DTO;

namespace MyApp.Infrastructure.Repositories;

public sealed partial class VehicleRepository
{
    public Task<Guid> AddPurchaseAsync(
        Guid vehicleId, VehiclePurchaseRequest request, Guid createdBy,
        CancellationToken cancellationToken) =>
        purchaseRepository.AddPurchaseAsync(vehicleId, request, createdBy, cancellationToken);

    public Task<IReadOnlyList<VehiclePurchaseResponse>> GetPurchasesAsync(
        Guid vehicleId, DateOnly? from, DateOnly? to,
        CancellationToken cancellationToken) =>
        purchaseRepository.GetPurchasesAsync(vehicleId, from, to, cancellationToken);
}
