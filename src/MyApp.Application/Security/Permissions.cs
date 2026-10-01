namespace MyApp.Application.Security;

public static class Permissions
{
    public const string HomeView = "menu.home";
    public const string TablesView = "menu.tables";
    public const string VehiclesView = "menu.vehicles";
    public const string RequirementsView = "menu.requirements";
    public const string MaintenanceView = "menu.maintenance";

    public static readonly IReadOnlyList<PermissionDefinition> Catalog =
    [
        new(TablesView, "Таблицы",
        [
            new("tables.v_full_ost", "Остатки на складе"),
            new("tables.v_meh_ost", "Остатки механиков"),
            new("tables.v_workers", "Работники"),
            new("tables.selected_components", "Выбранные компоненты")
        ]),
        new(VehiclesView, "Транспорт",
        [
            new("vehicles.repair_request", "Заявка на ремонт"),
            new("vehicles.works", "Ремонт"),
            new("vehicles.parts_request", "Заявка на закупку ЗЧ"),
            new("vehicles.hours", "Моточасы"),
            new("vehicles.report", "Отчёт")
        ]),
        new(RequirementsView, "Выписанные требования"),
        new(MaintenanceView, "Техническое обслуживание")
    ];

    public static readonly IReadOnlyList<string> All =
        Catalog.SelectMany(item => new[] { item.Key }.Concat(item.Children.Select(child => child.Key)))
            .ToArray();

    public static bool IsKnown(string permission) =>
        All.Contains(permission, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Expand(IEnumerable<string>? permissions)
    {
        var legacyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["tables.view"] = TablesView,
            ["vehicles.view"] = VehiclesView,
            ["requirements.view"] = RequirementsView,
            ["maintenance.view"] = MaintenanceView
        };
        var selected = new HashSet<string>(
            (permissions ?? []).Select(permission =>
                legacyMap.TryGetValue(permission, out var replacement)
                    ? replacement
                    : permission),
            StringComparer.OrdinalIgnoreCase);

        foreach (var item in Catalog)
        {
            if (item.Children.Any(child => selected.Contains(child.Key)))
            {
                selected.Add(item.Key);
            }
        }

        return selected.Where(IsKnown).ToArray();
    }
}

public sealed record PermissionDefinition(
    string Key,
    string Label,
    IReadOnlyList<PermissionDefinition> Children)
{
    public PermissionDefinition(string key, string label)
        : this(key, label, [])
    {
    }
}
