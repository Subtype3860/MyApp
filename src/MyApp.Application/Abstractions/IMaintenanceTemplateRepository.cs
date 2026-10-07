using MyApp.Application.DTO;

namespace MyApp.Application.Abstractions;

public interface IMaintenanceTemplateRepository
{
    Task<IReadOnlyList<MaintenanceEquipmentResponse>> GetAllAsync(
        CancellationToken cancellationToken);

    Task<bool> EquipmentExistsAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> IntervalExistsAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> EquipmentNameExistsAsync(
        string name,
        Guid? exceptId,
        CancellationToken cancellationToken);

    Task<bool> IntervalNameExistsAsync(
        Guid equipmentId,
        string name,
        Guid? exceptId,
        CancellationToken cancellationToken);

    Task<bool> IntervalNameConflictExistsAsync(
        Guid intervalId,
        string name,
        CancellationToken cancellationToken);

    Task<bool> MaterialExistsAsync(
        string materialName,
        CancellationToken cancellationToken);

    Task<bool> ItemExistsAsync(
        Guid intervalId,
        string materialName,
        CancellationToken cancellationToken);

    Task<Guid> CreateEquipmentAsync(
        string name,
        CancellationToken cancellationToken);

    Task<Guid> CreateIntervalAsync(
        Guid equipmentId,
        string name,
        CancellationToken cancellationToken);

    Task<Guid> AddItemAsync(
        Guid intervalId,
        string materialName,
        decimal quantity,
        CancellationToken cancellationToken);

    Task<bool> RenameEquipmentAsync(
        Guid id,
        string name,
        CancellationToken cancellationToken);

    Task<bool> RenameIntervalAsync(
        Guid id,
        string name,
        CancellationToken cancellationToken);

    Task<bool> UpdateItemAsync(
        Guid id,
        decimal quantity,
        CancellationToken cancellationToken);

    Task<bool> DeleteEquipmentAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> DeleteIntervalAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> DeleteItemAsync(Guid id, CancellationToken cancellationToken);
}
