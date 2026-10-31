using MyApp.Application.DTO;

namespace MyApp.Application.Abstractions;

/// <summary>Доступ к данным подписей сотрудников.</summary>
public interface IEmployeeSignatureRepository
{
    /// <summary>Возвращает данные подписи сотрудника.</summary>
    Task<EmployeeSignatureResponse?> GetAsync(
        EmployeeSignatureKey employee,
        CancellationToken cancellationToken);

    /// <summary>Сохраняет подпись сотрудника.</summary>
    Task SaveAsync(
        EmployeeSignatureKey employee,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken);

    /// <summary>Удаляет подпись сотрудника.</summary>
    Task<bool> DeleteAsync(
        EmployeeSignatureKey employee,
        CancellationToken cancellationToken);
}
