using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Application.Abstractions;
using MyApp.Application.Security;
using MyApp.Application.Storage;
using MyApp.Infrastructure.Db;
using MyApp.Infrastructure.Repositories;
using MyApp.Infrastructure.Security;
using MyApp.Infrastructure.Storage;
using Npgsql;

namespace MyApp.Infrastructure;

public static class InfrastructureRegistration
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString,
        JwtOptions jwtOptions,
        MediaStorageOptions mediaStorageOptions)
    {
        services.AddDbContext<AppDbContext>(
            options => options.UseNpgsql(connectionString));
        services.AddSingleton(NpgsqlDataSource.Create(connectionString));
        services.AddSingleton(jwtOptions);
        services.AddSingleton(mediaStorageOptions);
        services.AddSingleton<IMediaStorageService, MediaStorageService>();
        services.AddScoped<IMediaStorageAdministrationRepository, MediaStorageAdministrationRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IProfessionRepository, ProfessionRepository>();
        services.AddScoped<ITableViewRepository, TableViewRepository>();
        services.AddScoped<IResponsibleEmployeeRepository, ResponsibleEmployeeRepository>();
        services.AddScoped<IEmployeeSignatureRepository, EmployeeSignatureRepository>();
        services.AddScoped<ICsvFileRepository, CsvFileRepository>();
        services.AddScoped<IRequirementJournalRepository, RequirementJournalRepository>();
        services.AddScoped<IMaterialGroupRepository, MaterialGroupRepository>();
        services.AddScoped<IMaintenanceTemplateRepository, MaintenanceTemplateRepository>();
        services.AddScoped<IVehicleRepository, VehicleRepository>();
        services.AddScoped<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddScoped<ITokenProvider, JwtTokenProvider>();
        services.AddScoped<IDatabaseInitializer, DatabaseInitializer>();
        return services;
    }

    public static async Task InitializeDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var initializer = scope.ServiceProvider
            .GetRequiredService<IDatabaseInitializer>();
        await initializer.InitializeAsync(cancellationToken);
    }
}
