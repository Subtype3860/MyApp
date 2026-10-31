using MyApp.Application.Models;
using Xunit;

namespace MyApp.Infrastructure.Tests;

/// <summary>Тесты определения рабочего состояния техники.</summary>
public sealed class VehicleOperationalStateTests
{
    /// <summary>Проверяет, что ожидание запчастей само по себе не означает простой техники.</summary>
    [Fact]
    public void Waiting_for_parts_does_not_imply_downtime() =>
        Assert.Equal("operating_with_defect", VehicleOperationalState.Resolve([("awaiting_parts", "operating")]));

    /// <summary>Проверяет учёт оставшихся неисправностей после завершения одного ремонта.</summary>
    [Fact]
    public void Completing_one_defect_does_not_hide_another() =>
        Assert.Equal("operating_with_defect", VehicleOperationalState.Resolve([
            ("repaired", "stopped"), ("faulty", "operating")]));

    /// <summary>Проверяет сохранение простоя при наличии хотя бы одной открытой останавливающей неисправности.</summary>
    [Fact]
    public void Any_open_stopping_defect_keeps_the_vehicle_stopped() =>
        Assert.Equal("stopped", VehicleOperationalState.Resolve([
            ("awaiting_parts", "operating"), ("faulty", "stopped")]));

    /// <summary>Проверяет исправность техники после устранения всех неисправностей.</summary>
    [Fact]
    public void All_completed_defects_make_the_vehicle_healthy() =>
        Assert.Equal("healthy", VehicleOperationalState.Resolve([("repaired", "stopped"), ("repaired", "operating")]));

    /// <summary>Проверяет исправность техники без неисправностей.</summary>
    [Fact]
    public void No_defects_means_healthy() => Assert.Equal("healthy", VehicleOperationalState.Resolve([]));
}
