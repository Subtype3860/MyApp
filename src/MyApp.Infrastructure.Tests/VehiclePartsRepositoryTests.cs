using Microsoft.Extensions.DependencyInjection;
using MyApp.Application.Abstractions;
using MyApp.Infrastructure.Repositories;
using Npgsql;

namespace MyApp.Infrastructure.Tests;

public sealed class VehiclePartsRepositoryTests
{
    [Xunit.Fact]
    public void Parts_port_resolves_to_independent_repository_without_connection()
    {
        var services = new ServiceCollection();
        using var dataSource = NpgsqlDataSource.Create(
            "Host=localhost;Database=model_tests;Username=tests;Password=unused");
        services.AddSingleton(dataSource);
        services.AddScoped<IVehiclePartsRepository, VehiclePartsRepository>();
        services.AddScoped<IVehicleRepository, VehicleRepository>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Xunit.Assert.IsType<VehiclePartsRepository>(
            scope.ServiceProvider.GetRequiredService<IVehiclePartsRepository>());
        Xunit.Assert.IsType<VehicleRepository>(
            scope.ServiceProvider.GetRequiredService<IVehicleRepository>());
    }
}
