using MyApp.Application.Common;
using MyApp.Application.DTO;

namespace MyApp.Application.Services;

/// <summary>Прикладные операции групп материалов.</summary>
public interface IMaterialGroupService
{
    /// <summary>Возвращает список групп материалов.</summary>
    Task<IReadOnlyList<MaterialGroupResponse>> GetAllAsync(
        CancellationToken cancellationToken);

    /// <summary>Возвращает соответствия материалов их группам.</summary>
    Task<ServiceResult<IReadOnlyList<MaterialGroupMappingResponse>>> GetMappingsAsync(
        string sourceTable,
        CancellationToken cancellationToken);

    /// <summary>Создаёт группу материалов.</summary>
    Task<ServiceResult<Guid>> CreateGroupAsync(
        MaterialGroupRequest request,
        CancellationToken cancellationToken);

    /// <summary>Включает материал в выбранную группу.</summary>
    Task<ServiceResult<Guid>> AddItemAsync(
        Guid groupId,
        MaterialGroupItemRequest request,
        CancellationToken cancellationToken);

    /// <summary>Удаляет группу материалов.</summary>
    Task<ServiceResult<Guid>> DeleteGroupAsync(
        Guid id,
        CancellationToken cancellationToken);

    /// <summary>Исключает материал из группы.</summary>
    Task<ServiceResult<Guid>> DeleteItemAsync(
        Guid id,
        CancellationToken cancellationToken);

}
