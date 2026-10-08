using ExcelDataReader;
using ExcelDataReader.Exceptions;
using MyApp.Application.Abstractions;
using MyApp.Application.Common;
using MyApp.Application.DTO;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MyApp.Application.Services;

public sealed partial class VehicleService(
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
        if (!await HasProfessionAsync(
                userId, ExecutorProfessions, cancellationToken))
        {
            return ServiceResult<bool>.Unauthorized();
        }
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
        if (user is null || !IsAdministrator(user) &&
            !ExecutorProfessions.Contains(user.Profession.Name.Trim()))
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

    public async Task<ServiceResult<bool>> UpdatePartsRequestAsync(
        Guid workId,
        VehiclePartsRequest request,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (!await HasProfessionAsync(
                userId, ["Старший механик"], cancellationToken))
        {
            return ServiceResult<bool>.Unauthorized();
        }
        var errors = new Dictionary<string, string[]>();
        if (Clean(request.RequestNumber).Length is < 1 or > 100)
        {
            errors[nameof(request.RequestNumber)] =
                ["Укажите номер заявки длиной не более 100 символов."];
        }
        if (request.Content is { Length: > MaximumRequestFileSize })
        {
            errors["file"] = ["Размер файла не должен превышать 20 МБ."];
        }
        if (request.Content is { Length: > 0 } &&
            string.IsNullOrWhiteSpace(request.FileName))
        {
            errors["file"] = ["Укажите имя файла."];
        }
        if (errors.Count > 0)
        {
            return ServiceResult<bool>.Validation(errors);
        }
        var updated = await repository.UpdatePartsRequestAsync(
            workId,
            request with
            {
                RequestNumber = Clean(request.RequestNumber),
                FileName = request.FileName is null
                    ? null
                    : SafeFileName(request.FileName)
            },
            cancellationToken);
        return updated
            ? ServiceResult<bool>.Success(true)
            : ServiceResult<bool>.Conflict(
                "Заявку можно добавить только к работе со статусом awaiting_parts.");
    }

    public Task<VehicleRequestFileContent?> GetPartsRequestFileAsync(
        Guid workId,
        CancellationToken cancellationToken) =>
        repository.GetPartsRequestFileAsync(workId, cancellationToken);

    public async Task<ServiceResult<bool>> DeletePartsRequestAsync(
        Guid workId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (!await HasProfessionAsync(userId, ["Старший механик"], cancellationToken))
        {
            return ServiceResult<bool>.Unauthorized();
        }
        return await repository.DeletePartsRequestAsync(workId, cancellationToken)
            ? ServiceResult<bool>.Success(true)
            : ServiceResult<bool>.NotFound();
    }

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
        return description ?? failure ??
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


}
