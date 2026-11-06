using System.Text.Json.Nodes;

namespace MyApp.Application.Services;

/// <summary>Ограничивает строки общего снимка ГТО и сохраняет скрытые строки при редактировании своего участка.</summary>
public static class FleetReportScope
{
    public static bool IsAccessible(JsonNode? row, IReadOnlySet<Guid> allowed) =>
        Guid.TryParse(row?["vehicle"]?["id"]?.ToString(), out var id) && allowed.Contains(id);

    public static JsonObject Filter(string json, IReadOnlySet<Guid> allowed)
    {
        var result = JsonNode.Parse(json)!.AsObject();
        result["rows"] = new JsonArray((result["rows"]?.AsArray() ?? [])
            .Where(r => IsAccessible(r, allowed)).Select(r => r!.DeepClone()).ToArray());
        return result;
    }

    public static JsonObject Merge(string incoming, string? previous, IReadOnlySet<Guid> allowed)
    {
        var result = JsonNode.Parse(incoming)!.AsObject();
        if (result["rows"]!.AsArray().Any(r => !IsAccessible(r, allowed)))
            throw new UnauthorizedAccessException("Отчёт содержит технику другого участка.");
        if (previous is not null) {
            foreach (var row in JsonNode.Parse(previous)!["rows"]!.AsArray().Where(r => !IsAccessible(r, allowed)))
                result["rows"]!.AsArray().Add(row!.DeepClone());
        }
        return result;
    }
}
