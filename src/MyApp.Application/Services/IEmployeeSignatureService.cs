using MyApp.Application.Common;
using MyApp.Application.DTO;

namespace MyApp.Application.Services;

/// <summary>Прикладные операции подписей сотрудников.</summary>
public interface IEmployeeSignatureService
{
    /// <summary>Возвращает данные подписи сотрудника.</summary>
    Task<ServiceResult<EmployeeSignatureResponse>> GetAsync(
        EmployeeSignatureKey employee,
        CancellationToken cancellationToken);

    /// <summary>Сохраняет подпись сотрудника.</summary>
    Task<ServiceResult<bool>> SaveAsync(
        EmployeeSignatureKey employee,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken);

    /// <summary>Удаляет подпись сотрудника.</summary>
    Task<ServiceResult<bool>> DeleteAsync(
        EmployeeSignatureKey employee,
        CancellationToken cancellationToken);
}
