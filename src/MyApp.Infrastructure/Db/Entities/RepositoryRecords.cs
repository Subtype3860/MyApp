namespace MyApp.Infrastructure.Db.Entities;

public sealed class ComponentRequirementRecord
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string IssuerName { get; set; } = string.Empty;
    public string VehicleNumber { get; set; } = string.Empty;
    public string SourceTable { get; set; } = string.Empty;
    public string FormData { get; set; } = string.Empty;
}

public sealed class ComponentRequirementItemRecord
{
    public Guid RequirementId { get; set; }
    public int Position { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
}

public sealed class EmployeeSignatureRecord
{
    public string LastName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string Patronymic { get; set; } = string.Empty;
    public byte[] Content { get; set; } = [];
    public string ContentType { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class EmployeeDirectoryRecord
{
    public string FullName { get; set; } = string.Empty;
    public string Profession { get; set; } = string.Empty;
}

public sealed class MaintenanceEquipmentRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public sealed class MaintenanceIntervalRecord
{
    public Guid Id { get; set; }
    public Guid EquipmentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public sealed class MaintenanceIntervalItemRecord
{
    public Guid Id { get; set; }
    public Guid IntervalId { get; set; }
    public string MaterialName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public int SortOrder { get; set; }
}

public sealed class MaterialGroupRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class MaterialGroupItemRecord
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public string SourceTable { get; set; } = string.Empty;
    public string MaterialName { get; set; } = string.Empty;
}

public sealed class MediaStorageSettingsRecord
{
    public short Id { get; set; }
    public int RetentionDays { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class FullStockRecord
{
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string? Quantity { get; set; }
}

public sealed class MechanicalStockRecord
{
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string? Quantity { get; set; }
}

public sealed class FullStockLegacyRecord
{
    public string Name { get; set; } = string.Empty;
    public string? Amount { get; set; }
}

public sealed class MechanicalStockLegacyRecord
{
    public string Name { get; set; } = string.Empty;
    public string? Amount { get; set; }
}
