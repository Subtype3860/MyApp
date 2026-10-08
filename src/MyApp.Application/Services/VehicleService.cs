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


}
