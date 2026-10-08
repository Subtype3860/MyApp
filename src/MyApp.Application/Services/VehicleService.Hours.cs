using ExcelDataReader;
using ExcelDataReader.Exceptions;
using MyApp.Application.Abstractions;
using MyApp.Application.Common;
using MyApp.Application.DTO;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MyApp.Application.Services;

public sealed partial class VehicleService
{
    public Task<ServiceResult<Guid>> AddHoursAsync(
        Guid vehicleId,
        VehicleHoursRequest request,
        Guid createdBy,
        CancellationToken cancellationToken) =>
        CreateAsync(
            vehicleId,
            request.EngineHours < 0
                ? "Моточасы не могут быть отрицательными."
                : null,
            () => repository.AddHoursAsync(
                vehicleId,
                request with { Note = Clean(request.Note) },
                createdBy,
                cancellationToken),
            cancellationToken);

    public async Task<VehicleHoursImportResponse> ImportHoursAsync(
        Stream file,
        string fileExtension,
        Guid createdBy,
        DateOnly readingDate,
        CancellationToken cancellationToken)
    {
        var errors = new List<VehicleHoursImportError>();
        var rows = string.Equals(
            fileExtension,
            ".xlsx",
            StringComparison.OrdinalIgnoreCase)
            ? ReadExcelRows(file, errors)
            : await ReadCsvRowsAsync(file, errors, cancellationToken);
        var vehicles = await repository.GetVehiclesAsync(cancellationToken);
        var importedByVehicle = new Dictionary<Guid, VehicleHoursImportItem>();

        foreach (var row in rows)
        {
            var matches = vehicles
                .Where(vehicle =>
                    vehicle.GarageNumber == row.GarageNumber &&
                    string.Equals(
                        vehicle.ModelName.Trim(),
                        row.Model,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length == 0)
            {
                errors.Add(new(
                    row.Line,
                    $"Техника «{row.Model}», гаражный № {row.GarageNumber}, не найдена."));
                continue;
            }
            if (matches.Length > 1)
            {
                errors.Add(new(
                    row.Line,
                    $"Найдено несколько машин «{row.Model}» с гаражным № {row.GarageNumber}."));
                continue;
            }
            if (importedByVehicle.ContainsKey(matches[0].Id))
            {
                errors.Add(new(
                    row.Line,
                    "Эта машина уже указана в импортируемом файле."));
                continue;
            }
            importedByVehicle[matches[0].Id] = new(
                matches[0].Id,
                row.EngineHours);
        }

        if (importedByVehicle.Count > 0)
        {
            await repository.ImportHoursAsync(
                readingDate,
                importedByVehicle.Values.ToArray(),
                createdBy,
                cancellationToken);
        }
        return new VehicleHoursImportResponse(
            importedByVehicle.Count,
            errors);
    }

    private static async Task<IReadOnlyList<VehicleHoursImportRow>> ReadCsvRowsAsync(
        Stream csv,
        List<VehicleHoursImportError> errors,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(
            csv,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: true);
        var content = await reader.ReadToEndAsync(cancellationToken);
        var lines = content.Split(
            ["\r\n", "\n", "\r"],
            StringSplitOptions.None);
        var rows = new List<VehicleHoursImportRow>();
        var firstDataLine = true;

        for (var index = 0; index < lines.Length; index++)
        {
            var lineNumber = index + 1;
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var columns = ParseCsvLine(line, DetectSeparator(line));
            if (firstDataLine && IsHeader(columns))
            {
                firstDataLine = false;
                continue;
            }
            firstDataLine = false;

            if (columns.Count != 3)
            {
                errors.Add(new(
                    lineNumber,
                    "Ожидаются 3 колонки: garage_number;model;engine_hours."));
                continue;
            }
            if (!int.TryParse(columns[0].Trim(), out var garageNumber))
            {
                errors.Add(new(lineNumber, "Некорректный гаражный номер."));
                continue;
            }
            if (!TryParseEngineHours(columns[2], out var engineHours))
            {
                errors.Add(new(lineNumber, "Некорректное значение моточасов."));
                continue;
            }

            rows.Add(new(
                lineNumber,
                garageNumber,
                columns[1].Trim(),
                engineHours));
        }

        return rows;
    }

    private static IReadOnlyList<VehicleHoursImportRow> ReadExcelRows(
        Stream excel,
        List<VehicleHoursImportError> errors)
    {
        var rows = new List<VehicleHoursImportRow>();
        try
        {
            using var reader = ExcelReaderFactory.CreateReader(
                excel,
                new ExcelReaderConfiguration
                {
                    FallbackEncoding = Encoding.UTF8
                });
            var lineNumber = 0;
            while (reader.Read())
            {
                lineNumber++;
                var equipment = reader.FieldCount > 0
                    ? Convert.ToString(
                        reader.GetValue(0),
                        CultureInfo.InvariantCulture)?.Trim()
                    : null;
                if (string.IsNullOrWhiteSpace(equipment))
                {
                    continue;
                }
                if (!TryMapExcelVehicle(equipment, out var garageNumber, out var model))
                {
                    errors.Add(new(
                        lineNumber,
                        $"Неизвестное обозначение техники «{equipment}»."));
                    continue;
                }

                var rawHours = reader.FieldCount > 1
                    ? Convert.ToString(
                        reader.GetValue(1),
                        CultureInfo.InvariantCulture)
                    : null;
                if (!TryParseEngineHours(rawHours, out var engineHours))
                {
                    errors.Add(new(lineNumber, "Некорректное значение моточасов."));
                    continue;
                }
                rows.Add(new(lineNumber, garageNumber, model, engineHours));
            }
        }
        catch (HeaderException)
        {
            errors.Add(new(0, "Не удалось прочитать XLSX-файл."));
        }

        return rows;
    }

    private static bool TryMapExcelVehicle(
        string equipment,
        out int garageNumber,
        out string model)
    {
        foreach (var mapping in ExcelVehiclePatterns)
        {
            var match = mapping.Pattern.Match(equipment.Trim());
            if (match.Success &&
                int.TryParse(match.Groups["garage"].Value, out garageNumber))
            {
                model = mapping.Model;
                return true;
            }
        }

        garageNumber = 0;
        model = string.Empty;
        return false;
    }

    private static bool TryParseEngineHours(
        string? value,
        out decimal? engineHours)
    {
        engineHours = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }
        if (!TryParseDecimal(value, out var parsedEngineHours) ||
            parsedEngineHours < 0)
        {
            return false;
        }
        engineHours = parsedEngineHours;
        return true;
    }

    private static char DetectSeparator(string line)
    {
        var semicolons = line.Count(character => character == ';');
        var commas = line.Count(character => character == ',');
        return semicolons >= commas ? ';' : ',';
    }

    private static IReadOnlyList<string> ParseCsvLine(string line, char separator)
    {
        var values = new List<string>();
        var value = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    value.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (character == separator && !quoted)
            {
                values.Add(value.ToString());
                value.Clear();
            }
            else
            {
                value.Append(character);
            }
        }
        values.Add(value.ToString());
        return values;
    }

    private static bool IsHeader(IReadOnlyList<string> columns) =>
        columns.Count >= 3 &&
        columns[0].Trim().Equals(
            "garage_number",
            StringComparison.OrdinalIgnoreCase) &&
        columns[1].Trim().Equals(
            "model",
            StringComparison.OrdinalIgnoreCase);

    private static bool TryParseDecimal(string value, out decimal result) =>
        decimal.TryParse(
            value.Trim().Replace(',', '.'),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out result);

    private sealed record VehicleHoursImportRow(
        int Line,
        int GarageNumber,
        string Model,
        decimal? EngineHours);
}
