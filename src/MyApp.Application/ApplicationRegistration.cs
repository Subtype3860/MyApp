using Microsoft.Extensions.DependencyInjection;
using MyApp.Application.Services;

namespace MyApp.Application;

/// <summary>Регистрация сервисов прикладного слоя в контейнере зависимостей.</summary>
public static class ApplicationRegistration
{
    /// <summary>Регистрирует прикладные сервисы в контейнере зависимостей.</summary>
    public static IServiceCollection AddApplication(
        this IServiceCollection services)
    {
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IProfessionService, ProfessionService>();
        services.AddScoped<ITableViewService, TableViewService>();
        services.AddScoped<IComponentDocumentService, ComponentDocumentService>();
        services.AddScoped<IResponsibleEmployeeService, ResponsibleEmployeeService>();
        services.AddScoped<IEmployeeSignatureService, EmployeeSignatureService>();
        services.AddScoped<ICsvFileService, CsvFileService>();
        services.AddScoped<IRequirementJournalService, RequirementJournalService>();
        services.AddScoped<IMaterialGroupService, MaterialGroupService>();
        services.AddScoped<IMaintenanceTemplateService, MaintenanceTemplateService>();
        services.AddScoped<IVehicleService, VehicleService>();
        return services;
    }
}
