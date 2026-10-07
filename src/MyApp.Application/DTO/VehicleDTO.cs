namespace MyApp.Application.DTO;

public sealed record VehicleResponse(
    Guid Id,
    string GroupName,
    string TypeName,
    string ModelName,
    int? GarageNumber,
    string StateNumber,
    string Vin);

public sealed record VehiclePurchaseRequest(
    DateOnly RequestDate,
    string RequestNumber,
    string ItemName,
    decimal Quantity,
    string Status,
    string Note);

public sealed record VehiclePurchaseResponse(
    Guid Id,
    DateOnly RequestDate,
    string RequestNumber,
    string ItemName,
    decimal Quantity,
    string Status,
    string Note,
    DateTimeOffset CreatedAt);

public sealed record VehicleDefectRequest(
    string? ErrorCode,
    string Symptoms,
    DateTimeOffset DowntimeStartedAt);

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

public sealed record VehicleHoursRequest(
    DateOnly ReadingDate,
    decimal EngineHours,
    string Note);

public sealed record VehicleHoursResponse(
    Guid Id,
    DateOnly ReadingDate,
    decimal EngineHours,
    string Note,
    DateTimeOffset CreatedAt);

public sealed record VehicleHoursImportItem(
    Guid VehicleId,
    decimal? EngineHours);

public sealed record VehicleHoursImportError(
    int Line,
    string Message);

public sealed record VehicleHoursImportResponse(
    int Imported,
    IReadOnlyList<VehicleHoursImportError> Errors);

public sealed record VehicleWorkRequest(
    Guid DefectId,
    string Cause,
    string Description,
    string Status,
    string? RequiredParts);

public sealed record VehicleWorkResponse(
    Guid Id,
    Guid? DefectId,
    string DefectNodeName,
    string Description,
    string PurchaseRequestNumber,
    DateTimeOffset CreatedAt,
    IReadOnlyList<VehicleWorkPhotoResponse> Photos,
    string Cause = "",
    string Status = "",
    string RequiredParts = "",
    Guid? PerformedBy = null,
    string PerformerName = "",
    DateTimeOffset? CompletedAt = null,
    string PartsRequestNumber = "",
    DateOnly? PartsRequestDate = null,
    string PartsRequestFileName = "",
    string PurchaseRequestContentType = "",
    bool HasPurchaseRequestFile = false,
    IReadOnlyList<VehicleMediaResponse>? Videos = null);

public sealed record VehicleWorkPhotoResponse(
    Guid Id,
    string FileName,
    string ContentType,
    long Size);

public sealed record VehicleWorkPhotoUpload(
    string FileName,
    string ContentType,
    byte[] Content);

public sealed record VehicleWorkPhotoContent(
    string FileName,
    string ContentType,
    Stream Content,
    string? StoragePath = null);

public sealed record VehicleMediaResponse(
    Guid Id,
    string FileName,
    string ContentType,
    long Size);

public sealed record VehicleMediaUpload(
    string FileName,
    string ContentType,
    byte[] Content);

public sealed record VehiclePartsRequest(
    string RequestNumber,
    DateOnly RequestDate,
    string? FileName,
    string? ContentType,
    byte[]? Content);

public sealed record VehicleRequestFileContent(
    string FileName,
    string ContentType,
    byte[] Content);

public sealed record VehicleJournalResponse(
    VehicleResponse Vehicle,
    IReadOnlyList<VehiclePurchaseResponse> Purchases,
    IReadOnlyList<VehicleDefectResponse> Defects,
    IReadOnlyList<VehicleHoursResponse> Hours,
    IReadOnlyList<VehicleWorkResponse> Works);
