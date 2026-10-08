using MyApp.Application.DTO;

namespace MyApp.Infrastructure.Repositories;

public sealed partial class VehicleRepository
{
    public Task<Guid> AddWorkAsync(
        Guid vehicleId, VehicleWorkRequest request,
        Guid createdBy, CancellationToken cancellationToken) =>
        workRepository.AddWorkAsync(vehicleId, request, createdBy, cancellationToken);

    public Task<bool> WorkExistsAsync(
        Guid workId, CancellationToken cancellationToken) =>
        workRepository.WorkExistsAsync(workId, cancellationToken);

    public Task<bool> IsWorkPerformerAsync(
        Guid workId, Guid userId, CancellationToken cancellationToken) =>
        workRepository.IsWorkPerformerAsync(workId, userId, cancellationToken);

    public Task<IReadOnlyList<VehicleWorkResponse>> GetWorksAsync(
        Guid vehicleId, DateOnly? from, DateOnly? to,
        CancellationToken cancellationToken) =>
        workRepository.GetWorksAsync(vehicleId, from, to, cancellationToken);
}
