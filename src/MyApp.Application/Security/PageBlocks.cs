namespace MyApp.Application.Security;

/// <summary>Настройки представления страниц; не заменяют серверные права на операции.</summary>
public static class PageBlocks
{
    public const string HiddenPrefix = "ui.hidden.";
    public static readonly IReadOnlyList<PermissionDefinition> Catalog =
    [
        new("shift_report.repair", "Отчёт за смену: ремонт"),
        new("shift_report.maintenance", "Отчёт за смену: проведённые ТО"),
        new("shift_report.auxiliary", "Отчёт за смену: вспомогательные работы"),
        new("shift_report.performers", "Отчёт за смену: исполнители"),
        new("shift_report.media", "Отчёт за смену: фото и видео")
    ];
}
