using MyApp.Application.DTO;

namespace MyApp.Application.Services;

public static class MaintenanceCardMaterials
{
    public static (CardTemplate? Template, string? Error) Resolve(CardTemplate template, string model, string type,
        IReadOnlyList<MaintenanceEquipmentResponse> sources)
    {
        var all = sources.SelectMany(e => e.Intervals.Select(i => (Equipment: e, Interval: i))).ToArray();
        var service = MaintenanceCardRules.Normalize(template.Service);
        var choices = all.Where(x => MaintenanceCardRules.Normalize(x.Interval.Name) == service).ToArray();
        if (template.MaterialIntervalId is { } selected)
            choices = choices.Where(x => x.Interval.Id == selected).ToArray();
        else
        {
            var equipment = sources.Where(e => MaintenanceCardRules.Normalize(e.Name) == MaintenanceCardRules.Normalize(model)).ToArray();
            if (equipment.Length == 0)
            {
                var kind = MaintenanceCardRules.Normalize(type);
                var category = kind switch {
                    "САМОСВАЛ" or "КАРЬЕРНЫЙСАМОСВАЛ" or "КАРЬЕРНЫЕСАМОСВАЛЫ" => "САМОСВАЛЫ",
                    "ЭКСКАВАТОР" => "ЭКСКАВАТОРЫ", "БУЛЬДОЗЕР" => "БУЛЬДОЗЕРЫ", _ => kind
                };
                equipment = sources.Where(e => MaintenanceCardRules.Normalize(e.Name) == kind || MaintenanceCardRules.Normalize(e.Name) == category).ToArray();
            }
            choices = choices.Where(x => equipment.Any(e => e.Id == x.Equipment.Id)).ToArray();
        }
        if (choices.Length != 1)
            return (null, "Не найден однозначный источник материалов для этого ТО. В библиотеке карт выберите оборудование и вид ТО из раздела «Техническое обслуживание».");
        var choice = choices[0];
        if (choice.Interval.Items.Any(i => string.IsNullOrWhiteSpace(i.Unit)))
            return (null, "У материалов технического обслуживания не определена единица измерения. Проверьте их сопоставление со складом.");
        var materials = choice.Interval.Items.Select(i => new CardMaterial("maintenance:" + i.Id, choice.Equipment.Name,
            i.MaterialName, "", i.Unit, i.Quantity)).ToArray();
        return (template with { Materials = materials, MaterialIntervalId = choice.Interval.Id,
            MaterialSource = choice.Equipment.Name + " / " + choice.Interval.Name }, null);
    }
}
