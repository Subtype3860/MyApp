using MyApp.Application.Abstractions;
using MyApp.Application.Common;
using MyApp.Application.DTO;

namespace MyApp.Application.Services;

/// <summary>Прикладные операции шаблонов технического обслуживания.</summary>
public sealed class MaintenanceTemplateService(
    IMaintenanceTemplateRepository repository) : IMaintenanceTemplateService
{
    /// <summary>Возвращает список шаблонов ТО.</summary>
    public Task<IReadOnlyList<MaintenanceEquipmentResponse>> GetAllAsync(
        CancellationToken cancellationToken) =>
        repository.GetAllAsync(cancellationToken);

    /// <summary>Создаёт вид оборудования в шаблонах технического обслуживания.</summary>
    public async Task<ServiceResult<Guid>> CreateEquipmentAsync(
        MaintenanceEquipmentRequest request,
        CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        var validation = ValidateName(name, "техники");
        if (validation is not null)
        {
            return validation;
        }
        if (await repository.EquipmentNameExistsAsync(name, null, cancellationToken))
        {
            return ServiceResult<Guid>.Conflict("Такая техника уже существует.");
        }
        return ServiceResult<Guid>.Success(
            await repository.CreateEquipmentAsync(name, cancellationToken));
    }

    /// <summary>Создаёт интервал обслуживания для выбранного оборудования.</summary>
    public async Task<ServiceResult<Guid>> CreateIntervalAsync(
        Guid equipmentId,
        MaintenanceIntervalRequest request,
        CancellationToken cancellationToken)
    {
        if (!await repository.EquipmentExistsAsync(equipmentId, cancellationToken))
        {
            return ServiceResult<Guid>.NotFound();
        }
        var name = request.Name.Trim();
        var validation = ValidateName(name, "ТО");
        if (validation is not null)
        {
            return validation;
        }
        if (await repository.IntervalNameExistsAsync(
                equipmentId, name, null, cancellationToken))
        {
            return ServiceResult<Guid>.Conflict(
                "Такой пункт ТО для выбранной техники уже существует.");
        }
        return ServiceResult<Guid>.Success(
            await repository.CreateIntervalAsync(
                equipmentId, name, cancellationToken));
    }

    /// <summary>Добавляет материал и его количество в интервал ТО.</summary>
    public async Task<ServiceResult<Guid>> AddItemAsync(
        Guid intervalId,
        MaintenanceItemRequest request,
        CancellationToken cancellationToken)
    {
        var materialName = request.MaterialName.Trim();
        var validation = ValidateItem(materialName, request.Quantity);
        if (validation is not null)
        {
            return validation;
        }
        if (!await repository.IntervalExistsAsync(intervalId, cancellationToken))
        {
            return ServiceResult<Guid>.NotFound();
        }
        if (!await repository.MaterialExistsAsync(materialName, cancellationToken))
        {
            return Validation("Материал не найден в остатках на складе.");
        }
        if (await repository.ItemExistsAsync(
                intervalId, materialName, cancellationToken))
        {
            return ServiceResult<Guid>.Conflict(
                "Материал уже добавлен в этот шаблон ТО.");
        }
        return ServiceResult<Guid>.Success(
            await repository.AddItemAsync(
                intervalId, materialName, request.Quantity, cancellationToken));
    }

    /// <summary>Изменяет название вида оборудования в шаблонах ТО.</summary>
    public async Task<ServiceResult<Guid>> RenameEquipmentAsync(
        Guid id,
        MaintenanceEquipmentRequest request,
        CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        var validation = ValidateName(name, "техники");
        if (validation is not null)
        {
            return validation;
        }
        if (await repository.EquipmentNameExistsAsync(name, id, cancellationToken))
        {
            return ServiceResult<Guid>.Conflict("Такая техника уже существует.");
        }
        return await repository.RenameEquipmentAsync(id, name, cancellationToken)
            ? ServiceResult<Guid>.Success(id)
            : ServiceResult<Guid>.NotFound();
    }

    /// <summary>Изменяет название интервала обслуживания.</summary>
    public async Task<ServiceResult<Guid>> RenameIntervalAsync(
        Guid id,
        MaintenanceIntervalRequest request,
        CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        var validation = ValidateName(name, "ТО");
        if (validation is not null)
        {
            return validation;
        }
        return await repository.RenameIntervalAsync(id, name, cancellationToken)
            ? ServiceResult<Guid>.Success(id)
            : ServiceResult<Guid>.NotFound();
    }

    /// <summary>Обновляет количество материала в позиции шаблона обслуживания.</summary>
    public async Task<ServiceResult<Guid>> UpdateItemAsync(
        Guid id,
        MaintenanceItemRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Quantity <= 0)
        {
            return Validation("Количество должно быть больше нуля.");
        }
        return await repository.UpdateItemAsync(id, request.Quantity, cancellationToken)
            ? ServiceResult<Guid>.Success(id)
            : ServiceResult<Guid>.NotFound();
    }

    /// <summary>Удаляет вид оборудования из шаблонов ТО.</summary>
    public Task<ServiceResult<Guid>> DeleteEquipmentAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        DeleteAsync(id, repository.DeleteEquipmentAsync, cancellationToken);

    /// <summary>Удаляет интервал обслуживания.</summary>
    public Task<ServiceResult<Guid>> DeleteIntervalAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        DeleteAsync(id, repository.DeleteIntervalAsync, cancellationToken);

    /// <summary>Удаляет позицию материала из интервала ТО.</summary>
    public Task<ServiceResult<Guid>> DeleteItemAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        DeleteAsync(id, repository.DeleteItemAsync, cancellationToken);

    /// <summary>Проверяет допустимость названия элемента шаблона ТО.</summary>
    private static ServiceResult<Guid>? ValidateName(string name, string entity) =>
        name.Length is < 1 or > 100
            ? Validation($"Название {entity} должно содержать от 1 до 100 символов.")
            : null;

    /// <summary>Проверяет материал и количество в позиции шаблона ТО.</summary>
    private static ServiceResult<Guid>? ValidateItem(
        string materialName,
        decimal quantity)
    {
        if (materialName.Length == 0)
        {
            return Validation("Укажите наименование материала.");
        }
        return quantity <= 0
            ? Validation("Количество должно быть больше нуля.")
            : null;
    }

    /// <summary>Создаёт результат с ошибками проверки полей запроса.</summary>
    private static ServiceResult<Guid> Validation(string message) =>
        ServiceResult<Guid>.Validation(
            new Dictionary<string, string[]> { ["maintenance"] = [message] });

    /// <summary>Удаляет шаблон ТО.</summary>
    private static async Task<ServiceResult<Guid>> DeleteAsync(
        Guid id,
        Func<Guid, CancellationToken, Task<bool>> delete,
        CancellationToken cancellationToken) =>
        await delete(id, cancellationToken)
            ? ServiceResult<Guid>.Success(id)
            : ServiceResult<Guid>.NotFound();
}
