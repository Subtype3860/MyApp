using MyApp.Application.Common;
using MyApp.Application.DTO;

namespace MyApp.Application.Services;

/// <summary>Прикладные операции справочника профессий.</summary>
public interface IProfessionService
{
    /// <summary>Возвращает список профессий.</summary>
    Task<IReadOnlyList<ProfessionResponse>> GetAllAsync(
        CancellationToken cancellationToken);

    /// <summary>Создаёт профессию.</summary>
    Task<ServiceResult<ProfessionResponse>> CreateAsync(
        ProfessionRequest request,
        CancellationToken cancellationToken);

    /// <summary>Обновляет данные профессии.</summary>
    Task<ServiceResult<ProfessionResponse>> UpdateAsync(
        Guid id,
        ProfessionRequest request,
        CancellationToken cancellationToken);

    /// <summary>Удаляет профессию.</summary>
    Task<ServiceResult<Guid>> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken);
}
