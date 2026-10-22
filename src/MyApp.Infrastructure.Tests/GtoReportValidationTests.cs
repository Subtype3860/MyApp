using System.Text.Json;
using MyApp.API.Controllers;
using Xunit;

namespace MyApp.Infrastructure.Tests;

public sealed class GtoReportValidationTests
{
    private static GtoReportsController.SaveRequest Request(object? ktg, object? version = null, string date = "2026-10-22") =>
        new(0, JsonSerializer.SerializeToElement(new {
            version = version ?? 1, date, shift = "1", organisation = "Организация", site = "Участок", mechanic = "Механик",
            rows = new[] { new { vehicle = new { id = Guid.NewGuid().ToString() }, forecast = "healthy", ktg, maintenance = "ТО-2", note = "" } }
        }));

    [Theory]
    [InlineData(0)]
    [InlineData(75.5)]
    [InlineData(100)]
    public void AcceptsPercentWithinRange(double ktg) => Assert.True(GtoReportsController.ValidSnapshot(new(2026,10,22),1,Request(ktg)));

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void RejectsInvalidPercent(double ktg) => Assert.False(GtoReportsController.ValidSnapshot(new(2026,10,22),1,Request(ktg)));

    [Fact]
    public void RejectsWrongKeyOrVersionWithoutThrowing()
    {
        Assert.False(GtoReportsController.ValidSnapshot(new(2026,10,23),1,Request(50)));
        Assert.False(GtoReportsController.ValidSnapshot(new(2026,10,22),2,Request(50)));
        Assert.False(GtoReportsController.ValidSnapshot(new(2026,10,22),1,Request(50,"1")));
        Assert.True(GtoReportsController.ValidSnapshot(new(2026,10,22),1,Request("")));
        Assert.True(GtoReportsController.ValidSnapshot(new(2026,10,22),1,Request(null)));
        Assert.False(GtoReportsController.ValidSnapshot(new(2026,10,22),1,Request("NaN")));
    }
}
