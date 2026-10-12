namespace MyApp.Domain.Entities;

public static class VehicleOperationalState
{
    public static string Resolve(IEnumerable<(string? RepairStatus, string OperationalState)> defects)
    {
        var open = defects.Where(defect => defect.RepairStatus != "repaired").ToArray();
        return open.Length == 0 ? "healthy"
            : open.Any(defect => defect.OperationalState == "stopped") ? "stopped"
            : "operating_with_defect";
    }
}
