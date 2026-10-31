namespace MyApp.Application.Models;

/// <summary>Правила определения рабочего состояния техники по открытым неисправностям.</summary>
public static class VehicleOperationalState
{
    /// <summary>Определяет состояние техники: открытая неисправность с простоем имеет приоритет над остальными.</summary>
    public static string Resolve(IEnumerable<(string? RepairStatus, string OperationalState)> defects)
    {
        var open = defects.Where(defect => defect.RepairStatus != "repaired").ToArray();
        return open.Length == 0 ? "healthy"
            : open.Any(defect => defect.OperationalState == "stopped") ? "stopped"
            : "operating_with_defect";
    }
}
