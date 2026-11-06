namespace MyApp.Infrastructure.Db.Entities;

/// <summary>Общие поля записи фотографии или видеозаписи техники.</summary>
public interface IVehicleMediaRecord
{
    Guid Id { get; set; }
    string FileName { get; set; }
    string ContentType { get; set; }
    byte[]? Content { get; set; }
    long Size { get; set; }
    DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Запись справочника групп техники.</summary>
public sealed class GroupCarRecord
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public int ReportSortOrder { get; set; }
}

/// <summary>Запись справочника типов техники.</summary>
public sealed class TypeCarRecord
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public Guid? CarGroupId { get; set; }
    public string? IconPath { get; set; }
}

/// <summary>Запись справочника моделей техники.</summary>
public sealed class ModelCarRecord
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public Guid? CarTypeId { get; set; }
}

/// <summary>Запись карточки техники и её идентификационных номеров.</summary>
public sealed class NumberCarRecord
{
    public Guid Id { get; set; }
    public bool IsArchived { get; set; }
    public Guid? CarModeId { get; set; }
    public int? GarageNumber { get; set; }
    public string? StateNumber { get; set; }
    public string? Vin { get; set; }
}

/// <summary>Запись заявки на закупку для техники.</summary>
public sealed class VehiclePurchaseRequestRecord
{
    public Guid Id { get; set; }
    public Guid VehicleId { get; set; }
    public DateOnly RequestDate { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Запись неисправности и связанных с ней условий простоя техники.</summary>
public sealed class VehicleDefectRecord
{
    public Guid Id { get; set; }
    public Guid VehicleId { get; set; }
    public string NodeName { get; set; } = string.Empty;
    public string FailureReason { get; set; } = string.Empty;
    public string ErrorCode { get; set; } = string.Empty;
    public string Symptoms { get; set; } = string.Empty;
    public DateTimeOffset? DowntimeStartedAt { get; set; }
    public string OperationalState { get; set; } = "stopped";
    public Guid? AssignedTo { get; set; }
    public DateTimeOffset? RepairStartedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Показание счётчика моточасов техники на указанную дату.</summary>
public sealed class VehicleHourReadingRecord
{
    public Guid Id { get; set; }
    public Guid VehicleId { get; set; }
    public DateOnly ReadingDate { get; set; }
    public TimeOnly? ReadingTime { get; set; }
    public decimal EngineHours { get; set; }
    public int EngineNumber { get; set; }
    public string Note { get; set; } = string.Empty;
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Запись выполненной работы и её связи с неисправностью техники.</summary>
public sealed class VehicleWorkRecord
{
    public Guid Id { get; set; }
    public Guid? VehicleId { get; set; }
    public string WorkKind { get; set; } = "repair";
    public string Title { get; set; } = string.Empty;
    public DateOnly WorkDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public decimal? EngineHours { get; set; }
    public string Performer { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public Guid? DefectId { get; set; }
    public string FailureCause { get; set; } = string.Empty;
    public string RepairStatus { get; set; } = string.Empty;
    public string RequiredParts { get; set; } = string.Empty;
    public Guid? PerformedBy { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? OperationalState { get; set; }
    public DateTimeOffset? DowntimeStartedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Метаданные фотографии выполненной работы.</summary>
public sealed class VehicleWorkPhotoRecord : IVehicleMediaRecord
{
    public Guid Id { get; set; }
    public Guid WorkId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public byte[]? Content { get; set; }
    public string? StoragePath { get; set; }
    public long Size { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Метаданные фотографии неисправности.</summary>
public sealed class VehicleDefectPhotoRecord : IVehicleMediaRecord
{
    public Guid Id { get; set; }
    public Guid DefectId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public byte[]? Content { get; set; }
    public string? StoragePath { get; set; }
    public long Size { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Метаданные видеозаписи выполненной работы.</summary>
public sealed class VehicleWorkVideoRecord : IVehicleMediaRecord
{
    public Guid Id { get; set; }
    public Guid WorkId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public byte[]? Content { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Метаданные видеозаписи неисправности.</summary>
public sealed class VehicleDefectVideoRecord : IVehicleMediaRecord
{
    public Guid Id { get; set; }
    public Guid DefectId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public byte[]? Content { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Запись участия сотрудника в работе и решения механика.</summary>
public sealed class VehicleWorkParticipantRecord
{
    public Guid Id { get; set; }
    public Guid WorkId { get; set; }
    public Guid UserId { get; set; }
    public string Status { get; set; } = "pending";
    public DateTimeOffset RequestedAt { get; set; }
    public Guid? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
}
