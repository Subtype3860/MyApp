namespace MyApp.Application.DTO;

/// <summary>Карточка учтённой техники.</summary>
public sealed record VehicleResponse(
    Guid Id,
    string GroupName,
    string TypeName,
    string ModelName,
    int? GarageNumber,
    string StateNumber,
    string Vin,
    string OperationalState = "healthy",
    bool IsArchived = false,
    string? TypeIconPath = null,
    int GroupReportSortOrder = 0)
{
    public int EngineCount => TypeName.Contains("самосвал", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
}

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
    DateTimeOffset? DowntimeStartedAt,
    string OperationalState = "stopped",
    string NodeName = "",
    string FailureReason = "");

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
    DateTimeOffset? DowntimeStartedAt,
    Guid CreatedById,
    string CreatedByName,
    Guid? AssignedToId,
    string AssignedToName,
    DateTimeOffset? RepairStartedAt,
    IReadOnlyList<VehicleWorkPhotoResponse> Photos,
    IReadOnlyList<VehicleMediaResponse> Videos,
    string NodeName = "",
    string FailureReason = "",
    string OperationalState = "stopped");

/// <summary>Запрос на добавление показаний моточасов техники.</summary>
public sealed record VehicleHoursRequest(
    DateOnly ReadingDate,
    decimal EngineHours,
    string Note,
    int EngineNumber = 0);

/// <summary>Запись показаний моточасов техники.</summary>
public sealed record VehicleHoursResponse(
    Guid Id,
    DateOnly ReadingDate,
    decimal EngineHours,
    string Note,
    DateTimeOffset CreatedAt,
    int EngineNumber = 0,
    TimeOnly? ReadingTime = null);

/// <summary>Одна строка данных, распознанная при импорте моточасов из файла.</summary>
public sealed record VehicleHoursImportItem(
    Guid VehicleId,
    decimal? EngineHours,
    int EngineNumber = 0);

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
    DateTimeOffset? RepairDateTime = null,
    string? OperationalState = null,
    DateTimeOffset? DowntimeStartedAt = null,
    string Location = "");

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
    IReadOnlyList<VehicleMediaResponse>? Videos = null,
    Guid CreatedById = default,
    string PerformerPosition = "",
    string Location = "",
    string? OperationalState = null,
    IReadOnlyList<VehicleWorkParticipantResponse>? Participants = null,
    string Title = "",
    string WorkKind = "repair");

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

/// <summary>Поток содержимого видео для передачи клиенту без буферизации файла целиком.</summary>
public sealed record VehicleMediaStream(
    string FileName,
    string ContentType,
    Stream Content);

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
    string? RequiredParts = null,
    DateOnly? CompletedDate = null);

/// <summary>Реквизиты и состояние заявки на закупку запчастей.</summary>
public sealed record VehiclePartsRequestResponse(
    Guid Id,
    Guid DefectId,
    string RequestNumber,
    DateOnly RequestDate,
    string Description,
    string RequiredParts,
    DateTimeOffset CreatedAt,
    DateOnly? CompletedDate = null);

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
    IReadOnlyList<VehicleWorkResponse> Works,
    IReadOnlyList<VehiclePartsRequestResponse>? PartsRequests = null);

/// <summary>Изменяемые поля записи истории техники.</summary>
public sealed record VehicleHistoryEditRequest(string Description);

/// <summary>Участие сотрудника в конкретной работе; в отчёт попадает после подтверждения механиком.</summary>
public sealed record VehicleWorkParticipantResponse(
    Guid Id, Guid UserId, string Name, string Position, string Status,
    DateTimeOffset RequestedAt, Guid? ReviewedBy, DateTimeOffset? ReviewedAt,
    string ReviewedByName = "");

/// <summary>Решение механика по участию сотрудника в работе.</summary>
public sealed record VehicleWorkParticipantReviewRequest(bool Approved);

/// <summary>Работа на участке без привязки к транспорту.</summary>
public sealed record AuxiliaryWorkRequest(string Title, string Description, string Location, DateTimeOffset PerformedAt);

/// <summary>Поля карточки техники для создания или изменения.</summary>
public sealed record VehicleCatalogRequest(string GroupName, string TypeName, string ModelName, int? GarageNumber, string StateNumber, string Vin);
/// <summary>Значение справочника, доступное при редактировании техники.</summary>
public sealed record VehicleCatalogOption(Guid Id, string Name, Guid? ParentId = null);
/// <summary>Справочники парка и признаки доступных пользователю действий.</summary>
public sealed record VehicleCatalogOptions(bool CanManage, IReadOnlyList<VehicleCatalogOption> Groups, IReadOnlyList<VehicleCatalogOption> Types, IReadOnlyList<VehicleCatalogOption> Models)
{
    public bool CanManageHours { get; init; }
}
