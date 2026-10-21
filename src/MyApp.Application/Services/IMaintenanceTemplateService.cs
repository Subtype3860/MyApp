using MyApp.Application.Common;
using MyApp.Application.DTO;

namespace MyApp.Application.Services;

/// <summary>Прикладные операции шаблонов технического обслуживания.</summary>
public interface IMaintenanceTemplateService
{
    /// <summary>Возвращает список шаблонов ТО.</summary>
    Task<IReadOnlyList<MaintenanceEquipmentResponse>> GetAllAsync(
        CancellationToken cancellationToken);

    /// <summary>Создаёт вид оборудования в шаблонах технического обслуживания.</summary>
    Task<ServiceResult<Guid>> CreateEquipmentAsync(
        MaintenanceEquipmentRequest request,
        CancellationToken cancellationToken);

    /// <summary>Создаёт интервал обслуживания для выбранного оборудования.</summary>
    Task<ServiceResult<Guid>> CreateIntervalAsync(
        Guid equipmentId,
        MaintenanceIntervalRequest request,
        CancellationToken cancellationToken);

    /// <summary>Добавляет материал и его количество в интервал ТО.</summary>
    Task<ServiceResult<Guid>> AddItemAsync(
        Guid intervalId,
        MaintenanceItemRequest request,
        CancellationToken cancellationToken);

    /// <summary>Изменяет название вида оборудования в шаблонах ТО.</summary>
    Task<ServiceResult<Guid>> RenameEquipmentAsync(
        Guid id,
        MaintenanceEquipmentRequest request,
        CancellationToken cancellationToken);

    /// <summary>Изменяет название интервала обслуживания.</summary>
    Task<ServiceResult<Guid>> RenameIntervalAsync(
        Guid id,
        MaintenanceIntervalRequest request,
        CancellationToken cancellationToken);

    /// <summary>Обновляет количество материала в позиции шаблона обслуживания.</summary>
    Task<ServiceResult<Guid>> UpdateItemAsync(
        Guid id,
        MaintenanceItemRequest request,
        CancellationToken cancellationToken);

    /// <summary>Удаляет вид оборудования из шаблонов ТО.</summary>
    Task<ServiceResult<Guid>> DeleteEquipmentAsync(
        Guid id,
        CancellationToken cancellationToken);

    /// <summary>Удаляет интервал обслуживания.</summary>
    Task<ServiceResult<Guid>> DeleteIntervalAsync(
        Guid id,
        CancellationToken cancellationToken);

    /// <summary>Удаляет позицию материала из интервала ТО.</summary>
    Task<ServiceResult<Guid>> DeleteItemAsync(
        Guid id,
        CancellationToken cancellationToken);
}
