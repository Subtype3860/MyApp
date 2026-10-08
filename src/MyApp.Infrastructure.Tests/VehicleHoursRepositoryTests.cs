using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Application.Abstractions;
using MyApp.Application.DTO;
using MyApp.Infrastructure.Db;
using MyApp.Infrastructure.Repositories;
using Npgsql;

namespace MyApp.Infrastructure.Tests;

public sealed class VehicleHoursRepositoryTests
{
    [Xunit.Fact]
    public void Hours_port_resolves_independently_and_facade_uses_same_port()
    {
        var services = new ServiceCollection();
        using var dataSource = NpgsqlDataSource.Create(
            "Host=localhost;Database=model_tests;Username=tests;Password=unused");
        services.AddSingleton(dataSource);
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(
            "Host=localhost;Database=model_tests;Username=tests;Password=unused"));
        services.AddScoped<IVehiclePartsRepository, VehiclePartsRepository>();
        services.AddScoped<IVehiclePurchaseRepository, VehiclePurchaseRepository>();
        services.AddScoped<IVehicleHoursRepository, VehicleHoursRepository>();
        services.AddScoped<IVehicleWorkRepository, VehicleWorkRepository>();
        services.AddScoped<IVehicleRepository, VehicleRepository>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var hours = scope.ServiceProvider.GetRequiredService<IVehicleHoursRepository>();
        Xunit.Assert.IsType<VehicleHoursRepository>(hours);
        Xunit.Assert.IsType<VehicleRepository>(
            scope.ServiceProvider.GetRequiredService<IVehicleRepository>());
        Xunit.Assert.Same(hours,
            scope.ServiceProvider.GetRequiredService<IVehicleHoursRepository>());
    }
}
