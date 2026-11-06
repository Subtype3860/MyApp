namespace MyApp.Infrastructure.Db.Entities;

public sealed class MaintenanceCardTemplate
{
    public Guid Id { get; set; }
    public int Revision { get; set; }
    public bool Published { get; set; }
    public string Snapshot { get; set; } = "{}";
    public Guid? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
}
public sealed class MaintenanceCard
{
    public Guid Id { get; set; }
    public Guid VehicleId { get; set; }
    public Guid TemplateId { get; set; }
    public int TemplateRevision { get; set; }
    public int PlanRevision { get; set; }
    public Guid? RunInId { get; set; }
    public string VehicleLabel { get; set; } = "";
    public string Template { get; set; } = "{}";
    public string Data { get; set; } = "{}";
    public string Audit { get; set; } = "[]";
    public string Defects { get; set; } = "{}";
    public string Readings { get; set; } = "[]";
    public string Status { get; set; } = "draft";
    public int Revision { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? AcceptedBy { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
}
public sealed class MaintenanceCardPhoto
{
    public Guid Id { get; set; }
    public Guid CardId { get; set; }
    public string OperationId { get; set; } = "";
    public string ContentType { get; set; } = "";
    public byte[] Content { get; set; } = [];
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
