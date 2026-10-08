using MyApp.Application.Abstractions;
using MyApp.Infrastructure.Repositories;

namespace MyApp.Infrastructure.Tests;

public sealed class VehicleRepositoryPortTests
{
    [Xunit.Theory]
    [Xunit.InlineData(typeof(IVehicleQueryRepository))]
    [Xunit.InlineData(typeof(IVehiclePurchaseRepository))]
    [Xunit.InlineData(typeof(IVehicleDefectRepository))]
    [Xunit.InlineData(typeof(IVehicleMediaRepository))]
    [Xunit.InlineData(typeof(IVehicleHoursRepository))]
    [Xunit.InlineData(typeof(IVehicleWorkRepository))]
    [Xunit.InlineData(typeof(IVehiclePartsRepository))]
    [Xunit.InlineData(typeof(IVehicleEntryRepository))]
    public void Aggregate_repository_implements_each_feature_port(Type port)
    {
        Xunit.Assert.True(port.IsAssignableFrom(typeof(IVehicleRepository)));
        Xunit.Assert.True(port.IsAssignableFrom(typeof(VehicleRepository)));
    }
}
