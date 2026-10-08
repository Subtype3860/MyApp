namespace MyApp.Application.Abstractions;

// Compatibility facade: existing services can keep using IVehicleRepository
// while new services depend on the smaller feature-specific data access ports.
public interface IVehicleRepository :
    IVehicleQueryRepository,
    IVehiclePurchaseRepository,
    IVehicleDefectRepository,
    IVehicleMediaRepository,
    IVehicleHoursRepository,
    IVehicleWorkRepository,
    IVehiclePartsRepository,
    IVehicleEntryRepository
{
}
