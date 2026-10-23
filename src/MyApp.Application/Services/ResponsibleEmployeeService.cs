using MyApp.Application.Abstractions;
using MyApp.Application.Common;
using MyApp.Application.DTO;

namespace MyApp.Application.Services;

/// <summary>Прикладные операции выбора ответственных сотрудников.</summary>
public sealed class ResponsibleEmployeeService(
    IResponsibleEmployeeRepository repository) : IResponsibleEmployeeService
{
    /// <summary>Возвращает данные ответственного сотрудника.</summary>
    public async Task<ServiceResult<IReadOnlyList<ResponsibleEmployeeResponse>>> GetAsync(
        string sourceTable,
        CancellationToken cancellationToken)
    {
        var profession = GetProfession(sourceTable);
        if (profession is null)
        {
            return ServiceResult<IReadOnlyList<ResponsibleEmployeeResponse>>
                .BadRequest("Неизвестный источник остатков.");
        }

        return ServiceResult<IReadOnlyList<ResponsibleEmployeeResponse>>.Success(
            await repository.GetByProfessionAsync(
                profession,
                cancellationToken));
    }

    /// <summary>Проверяет выбранных ответственных и подготавливает их данные для документа.</summary>
    public async Task<ServiceResult<ResponsibleEmployeeResponse>> ResolveAsync(
        string sourceTable,
        ResponsibleEmployeeSelection employee,
        CancellationToken cancellationToken)
    {
        var profession = GetProfession(sourceTable);
        if (profession is null)
        {
            return ServiceResult<ResponsibleEmployeeResponse>.BadRequest(
                "Неизвестный источник остатков.");
        }

        if (!await repository.ExistsAsync(
            employee,
            profession,
            cancellationToken))
        {
            return ServiceResult<ResponsibleEmployeeResponse>.Validation(
                new Dictionary<string, string[]>
                {
                    [nameof(employee)] =
                        ["Выбранное ответственное лицо не найдено."]
                });
        }

        return ServiceResult<ResponsibleEmployeeResponse>.Success(
            new ResponsibleEmployeeResponse(
                employee.FirstName.Trim(),
                employee.Patronymic.Trim(),
                employee.LastName.Trim(),
                profession));
    }

    /// <summary>Определяет профессию, соответствующую роли ответственного в документе.</summary>
    private static string? GetProfession(string sourceTable) =>
        sourceTable.ToLowerInvariant() switch
        {
            "v_full_ost" or "full_ost" => "Кладовщик",
            "v_meh_ost" or "meh_ost" => "Старший механик",
            _ => null
        };
}
