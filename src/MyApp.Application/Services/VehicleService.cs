using ExcelDataReader;
using ExcelDataReader.Exceptions;
using MyApp.Application.Abstractions;
using MyApp.Application.Common;
using MyApp.Application.DTO;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MyApp.Application.Services;

/// <summary>
/// Реализация <see cref="IVehicleService"/>: бизнес-логика журнала техники —
/// валидация заявок, прав пользователей на неисправности/работы, ограничений
/// на медиафайлы и делегирование хранения данных в <see cref="IVehicleRepository"/>.
/// </summary>
public sealed class VehicleService(
    IVehicleRepository repository,
    IUserRepository userRepository) : IVehicleService
{
    private const int MaximumPhotosPerEntry = 10;
    private const int MaximumPhotoSize = 8 * 1024 * 1024;
    private const int MaximumVideoSize = 100 * 1024 * 1024;
    private const int MaximumRequestFileSize = 20 * 1024 * 1024;
    private static readonly HashSet<string> AllowedPhotoTypes =
    [
        "image/jpeg",
        "image/png",
        "image/webp"
    ];
    private static readonly HashSet<string> AllowedVideoTypes =
    [
        "video/mp4",
        "video/webm",
        "video/quicktime"
    ];
    private static readonly HashSet<string> ExecutorProfessions = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "Слесарь",
        "Электрослесарь",
        "Сервисный инженер"
    };
    private static readonly (Regex Pattern, string Model)[] ExcelVehiclePatterns =
    [
        (new(@"^SET\s*(?<garage>\d+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "Sany SET150"),
        (new(@"^Sany\s*1250\s*/\s*(?<garage>\d+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "Sany SY 1250H"),
        (new(@"^Komatsu\s*D375\s*/\s*(?<garage>\d+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "Komatsu 375"),
        (new(@"^Liebherr\s*764\s*/\s*(?<garage>\d+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "Liebherr 764"),
        (new(@"^Caterpillar\s*[№#�]?\s*(?<garage>\d+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "Caterpillar 140")
    ];

    static VehicleService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public Task<IReadOnlyList<VehicleResponse>> GetVehiclesAsync(
        CancellationToken cancellationToken) =>
        repository.GetVehiclesAsync(cancellationToken);

    public Task<VehicleJournalResponse?> GetJournalAsync(
        Guid vehicleId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken) =>
        repository.GetJournalAsync(vehicleId, from, to, cancellationToken);

    public Task<ServiceResult<Guid>> AddPurchaseAsync(
        Guid vehicleId,
        VehiclePurchaseRequest request,
        Guid createdBy,
        CancellationToken cancellationToken) =>
        CreateAsync(
            vehicleId,
            ValidatePurchase(request),
            () => repository.AddPurchaseAsync(
                vehicleId,
                request with
                {
                    RequestNumber = Clean(request.RequestNumber),
                    ItemName = Clean(request.ItemName),
                    Status = Clean(request.Status),
                    Note = Clean(request.Note)
                },
                createdBy,
                cancellationToken),
            cancellationToken);

    public async Task<ServiceResult<Guid>> AddDefectAsync(
        Guid vehicleId,
        VehicleDefectRequest request,
        Guid createdBy,
        CancellationToken cancellationToken)
    {
        if (!await HasProfessionAsync(
                createdBy, ["Механик"], cancellationToken))
        {
            return ServiceResult<Guid>.Unauthorized();
        }
        var symptoms = Clean(request.Symptoms);
        var downtimeStartedAt = request.DowntimeStartedAt;
        return await CreateAsync(
            vehicleId,
            ValidateDefect(request, symptoms, downtimeStartedAt),
            () => repository.AddDefectAsync(
                vehicleId,
                request with
                {
                    ErrorCode = Clean(request.ErrorCode),
                    Symptoms = symptoms
                },
                createdBy,
                cancellationToken),
            cancellationToken);
    }

    public async Task<ServiceResult<bool>> ClaimDefectAsync(
        Guid defectId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (!await repository.DefectExistsAsync(defectId, cancellationToken))
        {
            return ServiceResult<bool>.NotFound();
        }
        return await repository.ClaimDefectAsync(
            defectId, userId, cancellationToken)
            ? ServiceResult<bool>.Success(true)
            : ServiceResult<bool>.Conflict("Задача уже назначена.");
    }

    public async Task<ServiceResult<IReadOnlyList<Guid>>> AddDefectPhotosAsync(
        Guid defectId,
        IReadOnlyList<VehicleWorkPhotoUpload> photos,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (!await repository.DefectExistsAsync(defectId, cancellationToken))
        {
            return ServiceResult<IReadOnlyList<Guid>>.NotFound();
        }
        if (!await CanManageDefectAsync(defectId, userId, cancellationToken))
        {
            return ServiceResult<IReadOnlyList<Guid>>.Unauthorized();
        }
        var validation = await ValidatePhotosAsync(
            photos,
            () => repository.GetDefectPhotoCountAsync(defectId, cancellationToken));
        return validation is not null
            ? PhotoValidation(validation)
            : ServiceResult<IReadOnlyList<Guid>>.Success(
                await repository.AddDefectPhotosAsync(
                    defectId, photos, cancellationToken));
    }

    public Task<VehicleWorkPhotoContent?> GetDefectPhotoAsync(
        Guid photoId,
        CancellationToken cancellationToken) =>
        repository.GetDefectPhotoAsync(photoId, cancellationToken);

    public Task<ServiceResult<bool>> DeleteDefectPhotoAsync(
        Guid photoId,
        Guid userId,
        CancellationToken cancellationToken) =>
        DeleteMediaAsync(
            "defect-photo", photoId, userId,
            repository.DeleteDefectPhotoAsync, cancellationToken);

    public Task<ServiceResult<IReadOnlyList<Guid>>> AddDefectVideosAsync(
        Guid defectId,
        IReadOnlyList<VehicleMediaUpload> videos,
        Guid userId,
        CancellationToken cancellationToken) =>
        AddVideosAsync(
            defectId,
            videos,
            userId,
            repository.DefectExistsAsync,
            CanManageDefectAsync,
            repository.GetDefectVideoCountAsync,
            repository.AddDefectVideosAsync,
            cancellationToken);

    public Task<VehicleWorkPhotoContent?> GetDefectVideoAsync(
        Guid videoId,
        CancellationToken cancellationToken) =>
        repository.GetDefectVideoAsync(videoId, cancellationToken);

    public Task<ServiceResult<bool>> DeleteDefectVideoAsync(
        Guid videoId,
        Guid userId,
        CancellationToken cancellationToken) =>
        DeleteMediaAsync(
            "defect-video", videoId, userId,
            repository.DeleteDefectVideoAsync, cancellationToken);

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

    public async Task<ServiceResult<Guid>> AddWorkAsync(
        Guid vehicleId,
        VehicleWorkRequest request,
        Guid createdBy,
        CancellationToken cancellationToken)
    {
        if (!await repository.DefectBelongsToVehicleAsync(
                request.DefectId, vehicleId, cancellationToken))
        {
            return ServiceResult<Guid>.NotFound();
        }
        return await CompleteDefectAsync(
            request.DefectId, request, createdBy, cancellationToken);
    }

    public async Task<ServiceResult<Guid>> CompleteDefectAsync(
        Guid defectId,
        VehicleWorkRequest request,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return ServiceResult<Guid>.Unauthorized();
        }
        request = request with { DefectId = defectId };
        var cause = Clean(request.Cause);
        var status = Clean(request.Status).ToLowerInvariant();
        request = request with
        {
            Description = Clean(request.Description),
            Cause = cause,
            Status = status,
            RequiredParts = Clean(request.RequiredParts)
        };
        var validation = ValidateWork(request);
        if (validation is not null)
        {
            return ServiceResult<Guid>.Validation(
                new Dictionary<string, string[]>
                {
                    ["vehicle"] = [validation]
                });
        }
        if (!await repository.DefectExistsAsync(defectId, cancellationToken))
        {
            return ServiceResult<Guid>.NotFound();
        }
        var id = await repository.CompleteDefectAsync(
            defectId, request, userId, IsAdministrator(user), cancellationToken);
        return id.HasValue
            ? ServiceResult<Guid>.Success(id.Value)
            : ServiceResult<Guid>.Conflict(
                "Задача не назначена вам или уже завершена.");
    }

    public async Task<bool> DeleteEntryAsync(
        string category,
        Guid id,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (category == "defects" &&
            !await CanManageDefectAsync(id, userId, cancellationToken) ||
            category == "works" &&
            !await CanManageWorkAsync(id, userId, cancellationToken))
        {
            return false;
        }
        return await repository.DeleteEntryAsync(
            category, id, cancellationToken);
    }

    public async Task<ServiceResult<IReadOnlyList<Guid>>> AddWorkPhotosAsync(
        Guid workId,
        IReadOnlyList<VehicleWorkPhotoUpload> photos,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (!await repository.WorkExistsAsync(workId, cancellationToken))
        {
            return ServiceResult<IReadOnlyList<Guid>>.NotFound();
        }
        if (!await CanManageWorkAsync(workId, userId, cancellationToken))
        {
            return ServiceResult<IReadOnlyList<Guid>>.Unauthorized();
        }
        var validation = await ValidatePhotosAsync(
            photos,
            () => repository.GetWorkPhotoCountAsync(workId, cancellationToken));
        return validation is not null
            ? PhotoValidation(validation)
            : ServiceResult<IReadOnlyList<Guid>>.Success(
                await repository.AddWorkPhotosAsync(
                    workId, photos, cancellationToken));
    }

    public Task<VehicleWorkPhotoContent?> GetWorkPhotoAsync(
        Guid photoId,
        CancellationToken cancellationToken) =>
        repository.GetWorkPhotoAsync(photoId, cancellationToken);

    public Task<ServiceResult<bool>> DeleteWorkPhotoAsync(
        Guid photoId,
        Guid userId,
        CancellationToken cancellationToken) =>
        DeleteMediaAsync(
            "work-photo", photoId, userId,
            repository.DeleteWorkPhotoAsync, cancellationToken);

    public Task<ServiceResult<IReadOnlyList<Guid>>> AddWorkVideosAsync(
        Guid workId,
        IReadOnlyList<VehicleMediaUpload> videos,
        Guid userId,
        CancellationToken cancellationToken) =>
        AddVideosAsync(
            workId,
            videos,
            userId,
            repository.WorkExistsAsync,
            CanManageWorkAsync,
            repository.GetWorkVideoCountAsync,
            repository.AddWorkVideosAsync,
            cancellationToken);

    public Task<VehicleWorkPhotoContent?> GetWorkVideoAsync(
        Guid videoId,
        CancellationToken cancellationToken) =>
        repository.GetWorkVideoAsync(videoId, cancellationToken);

    public Task<ServiceResult<bool>> DeleteWorkVideoAsync(
        Guid videoId,
        Guid userId,
        CancellationToken cancellationToken) =>
        DeleteMediaAsync(
            "work-video", videoId, userId,
            repository.DeleteWorkVideoAsync, cancellationToken);

    public async Task<ServiceResult<Guid>> AddPartsRequestAsync(
        Guid defectId,
        VehiclePartsRequest request,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (!await HasProfessionAsync(
                userId, ["Старший механик"], cancellationToken))
        {
            return ServiceResult<Guid>.Unauthorized();
        }

        var errors = new Dictionary<string, string[]>();
        if (Clean(request.RequestNumber).Length is < 1 or > 100)
        {
            errors[nameof(request.RequestNumber)] =
                ["Укажите номер заявки длиной не более 100 символов."];
        }
        if (request.Description.Trim().Length is < 1 or > 5000)
        {
            errors[nameof(request.Description)] =
                ["Укажите описание заявки длиной от 1 до 5000 символов."];
        }
        if (errors.Count > 0)
        {
            return ServiceResult<Guid>.Validation(errors);
        }

        var id = await repository.AddPartsRequestAsync(
            defectId,
            request with
            {
                RequestNumber = Clean(request.RequestNumber),
                Description = request.Description.Trim(),
                RequiredParts = request.RequiredParts?.Trim() ?? ""
            },
            userId,
            cancellationToken);
        return id is Guid value
            ? ServiceResult<Guid>.Success(value)
            : ServiceResult<Guid>.Conflict(
                "Заявку можно добавить только к неисправности со статусом awaiting_parts.");
    }

    public async Task<ServiceResult<bool>> UpdatePartsRequestAsync(
        Guid requestId,
        VehiclePartsRequest request,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (!await HasProfessionAsync(userId, ["Старший механик"], cancellationToken))
        {
            return ServiceResult<bool>.Unauthorized();
        }

        var errors = ValidatePartsRequest(request);
        if (errors.Count > 0)
        {
            return ServiceResult<bool>.Validation(errors);
        }

        var updated = await repository.UpdatePartsRequestAsync(
            requestId, NormalizePartsRequest(request), cancellationToken);
        return updated
            ? ServiceResult<bool>.Success(true)
            : ServiceResult<bool>.NotFound();
    }

    public async Task<ServiceResult<bool>> DeletePartsRequestAsync(
        Guid requestId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (!await HasProfessionAsync(userId, ["Старший механик"], cancellationToken))
        {
            return ServiceResult<bool>.Unauthorized();
        }

        var deleted = await repository.DeletePartsRequestAsync(requestId, cancellationToken);
        return deleted
            ? ServiceResult<bool>.Success(true)
            : ServiceResult<bool>.NotFound();
    }

    private static Dictionary<string, string[]> ValidatePartsRequest(
        VehiclePartsRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (Clean(request.RequestNumber).Length is < 1 or > 100)
        {
            errors[nameof(request.RequestNumber)] =
                ["Укажите номер заявки длиной не более 100 символов."];
        }
        if (request.Description.Trim().Length is < 1 or > 5000)
        {
            errors[nameof(request.Description)] =
                ["Укажите описание заявки длиной от 1 до 5000 символов."];
        }
        return errors;
    }

    private static VehiclePartsRequest NormalizePartsRequest(VehiclePartsRequest request) =>
        request with
        {
            RequestNumber = Clean(request.RequestNumber),
            Description = request.Description.Trim(),
            RequiredParts = request.RequiredParts?.Trim() ?? ""
        };

    private async Task<ServiceResult<Guid>> CreateAsync(
        Guid vehicleId,
        string? validationMessage,
        Func<Task<Guid>> create,
        CancellationToken cancellationToken)
    {
        if (validationMessage is not null)
        {
            return ServiceResult<Guid>.Validation(
                new Dictionary<string, string[]>
                {
                    ["vehicle"] = [validationMessage]
                });
        }
        if (!await repository.VehicleExistsAsync(vehicleId, cancellationToken))
        {
            return ServiceResult<Guid>.NotFound();
        }
        return ServiceResult<Guid>.Success(await create());
    }

    private static string? ValidatePurchase(VehiclePurchaseRequest request)
    {
        if (Clean(request.ItemName).Length is < 1 or > 500)
        {
            return "Наименование должно содержать от 1 до 500 символов.";
        }
        if (request.Quantity <= 0)
        {
            return "Количество должно быть больше нуля.";
        }
        return null;
    }

    private static string? ValidateDefect(
        VehicleDefectRequest request,
        string symptoms,
        DateTimeOffset downtimeStartedAt) =>
        ValidateText(symptoms, "Симптомы")
        ?? (Clean(request.ErrorCode).Length > 100
            ? "Код ошибки не должен превышать 100 символов."
            : downtimeStartedAt == default
                ? "Укажите время начала простоя."
                : downtimeStartedAt > DateTimeOffset.UtcNow.AddMinutes(5)
                    ? "Время начала простоя не может быть в будущем."
                    : null);

    private static string? ValidateWork(VehicleWorkRequest request)
    {
        if (request.DefectId == Guid.Empty)
        {
            return "Выберите неисправность.";
        }
        var description = ValidateText(request.Description, "Выполненные работы");
        var failure = ValidateText(request.Cause, "Причина отказа");
        var status = Clean(request.Status);
        var repairDateTimeError = request.RepairDateTime is { } repairDateTime &&
            repairDateTime > DateTimeOffset.UtcNow.AddMinutes(5)
                ? "Дата и время ремонта не могут быть в будущем."
                : null;
        return description ?? failure ?? repairDateTimeError ??
            (status is not ("repaired" or "faulty" or "awaiting_parts")
                ? "Статус должен быть repaired, faulty или awaiting_parts."
                : status == "awaiting_parts" &&
                  Clean(request.RequiredParts).Length == 0
                    ? "Для статуса awaiting_parts укажите необходимые запчасти."
                    : null);
    }

    private static string? ValidateText(string value, string label) =>
        Clean(value).Length is < 1 or > 4000
            ? $"{label} должно содержать от 1 до 4000 символов."
            : null;

    private static string Clean(string? value) => value?.Trim() ?? string.Empty;

    private static string SafeFileName(string value)
    {
        var name = value.Replace('\\', '/').Split('/').Last();
        name = new string(name.Where(character => !char.IsControl(character)).ToArray());
        return name.Length <= 255 ? name : name[..255];
    }

    private static async Task<string?> ValidatePhotosAsync(
        IReadOnlyList<VehicleWorkPhotoUpload> photos,
        Func<Task<int>> getExistingCount)
    {
        if (photos.Count == 0)
        {
            return "Выберите хотя бы одну фотографию.";
        }
        if (await getExistingCount() + photos.Count > MaximumPhotosPerEntry)
        {
            return $"Для одной записи можно сохранить не более {MaximumPhotosPerEntry} фотографий.";
        }
        return photos.Any(photo =>
            photo.Content.Length is <= 0 or > MaximumPhotoSize ||
            !AllowedPhotoTypes.Contains(photo.ContentType) ||
            !HasValidImageSignature(photo.ContentType, photo.Content))
            ? "Разрешены JPEG, PNG и WebP размером не более 8 МБ."
            : null;
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

    private static ServiceResult<IReadOnlyList<Guid>> PhotoValidation(
        string message) =>
        ServiceResult<IReadOnlyList<Guid>>.Validation(
            new Dictionary<string, string[]> { ["photos"] = [message] });

    private async Task<bool> HasProfessionAsync(
        Guid userId,
        IEnumerable<string> professions,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.FindByIdAsync(userId, cancellationToken);
        return user is not null &&
            (IsAdministrator(user) ||
             professions.Contains(
                 user.Profession.Name.Trim(),
                 StringComparer.OrdinalIgnoreCase));
    }

    private static bool IsAdministrator(MyApp.Domain.Entities.User user) =>
        user.Role.Trim().Equals(
            "administrator", StringComparison.OrdinalIgnoreCase);

    private static async Task<ServiceResult<IReadOnlyList<Guid>>> AddVideosAsync(
        Guid parentId,
        IReadOnlyList<VehicleMediaUpload> videos,
        Guid userId,
        Func<Guid, CancellationToken, Task<bool>> exists,
        Func<Guid, Guid, CancellationToken, Task<bool>> canManage,
        Func<Guid, CancellationToken, Task<int>> count,
        Func<Guid, IReadOnlyList<VehicleMediaUpload>, CancellationToken,
            Task<IReadOnlyList<Guid>>> add,
        CancellationToken cancellationToken)
    {
        if (!await exists(parentId, cancellationToken))
        {
            return ServiceResult<IReadOnlyList<Guid>>.NotFound();
        }
        if (!await canManage(parentId, userId, cancellationToken))
        {
            return ServiceResult<IReadOnlyList<Guid>>.Unauthorized();
        }
        if (videos.Count == 0 ||
            await count(parentId, cancellationToken) + videos.Count >
                MaximumPhotosPerEntry)
        {
            return ServiceResult<IReadOnlyList<Guid>>.Validation(
                new Dictionary<string, string[]>
                {
                    ["videos"] = ["Можно сохранить от 1 до 10 видео."]
                });
        }
        if (videos.Any(video =>
                video.Content.Length is <= 0 or > MaximumVideoSize ||
                !AllowedVideoTypes.Contains(video.ContentType) ||
                !HasValidVideoSignature(video.ContentType, video.Content)))
        {
            return ServiceResult<IReadOnlyList<Guid>>.Validation(
                new Dictionary<string, string[]>
                {
                    ["videos"] =
                        ["Разрешены MP4, WebM и QuickTime размером не более 100 МБ."]
                });
        }
        return ServiceResult<IReadOnlyList<Guid>>.Success(
            await add(parentId, videos, cancellationToken));
    }

    private static bool HasValidImageSignature(
        string contentType,
        byte[] content) =>
        contentType switch
        {
            "image/jpeg" => content.Length >= 3 &&
                content[0] == 0xff && content[1] == 0xd8 && content[2] == 0xff,
            "image/png" => content.Length >= 8 &&
                content.AsSpan(0, 8).SequenceEqual(
                    new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }),
            "image/webp" => content.Length >= 12 &&
                content.AsSpan(0, 4).SequenceEqual("RIFF"u8) &&
                content.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            _ => false
        };

    private static bool HasValidVideoSignature(
        string contentType,
        byte[] content) =>
        contentType switch
        {
            "video/webm" => content.Length >= 4 &&
                content.AsSpan(0, 4).SequenceEqual(
                    new byte[] { 0x1a, 0x45, 0xdf, 0xa3 }),
            "video/mp4" or "video/quicktime" => content.Length >= 12 &&
                content.AsSpan(4, 4).SequenceEqual("ftyp"u8),
            _ => false
        };

    private async Task<bool> CanManageDefectAsync(
        Guid defectId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.FindByIdAsync(userId, cancellationToken);
        return user is not null &&
            (IsAdministrator(user) ||
             user.Profession.Name.Trim().Equals(
                 "Механик", StringComparison.OrdinalIgnoreCase) &&
             await repository.IsDefectCreatorAsync(
                 defectId, userId, cancellationToken));
    }

    private async Task<bool> CanManageWorkAsync(
        Guid workId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.FindByIdAsync(userId, cancellationToken);
        return user is not null &&
            (IsAdministrator(user) ||
             ExecutorProfessions.Contains(user.Profession.Name.Trim()) &&
             await repository.IsWorkPerformerAsync(
                 workId, userId, cancellationToken));
    }

    private async Task<ServiceResult<bool>> DeleteMediaAsync(
        string category,
        Guid mediaId,
        Guid userId,
        Func<Guid, CancellationToken, Task<bool>> delete,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.FindByIdAsync(userId, cancellationToken);
        if (user is null ||
            !IsAdministrator(user) &&
            !await repository.CanManageMediaAsync(
                category, mediaId, userId, cancellationToken))
        {
            return ServiceResult<bool>.Unauthorized();
        }
        return await delete(mediaId, cancellationToken)
            ? ServiceResult<bool>.Success(true)
            : ServiceResult<bool>.NotFound();
    }

    private sealed record VehicleHoursImportRow(
        int Line,
        int GarageNumber,
        string Model,
        decimal? EngineHours);
}
