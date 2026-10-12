using MyApp.Domain.Entities;
using Xunit;

namespace MyApp.Infrastructure.Tests;

public sealed class VehicleOperationalStateTests
{
    [Fact]
    public void Waiting_for_parts_does_not_imply_downtime() =>
        Assert.Equal("operating_with_defect", VehicleOperationalState.Resolve([("awaiting_parts", "operating")]));

    [Fact]
    public void Completing_one_defect_does_not_hide_another() =>
        Assert.Equal("operating_with_defect", VehicleOperationalState.Resolve([
            ("repaired", "stopped"), ("faulty", "operating")]));

    [Fact]
    public void Any_open_stopping_defect_keeps_the_vehicle_stopped() =>
        Assert.Equal("stopped", VehicleOperationalState.Resolve([
            ("awaiting_parts", "operating"), ("faulty", "stopped")]));

    [Fact]
    public void All_completed_defects_make_the_vehicle_healthy() =>
        Assert.Equal("healthy", VehicleOperationalState.Resolve([("repaired", "stopped"), ("repaired", "operating")]));

    [Fact]
    public void No_defects_means_healthy() => Assert.Equal("healthy", VehicleOperationalState.Resolve([]));
}
