namespace MyApp.Application.DTO;

/// <summary>Карточка учтённой техники.</summary>
public sealed record VehicleResponse(
    Guid Id,
    string GroupName,
    string TypeName,
    string ModelName,
    int? GarageNumber,
    string StateNumber,
    string Vin);

/// <summary>Запрос на создание заявки о закупке материалов для техники.</summary>
public sealed record VehiclePurchaseRequest(
    DateOnly RequestDate,
    string RequestNumber,
    string ItemName,
    decimal Quantity,
    string Status,
    string Note);

/// <summary>Запись о закупке материалов, отображаемая в журнале техники.</summary>
public sealed record VehiclePurchaseResponse(
    Guid Id,
    DateOnly RequestDate,
    string RequestNumber,
    string ItemName,
    decimal Quantity,
    string Status,
    string Note,
    DateTimeOffset CreatedAt);

/// <summary>Запрос на регистрацию неисправности (заявки на ремонт).</summary>
public sealed record VehicleDefectRequest(
    string? ErrorCode,
    string Symptoms,
    DateTimeOffset DowntimeStartedAt);

/// <summary>
/// Неисправность техники с прикреплёнными фото- и видеовложениями и
/// сведениями об исполнителе.
/// </summary>
public sealed record VehicleDefectResponse(
    Guid Id,
    string ErrorCode,
    string Symptoms,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset DowntimeStartedAt,
    Guid CreatedById,
    string CreatedByName,
    Guid? AssignedToId,
    string AssignedToName,
    DateTimeOffset? RepairStartedAt,
    IReadOnlyList<VehicleWorkPhotoResponse> Photos,
    IReadOnlyList<VehicleMediaResponse> Videos,
    string NodeName = "",
    string FailureReason = "");

/// <summary>Запрос на добавление показаний моточасов техники.</summary>
public sealed record VehicleHoursRequest(
    DateOnly ReadingDate,
    decimal EngineHours,
    string Note);

/// <summary>Запись показаний моточасов техники.</summary>
public sealed record VehicleHoursResponse(
    Guid Id,
    DateOnly ReadingDate,
    decimal EngineHours,
    string Note,
    DateTimeOffset CreatedAt);

/// <summary>Одна строка данных, распознанная при импорте моточасов из файла.</summary>
public sealed record VehicleHoursImportItem(
    Guid VehicleId,
    decimal? EngineHours);

/// <summary>Ошибка, возникшая при разборе конкретной строки импортируемого файла моточасов.</summary>
public sealed record VehicleHoursImportError(
    int Line,
    string Message);

/// <summary>Результат импорта моточасов: количество импортированных строк и список ошибок.</summary>
public sealed record VehicleHoursImportResponse(
    int Imported,
    IReadOnlyList<VehicleHoursImportError> Errors);

/// <summary>Запрос на регистрацию или завершение ремонтной работы по неисправности.</summary>
public sealed record VehicleWorkRequest(
    Guid DefectId,
    string Cause,
    string Description,
    string Status,
    string? RequiredParts,
    DateTimeOffset? RepairDateTime = null);

/// <summary>
/// Запись о выполненных ремонтных работах, включая заявку на закупку
/// запчастей и прикреплённые фото- и видеовложения.
/// </summary>
public sealed record VehicleWorkResponse(
    Guid Id,
    Guid? DefectId,
    string DefectNodeName,
    string Description,
    DateTimeOffset CreatedAt,
    IReadOnlyList<VehicleWorkPhotoResponse> Photos,
    string Cause = "",
    string Status = "",
    string RequiredParts = "",
    Guid? PerformedBy = null,
    string PerformerName = "",
    DateTimeOffset? CompletedAt = null,
    IReadOnlyList<VehiclePartsRequestResponse>? PartsRequests = null,
    IReadOnlyList<VehicleMediaResponse>? Videos = null);

/// <summary>Метаданные загруженной фотографии (без содержимого файла).</summary>
public sealed record VehicleWorkPhotoResponse(
    Guid Id,
    string FileName,
    string ContentType,
    long Size);

/// <summary>Загружаемый файл фотографии перед сохранением в хранилище.</summary>
public sealed record VehicleWorkPhotoUpload(
    string FileName,
    string ContentType,
    byte[] Content);

/// <summary>Содержимое фотографии, возвращаемое клиенту при её открытии или скачивании.</summary>
public sealed record VehicleWorkPhotoContent(
    string FileName,
    string ContentType,
    byte[] Content,
    string? StoragePath = null);

/// <summary>Метаданные загруженного видео (без содержимого файла).</summary>
public sealed record VehicleMediaResponse(
    Guid Id,
    string FileName,
    string ContentType,
    long Size);

/// <summary>Загружаемый видеофайл перед сохранением в хранилище.</summary>
public sealed record VehicleMediaUpload(
    string FileName,
    string ContentType,
    byte[] Content);

/// <summary>Данные заявки на закупку запчастей, прикладываемые к ремонтной работе.</summary>
public sealed record VehiclePartsRequest(
    string RequestNumber,
    DateOnly RequestDate,
    string Description = "",
    string? RequiredParts = null);

public sealed record VehiclePartsRequestResponse(
    Guid Id,
    Guid DefectId,
    string RequestNumber,
    DateOnly RequestDate,
    string Description,
    string RequiredParts,
    DateTimeOffset CreatedAt);

/// <summary>Содержимое файла заявки на закупку запчастей.</summary>
/// <summary>
/// Полный журнал техники: данные о самой технике, закупках, неисправностях,
/// моточасах и выполненных ремонтных работах.
/// </summary>
public sealed record VehicleJournalResponse(
    VehicleResponse Vehicle,
    IReadOnlyList<VehiclePurchaseResponse> Purchases,
    IReadOnlyList<VehicleDefectResponse> Defects,
    IReadOnlyList<VehicleHoursResponse> Hours,
    IReadOnlyList<VehicleWorkResponse> Works);
