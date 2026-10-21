namespace MyApp.Infrastructure.Db.Entities;

/// <summary>Заголовок сохранённого требования на выдачу материалов.</summary>
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

/// <summary>Позиция сохранённого требования на выдачу материалов.</summary>
public sealed class ComponentRequirementItemRecord
{
    public Guid RequirementId { get; set; }
    public int Position { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
}

/// <summary>Сохранённое изображение подписи сотрудника и его ключ.</summary>
public sealed class EmployeeSignatureRecord
{
    public string LastName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string Patronymic { get; set; } = string.Empty;
    public byte[] Content { get; set; } = [];
    public string ContentType { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Запись справочника сотрудников.</summary>
public sealed class EmployeeDirectoryRecord
{
    public string FullName { get; set; } = string.Empty;
    public string Profession { get; set; } = string.Empty;
}

/// <summary>Вид оборудования в иерархии шаблонов ТО.</summary>
public sealed class MaintenanceEquipmentRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

/// <summary>Интервал обслуживания определённого вида оборудования.</summary>
public sealed class MaintenanceIntervalRecord
{
    public Guid Id { get; set; }
    public Guid EquipmentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

/// <summary>Материал и его количество в интервале обслуживания.</summary>
public sealed class MaintenanceIntervalItemRecord
{
    public Guid Id { get; set; }
    public Guid IntervalId { get; set; }
    public string MaterialName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>Группа материалов для выбора и отображения.</summary>
public sealed class MaterialGroupRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>Связь материала с группой.</summary>
public sealed class MaterialGroupItemRecord
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public string SourceTable { get; set; } = string.Empty;
    public string MaterialName { get; set; } = string.Empty;
}

/// <summary>Сохранённые параметры срока хранения временных медиафайлов.</summary>
public sealed class MediaStorageSettingsRecord
{
    public short Id { get; set; }
    public int RetentionDays { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Модель чтения текущих остатков основного склада.</summary>
public sealed class FullStockRecord
{
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string? Quantity { get; set; }
}

/// <summary>Модель чтения текущих остатков механического склада.</summary>
public sealed class MechanicalStockRecord
{
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string? Quantity { get; set; }
}

/// <summary>Модель чтения прежнего источника остатков основного склада.</summary>
public sealed class FullStockLegacyRecord
{
    public string Name { get; set; } = string.Empty;
    public string? Amount { get; set; }
}

/// <summary>Модель чтения прежнего источника остатков механического склада.</summary>
public sealed class MechanicalStockLegacyRecord
{
    public string Name { get; set; } = string.Empty;
    public string? Amount { get; set; }
}
