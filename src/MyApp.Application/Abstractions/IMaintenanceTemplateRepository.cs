using MyApp.Application.DTO;

namespace MyApp.Application.Abstractions;

/// <summary>Доступ к данным шаблонов технического обслуживания.</summary>
public interface IMaintenanceTemplateRepository
{
    /// <summary>Возвращает список шаблонов ТО.</summary>
    Task<IReadOnlyList<MaintenanceEquipmentResponse>> GetAllAsync(
        CancellationToken cancellationToken);

    /// <summary>Проверяет существование вида оборудования.</summary>
    Task<bool> EquipmentExistsAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Проверяет существование интервала обслуживания.</summary>
    Task<bool> IntervalExistsAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Проверяет, занято ли название вида оборудования.</summary>
    Task<bool> EquipmentNameExistsAsync(
        string name,
        Guid? exceptId,
        CancellationToken cancellationToken);

    /// <summary>Проверяет уникальность названия интервала в пределах оборудования.</summary>
    Task<bool> IntervalNameExistsAsync(
        Guid equipmentId,
        string name,
        Guid? exceptId,
        CancellationToken cancellationToken);

    /// <summary>Проверяет наличие материала в доступном источнике складских данных.</summary>
    Task<bool> MaterialExistsAsync(
        string materialName,
        CancellationToken cancellationToken);

    /// <summary>Проверяет существование позиции шаблона обслуживания.</summary>
    Task<bool> ItemExistsAsync(
        Guid intervalId,
        string materialName,
        CancellationToken cancellationToken);

    /// <summary>Создаёт вид оборудования в шаблонах технического обслуживания.</summary>
    Task<Guid> CreateEquipmentAsync(
        string name,
        CancellationToken cancellationToken);

    /// <summary>Создаёт интервал обслуживания для выбранного оборудования.</summary>
    Task<Guid> CreateIntervalAsync(
        Guid equipmentId,
        string name,
        CancellationToken cancellationToken);

    /// <summary>Добавляет материал и его количество в интервал ТО.</summary>
    Task<Guid> AddItemAsync(
        Guid intervalId,
        string materialName,
        decimal quantity,
        CancellationToken cancellationToken);

    /// <summary>Изменяет название вида оборудования в шаблонах ТО.</summary>
    Task<bool> RenameEquipmentAsync(
        Guid id,
        string name,
        CancellationToken cancellationToken);

    /// <summary>Изменяет название интервала обслуживания.</summary>
    Task<bool> RenameIntervalAsync(
        Guid id,
        string name,
        CancellationToken cancellationToken);

    /// <summary>Обновляет количество материала в позиции шаблона обслуживания.</summary>
    Task<bool> UpdateItemAsync(
        Guid id,
        decimal quantity,
        CancellationToken cancellationToken);

    /// <summary>Удаляет вид оборудования из шаблонов ТО.</summary>
    Task<bool> DeleteEquipmentAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Удаляет интервал обслуживания.</summary>
    Task<bool> DeleteIntervalAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Удаляет позицию материала из интервала ТО.</summary>
    Task<bool> DeleteItemAsync(Guid id, CancellationToken cancellationToken);
}
