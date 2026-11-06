using MyApp.Application.Common;
using MyApp.Application.DTO;

namespace MyApp.Application.Services;

/// <summary>Прикладные операции выбора ответственных сотрудников.</summary>
public interface IResponsibleEmployeeService
{
    /// <summary>Возвращает данные ответственного сотрудника.</summary>
    Task<ServiceResult<IReadOnlyList<ResponsibleEmployeeResponse>>> GetAsync(
        string sourceTable,
        CancellationToken cancellationToken);

    /// <summary>Проверяет выбранного ответственного и возвращает его данные для документа.</summary>
    Task<ServiceResult<ResponsibleEmployeeResponse>> ResolveAsync(
        string sourceTable,
        ResponsibleEmployeeSelection employee,
        CancellationToken cancellationToken);
}
