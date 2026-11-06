using MyApp.Application.DTO;

namespace MyApp.Application.Abstractions;

/// <summary>Доступ к данным групп материалов.</summary>
public interface IMaterialGroupRepository
{
    /// <summary>Возвращает список групп материалов.</summary>
    Task<IReadOnlyList<MaterialGroupResponse>> GetAllAsync(
        CancellationToken cancellationToken);

    /// <summary>Возвращает соответствия материалов их группам.</summary>
    Task<IReadOnlyList<MaterialGroupMappingResponse>> GetMappingsAsync(
        string sourceTable,
        CancellationToken cancellationToken);

    /// <summary>Проверяет существование группы материалов.</summary>
    Task<bool> GroupExistsAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Проверяет, занято ли название группы материалов.</summary>
    Task<bool> GroupNameExistsAsync(
        string name,
        CancellationToken cancellationToken);

    /// <summary>Проверяет наличие материала в доступном источнике складских данных.</summary>
    Task<bool> MaterialExistsAsync(
        string sourceTable,
        string materialName,
        CancellationToken cancellationToken);

    /// <summary>Проверяет, включён ли материал в группу.</summary>
    Task<bool> MaterialIsAssignedAsync(
        string sourceTable,
        string materialName,
        CancellationToken cancellationToken);

    /// <summary>Создаёт группу материалов.</summary>
    Task<Guid> CreateGroupAsync(
        string name,
        CancellationToken cancellationToken);

    /// <summary>Включает материал в выбранную группу.</summary>
    Task<Guid> AddItemAsync(
        Guid groupId,
        string sourceTable,
        string materialName,
        CancellationToken cancellationToken);

    /// <summary>Удаляет группу материалов.</summary>
    Task<bool> DeleteGroupAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Исключает материал из группы.</summary>
    Task<bool> DeleteItemAsync(Guid id, CancellationToken cancellationToken);
}
