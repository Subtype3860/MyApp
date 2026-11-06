using MyApp.Application.DTO;

namespace MyApp.Application.Abstractions;

/// <summary>Доступ к данным выбора ответственных сотрудников.</summary>
public interface IResponsibleEmployeeRepository
{
    /// <summary>Возвращает сотрудников указанной профессии.</summary>
    Task<IReadOnlyList<ResponsibleEmployeeResponse>> GetByProfessionAsync(
        string profession,
        CancellationToken cancellationToken);

    /// <summary>Проверяет наличие ответственного сотрудника.</summary>
    Task<bool> ExistsAsync(
        ResponsibleEmployeeSelection employee,
        string profession,
        CancellationToken cancellationToken);
}
