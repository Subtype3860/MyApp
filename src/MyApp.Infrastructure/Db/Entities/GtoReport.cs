namespace MyApp.Infrastructure.Db.Entities;

/// <summary>Полный сохранённый снимок отчёта по дате и смене.</summary>
public sealed class GtoReport
{
    public DateOnly ReportDate { get; set; }
    public int Shift { get; set; }
    public int Revision { get; set; }
    public string Snapshot { get; set; } = "{}";
    public Guid UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
}
