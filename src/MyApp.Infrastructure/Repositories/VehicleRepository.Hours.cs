using MyApp.Application.DTO;

namespace MyApp.Infrastructure.Repositories;

public sealed partial class VehicleRepository
{
    public Task<Guid> AddHoursAsync(
        Guid vehicleId, VehicleHoursRequest request,
        Guid createdBy, CancellationToken cancellationToken) =>
        hoursRepository.AddHoursAsync(vehicleId, request, createdBy, cancellationToken);

    public Task ImportHoursAsync(
        DateOnly readingDate, IReadOnlyList<VehicleHoursImportItem> items,
        Guid createdBy, CancellationToken cancellationToken) =>
        hoursRepository.ImportHoursAsync(readingDate, items, createdBy, cancellationToken);

    private Task<IReadOnlyList<VehicleHoursResponse>> GetHoursAsync(
        Guid vehicleId, DateOnly? from, DateOnly? to,
        CancellationToken cancellationToken) =>
        hoursRepository.GetHoursAsync(vehicleId, from, to, cancellationToken);
}
