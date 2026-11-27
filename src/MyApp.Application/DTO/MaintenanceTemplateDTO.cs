namespace MyApp.Application.DTO;

/// <summary>Название оборудования для шаблона ТО.</summary>
public sealed record MaintenanceEquipmentRequest(string Name);

/// <summary>Данные интервала обслуживания оборудования.</summary>
public sealed record MaintenanceIntervalRequest(string Name);

/// <summary>Материал и количество для позиции шаблона ТО.</summary>
public sealed record MaintenanceItemRequest(
    string MaterialName,
    decimal Quantity);

/// <summary>Позиция материала в шаблоне ТО для клиента.</summary>
public sealed record MaintenanceItemResponse(
    Guid Id,
    string MaterialName,
    string Unit,
    decimal Quantity,
    decimal AvailableQuantity);

/// <summary>Интервал ТО с входящими в него материалами.</summary>
public sealed record MaintenanceIntervalResponse(
    Guid Id,
    string Name,
    IReadOnlyList<MaintenanceItemResponse> Items);

/// <summary>Вид оборудования с доступными интервалами ТО.</summary>
public sealed record MaintenanceEquipmentResponse(
    Guid Id,
    string Name,
    IReadOnlyList<MaintenanceIntervalResponse> Intervals);
