namespace MyApp.Infrastructure.Db.Entities;

public interface IVehicleMediaRecord
{
    Guid Id { get; set; }
    string FileName { get; set; }
    string ContentType { get; set; }
    byte[]? Content { get; set; }
    long Size { get; set; }
    DateTimeOffset CreatedAt { get; set; }
}

public sealed class GroupCarRecord
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
}

public sealed class TypeCarRecord
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public Guid? CarGroupId { get; set; }
}

public sealed class ModelCarRecord
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public Guid? CarTypeId { get; set; }
}

public sealed class NumberCarRecord
{
    public Guid Id { get; set; }
    public Guid? CarModeId { get; set; }
    public int? GarageNumber { get; set; }
    public string? StateNumber { get; set; }
    public string? Vin { get; set; }
}

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

public sealed class VehicleDefectRecord
{
    public Guid Id { get; set; }
    public Guid VehicleId { get; set; }
    public string NodeName { get; set; } = string.Empty;
    public string FailureReason { get; set; } = string.Empty;
    public string ErrorCode { get; set; } = string.Empty;
    public string Symptoms { get; set; } = string.Empty;
    public DateTimeOffset DowntimeStartedAt { get; set; }
    public Guid? AssignedTo { get; set; }
    public DateTimeOffset? RepairStartedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class VehicleHourReadingRecord
{
    public Guid Id { get; set; }
    public Guid VehicleId { get; set; }
    public DateOnly ReadingDate { get; set; }
    public decimal EngineHours { get; set; }
    public string Note { get; set; } = string.Empty;
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class VehicleWorkRecord
{
    public Guid Id { get; set; }
    public Guid VehicleId { get; set; }
    public DateOnly WorkDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal? EngineHours { get; set; }
    public string Performer { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public Guid? DefectId { get; set; }
    public string FailureCause { get; set; } = string.Empty;
    public string RepairStatus { get; set; } = string.Empty;
    public string RequiredParts { get; set; } = string.Empty;
    public Guid? PerformedBy { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

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
