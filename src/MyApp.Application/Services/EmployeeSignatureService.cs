using MyApp.Application.Abstractions;
using MyApp.Application.Common;
using MyApp.Application.DTO;

namespace MyApp.Application.Services;

/// <summary>Прикладные операции подписей сотрудников.</summary>
public sealed class EmployeeSignatureService(
    IEmployeeSignatureRepository repository) : IEmployeeSignatureService
{
    /// <summary>Возвращает данные подписи сотрудника.</summary>
    public async Task<ServiceResult<EmployeeSignatureResponse>> GetAsync(
        EmployeeSignatureKey employee,
        CancellationToken cancellationToken)
    {
        var signature = await repository.GetAsync(
            Normalize(employee),
            cancellationToken);
        return signature is null
            ? ServiceResult<EmployeeSignatureResponse>.NotFound()
            : ServiceResult<EmployeeSignatureResponse>.Success(signature);
    }

    /// <summary>Сохраняет подпись сотрудника.</summary>
    public async Task<ServiceResult<bool>> SaveAsync(
        EmployeeSignatureKey employee,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(employee);
        if (string.IsNullOrEmpty(normalized.LastName) ||
            string.IsNullOrEmpty(normalized.FirstName))
        {
            return ServiceResult<bool>.Validation(
                new Dictionary<string, string[]>
                {
                    [nameof(employee)] = ["Не удалось определить работника."]
                });
        }

        await repository.SaveAsync(
            normalized,
            content,
            contentType,
            cancellationToken);
        return ServiceResult<bool>.Success(true);
    }

    /// <summary>Удаляет подпись сотрудника.</summary>
    public async Task<ServiceResult<bool>> DeleteAsync(
        EmployeeSignatureKey employee,
        CancellationToken cancellationToken)
    {
        var deleted = await repository.DeleteAsync(
            Normalize(employee),
            cancellationToken);
        return deleted
            ? ServiceResult<bool>.Success(true)
            : ServiceResult<bool>.NotFound();
    }

    /// <summary>Нормализует ключ сотрудника перед поиском или сохранением подписи.</summary>
    private static EmployeeSignatureKey Normalize(EmployeeSignatureKey employee) =>
        new(
            employee.LastName.Trim(),
            employee.FirstName.Trim(),
            employee.Patronymic.Trim());
}
