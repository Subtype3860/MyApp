using MyApp.Application.DTO;

namespace MyApp.Application.Abstractions;

public interface IVehicleEntryRepository
{
    Task<bool> DeleteEntryAsync(
        string category,
        Guid id,
        CancellationToken cancellationToken);
}
