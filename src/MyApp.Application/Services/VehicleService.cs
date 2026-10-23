using ExcelDataReader;
using ExcelDataReader.Exceptions;
using MyApp.Application.Abstractions;
using MyApp.Application.Common;
using MyApp.Application.DTO;
using MyApp.Application.Security;
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
    /// <summary>Проверяет право пользователя изменять показания моточасов.</summary>
    public async Task<bool> CanManageHoursAsync(Guid userId, CancellationToken cancellationToken) =>
        await userRepository.FindByIdAsync(userId, cancellationToken) is { } user && UserAccess.CanManageHours(user);

    /// <summary>Проверяет право пользователя создавать, изменять и архивировать технику.</summary>
    private Task<bool> CanManageFleetAsync(Guid userId, CancellationToken cancellationToken) =>
        HasProfessionAsync(userId, ["Механик", "Старший механик"], cancellationToken);

    /// <summary>Возвращает справочники для редактирования парка и сведения о доступных действиях.</summary>
    public async Task<VehicleCatalogOptions> GetVehicleCatalogOptionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userRepository.FindByIdAsync(userId, cancellationToken);
        return (await repository.GetVehicleCatalogOptionsAsync(cancellationToken)) with
        {
            CanManage = user is not null && (IsAdministrator(user) || UserAccess.IsMechanic(user)),
            CanManageHours = user is not null && UserAccess.CanManageHours(user)
        };
    }

    /// <summary>Создаёт или обновляет карточку техники в каталоге парка.</summary>
    public async Task<ServiceResult<Guid>> SaveVehicleCatalogAsync(Guid? id, VehicleCatalogRequest request, Guid userId, CancellationToken cancellationToken)
    {
        if (!await CanManageFleetAsync(userId, cancellationToken)) return ServiceResult<Guid>.Forbidden("Управлять парком техники может механик или администратор.");
        request = request with { GroupName = Clean(request.GroupName), TypeName = Clean(request.TypeName), ModelName = Clean(request.ModelName), StateNumber = Clean(request.StateNumber), Vin = Clean(request.Vin) };
        var errors = new Dictionary<string, string[]>();
        if (request.GroupName.Length is < 1 or > 50) errors["groupName"] = ["Укажите группу техники (до 50 символов)."];
        if (request.TypeName.Length is < 1 or > 50) errors["typeName"] = ["Укажите тип техники (до 50 символов)."];
        if (request.ModelName.Length is < 1 or > 50) errors["modelName"] = ["Укажите модель (до 50 символов)."];
        if (request.StateNumber.Length > 15) errors["stateNumber"] = ["Госномер должен содержать не больше 15 символов."];
        if (request.Vin.Length > 20) errors["vin"] = ["VIN должен содержать не больше 20 символов."];
        if (request.GarageNumber < 0) errors["garageNumber"] = ["Гаражный номер не может быть отрицательным."];
        if (request.GarageNumber is null && request.StateNumber.Length == 0 && request.Vin.Length == 0) errors["vehicle"] = ["Укажите гаражный номер, госномер или VIN."];
        return errors.Count > 0 ? ServiceResult<Guid>.Validation(errors) : await repository.SaveVehicleCatalogAsync(id, request, cancellationToken);
    }

    /// <summary>Переводит технику в архив с сохранением связанных записей истории.</summary>
    public async Task<ServiceResult<bool>> ArchiveVehicleAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        if (!await CanManageFleetAsync(userId, cancellationToken)) return ServiceResult<bool>.Forbidden("Удалять технику из парка может механик или администратор.");
        return await repository.ArchiveVehicleAsync(id, cancellationToken) ? ServiceResult<bool>.Success(true) : ServiceResult<bool>.NotFound();
    }

    /// <summary>Возвращает вспомогательные работы за выбранный период.</summary>
    public Task<IReadOnlyList<VehicleWorkResponse>> GetAuxiliaryWorksAsync(CancellationToken cancellationToken) =>
        repository.GetAuxiliaryWorksAsync(cancellationToken);
    /// <summary>Проверяет, существует ли указанная вспомогательная работа.</summary>
    public Task<bool> AuxiliaryWorkExistsAsync(Guid workId, CancellationToken cancellationToken) =>
        repository.AuxiliaryWorkExistsAsync(workId, cancellationToken);
    /// <summary>Проверяет принадлежность медиафайла указанной вспомогательной работе.</summary>
    public Task<bool> AuxiliaryMediaExistsAsync(Guid mediaId, bool video, CancellationToken cancellationToken) =>
        repository.AuxiliaryMediaExistsAsync(mediaId, video, cancellationToken);

    /// <summary>Создаёт запись о вспомогательной работе сотрудника.</summary>
    public async Task<ServiceResult<Guid>> AddAuxiliaryWorkAsync(AuxiliaryWorkRequest request, Guid userId, CancellationToken cancellationToken)
    {
        if (await userRepository.FindByIdAsync(userId, cancellationToken) is null) return ServiceResult<Guid>.Unauthorized();
        request = request with { Title = Clean(request.Title), Description = Clean(request.Description), Location = Clean(request.Location).ToUpperInvariant() };
        var errors = new Dictionary<string, string[]>();
        if (request.Title.Length is < 1 or > 160) errors["title"] = ["Название должно содержать от 1 до 160 символов."];
        if (request.Description.Length is < 1 or > 4000) errors["description"] = ["Описание должно содержать от 1 до 4000 символов."];
        if (request.Location is not ("КАРЬЕР" or "РММ")) errors["location"] = ["Выберите место работ: КАРЬЕР или РММ."];
        if (request.PerformedAt == default || request.PerformedAt > DateTimeOffset.UtcNow.AddMinutes(5)) errors["performedAt"] = ["Укажите дату и время выполненной работы; будущее время недопустимо."];
        return errors.Count > 0 ? ServiceResult<Guid>.Validation(errors)
            : ServiceResult<Guid>.Success(await repository.AddAuxiliaryWorkAsync(request, userId, cancellationToken));
    }

    /// <summary>Возвращает участников работы и состояние согласования их участия.</summary>
    public async Task<ServiceResult<IReadOnlyList<VehicleWorkParticipantResponse>>> GetWorkParticipantsAsync(Guid workId, CancellationToken cancellationToken)
    {
        if (!await repository.WorkExistsAsync(workId, cancellationToken))
            return ServiceResult<IReadOnlyList<VehicleWorkParticipantResponse>>.NotFound();
        return ServiceResult<IReadOnlyList<VehicleWorkParticipantResponse>>.Success(await repository.GetWorkParticipantsAsync(workId, cancellationToken));
    }

    /// <summary>Создаёт заявку текущего сотрудника на участие в указанной работе.</summary>
    public async Task<ServiceResult<bool>> RequestWorkParticipationAsync(Guid workId, Guid userId, CancellationToken cancellationToken)
    {
        if (await userRepository.FindByIdAsync(userId, cancellationToken) is null)
            return ServiceResult<bool>.Unauthorized();
        if (!await repository.WorkExistsAsync(workId, cancellationToken))
            return ServiceResult<bool>.NotFound();
        return await repository.RequestWorkParticipationAsync(workId, userId, cancellationToken)
            ? ServiceResult<bool>.Success(true)
            : ServiceResult<bool>.Conflict("Ваше участие уже подтверждено или ожидает подтверждения механика.");
    }

    /// <summary>Сохраняет решение по заявке на участие в работе.</summary>
    public async Task<ServiceResult<bool>> ReviewWorkParticipationAsync(Guid workId, Guid participantId, bool approved, Guid reviewerId, CancellationToken cancellationToken)
    {
        var reviewer = await userRepository.FindByIdAsync(reviewerId, cancellationToken);
        if (reviewer is null) return ServiceResult<bool>.Unauthorized();
        // Profession is checked against the database, including for administrators.
        if (!reviewer.Profession.Name.Trim().Equals("Механик", StringComparison.OrdinalIgnoreCase))
            return ServiceResult<bool>.BadRequest("Подтверждать участие в работе может только механик.");
        if (!await repository.WorkExistsAsync(workId, cancellationToken))
            return ServiceResult<bool>.NotFound();
        return await repository.ReviewWorkParticipationAsync(workId, participantId, approved, reviewerId, cancellationToken)
            ? ServiceResult<bool>.Success(true)
            : ServiceResult<bool>.Conflict("Заявка уже рассмотрена или не найдена. Обновите список исполнителей.");
    }

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

    /// <summary>Создаёт VehicleService и сохраняет переданные зависимости.</summary>
    static VehicleService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>Возвращает список техники.</summary>
    public Task<IReadOnlyList<VehicleResponse>> GetVehiclesAsync(
        CancellationToken cancellationToken, bool includeArchived = false) =>
        repository.GetVehiclesAsync(cancellationToken, includeArchived);

    /// <summary>Возвращает журнал событий выбранной техники.</summary>
    public Task<VehicleJournalResponse?> GetJournalAsync(
        Guid vehicleId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken) =>
        repository.GetJournalAsync(vehicleId, from, to, cancellationToken);

    /// <summary>Возвращает журналы ремонта с учётом заданных фильтров.</summary>
    public Task<IReadOnlyList<VehicleJournalResponse>> GetRepairJournalsAsync(
        CancellationToken cancellationToken, bool includeArchived = false) =>
        repository.GetRepairJournalsAsync(cancellationToken, includeArchived);

    /// <summary>Добавляет запись о закупке для техники.</summary>
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

    /// <summary>Регистрирует неисправность техники.</summary>
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

    /// <summary>Закрепляет неисправность за исполнителем.</summary>
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

    /// <summary>Сохраняет фотографии неисправности.</summary>
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

    /// <summary>Возвращает содержимое фотографии неисправности.</summary>
    public Task<VehicleWorkPhotoContent?> GetDefectPhotoAsync(
        Guid photoId,
        CancellationToken cancellationToken) =>
        repository.GetDefectPhotoAsync(photoId, cancellationToken);

    /// <summary>Удаляет фотографию неисправности.</summary>
    public Task<ServiceResult<bool>> DeleteDefectPhotoAsync(
        Guid photoId,
        Guid userId,
        CancellationToken cancellationToken) =>
        DeleteMediaAsync(
            "defect-photo", photoId, userId,
            repository.DeleteDefectPhotoAsync, cancellationToken);

    /// <summary>Сохраняет видеозаписи неисправности.</summary>
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

    /// <summary>Открывает поток чтения видео неисправности.</summary>
    public Task<VehicleMediaStream?> GetDefectVideoStreamAsync(
        Guid videoId,
        CancellationToken cancellationToken) =>
        repository.GetDefectVideoStreamAsync(videoId, cancellationToken);

    /// <summary>Удаляет видеозапись неисправности.</summary>
    public Task<ServiceResult<bool>> DeleteDefectVideoAsync(
        Guid videoId,
        Guid userId,
        CancellationToken cancellationToken) =>
        DeleteMediaAsync(
            "defect-video", videoId, userId,
            repository.DeleteDefectVideoAsync, cancellationToken);

    /// <summary>Добавляет показание моточасов техники.</summary>
    public async Task<ServiceResult<Guid>> AddHoursAsync(
        Guid vehicleId,
        VehicleHoursRequest request,
        Guid createdBy,
        CancellationToken cancellationToken)
    {
        if (!await CanManageHoursAsync(createdBy, cancellationToken))
            return ServiceResult<Guid>.Forbidden("Нет права изменять моточасы.");
        if (request.EngineNumber is < 0 or > 2) return ServiceResult<Guid>.BadRequest("Номер ДВС должен быть 1 или 2.");
        if (request.EngineNumber == 2) {
            var vehicle = (await repository.GetVehiclesAsync(cancellationToken)).FirstOrDefault(x => x.Id == vehicleId);
            if (vehicle?.EngineCount != 2) return ServiceResult<Guid>.BadRequest("У этой техники один ДВС.");
        }
        return await CreateAsync(
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
    }

    /// <summary>Проверяет ежедневные показания либо сохраняет их с контролем предварительного результата.</summary>
    public async Task<ServiceResult<DailyHoursPreview>> ProcessDailyHoursAsync(DailyHoursRequest request,
        bool commit, Guid userId, CancellationToken cancellationToken)
    {
        if (!await CanManageHoursAsync(userId, cancellationToken))
            return ServiceResult<DailyHoursPreview>.Forbidden("Нет права изменять моточасы.");
        if (request.Rows is null || request.Rows.Count is < 1 or > 10000 ||
            request.Rows.Any(r => r is null || r.ReadingDate == default || r.GarageNumber < 0 ||
                r.Source is null || r.Source.Length > 100))
            return ServiceResult<DailyHoursPreview>.BadRequest("Некорректные данные импорта (от 1 до 10 000 показаний).");
        return await repository.ProcessDailyHoursAsync(request, commit, userId, cancellationToken);
    }

    /// <summary>Читает CSV или Excel с моточасами и импортирует распознанные показания.</summary>
    public async Task<ServiceResult<VehicleHoursImportResponse>> ImportHoursAsync(
        Stream file,
        string fileExtension,
        Guid createdBy,
        DateOnly readingDate,
        CancellationToken cancellationToken)
    {
        if (!await CanManageHoursAsync(createdBy, cancellationToken))
            return ServiceResult<VehicleHoursImportResponse>.Forbidden("Нет права изменять моточасы.");
        var errors = new List<VehicleHoursImportError>();
        var rows = string.Equals(
            fileExtension,
            ".xlsx",
            StringComparison.OrdinalIgnoreCase)
            ? ReadExcelRows(file, errors)
            : await ReadCsvRowsAsync(file, errors, cancellationToken);
        var vehicles = await repository.GetVehiclesAsync(cancellationToken);
        var importedByVehicle = new Dictionary<(Guid VehicleId, int EngineNumber), VehicleHoursImportItem>();

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
            if (matches[0].EngineCount == 1 && rows.Any(other => other.Line == row.Line && other.EngineNumber == 2))
            {
                if (row.EngineNumber == 1) errors.Add(new(row.Line, "У этой техники один ДВС."));
                continue;
            }
            var number = row.EngineNumber;
            if (number == 0) {
                if (matches[0].EngineCount == 2) { errors.Add(new(row.Line, "Для самосвала укажите ДВС № 1 и № 2: отдельными колонками или через /.") ); continue; }
                number = 1;
            }
            if (number > matches[0].EngineCount) { errors.Add(new(row.Line, "У этой техники один ДВС.")); continue; }
            if (importedByVehicle.ContainsKey((matches[0].Id, number)))
            {
                errors.Add(new(
                    row.Line,
                    "Эта машина уже указана в импортируемом файле."));
                continue;
            }
            importedByVehicle[(matches[0].Id, number)] = new(
                matches[0].Id,
                row.EngineHours, number);
        }

        if (importedByVehicle.Count > 0)
        {
            await repository.ImportHoursAsync(
                readingDate,
                importedByVehicle.Values.ToArray(),
                createdBy,
                cancellationToken);
        }
        return ServiceResult<VehicleHoursImportResponse>.Success(new VehicleHoursImportResponse(
            importedByVehicle.Count,
            errors));
    }

    /// <summary>Добавляет запись о выполненной работе по технике.</summary>
    public async Task<ServiceResult<Guid>> AddWorkAsync(
        Guid vehicleId,
        VehicleWorkRequest request,
        Guid createdBy,
        CancellationToken cancellationToken)
    {
        if (!await repository.VehicleExistsAsync(vehicleId, cancellationToken)) return ServiceResult<Guid>.NotFound();
        if (!await repository.DefectBelongsToVehicleAsync(
                request.DefectId, vehicleId, cancellationToken))
        {
            return ServiceResult<Guid>.NotFound();
        }
        return await CompleteDefectAsync(
            request.DefectId, request, createdBy, cancellationToken);
    }

    /// <summary>Фиксирует завершение устранения неисправности.</summary>
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

    /// <summary>Обновляет редактируемые данные записи истории техники.</summary>
    public async Task<bool> UpdateHistoryEntryAsync(string category, Guid id, string description, Guid userId, CancellationToken cancellationToken)
    {
        var user = await userRepository.FindByIdAsync(userId, cancellationToken);
        if (user is null || string.IsNullOrWhiteSpace(description) || category is not ("defects" or "works")) return false;
        if (!IsAdministrator(user) && !(category == "defects" &&
            (UserAccess.IsMechanic(user) || await repository.IsDefectCreatorAsync(id, userId, cancellationToken)))) return false;
        return await repository.UpdateHistoryEntryAsync(category, id, description.Trim(), cancellationToken);
    }

    /// <summary>Удаляет указанную запись истории техники.</summary>
    public async Task<bool> DeleteEntryAsync(
        string category,
        Guid id,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (category == "hours" && !await CanManageHoursAsync(userId, cancellationToken)) return false;
        if (category == "defects" &&
            !await CanManageDefectAsync(id, userId, cancellationToken) ||
            category == "works" &&
            !await CanDeleteWorkAsync(id, userId, cancellationToken))
        {
            return false;
        }
        return await repository.DeleteEntryAsync(
            category, id, cancellationToken);
    }

    /// <summary>Сохраняет фотографии выполненной работы.</summary>
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

    /// <summary>Возвращает содержимое фотографии выполненной работы.</summary>
    public Task<VehicleWorkPhotoContent?> GetWorkPhotoAsync(
        Guid photoId,
        CancellationToken cancellationToken) =>
        repository.GetWorkPhotoAsync(photoId, cancellationToken);

    /// <summary>Удаляет фотографию выполненной работы.</summary>
    public Task<ServiceResult<bool>> DeleteWorkPhotoAsync(
        Guid photoId,
        Guid userId,
        CancellationToken cancellationToken) =>
        DeleteMediaAsync(
            "work-photo", photoId, userId,
            repository.DeleteWorkPhotoAsync, cancellationToken);

    /// <summary>Сохраняет видеозаписи выполненной работы.</summary>
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

    /// <summary>Открывает поток чтения видео выполненной работы.</summary>
    public Task<VehicleMediaStream?> GetWorkVideoStreamAsync(
        Guid videoId,
        CancellationToken cancellationToken) =>
        repository.GetWorkVideoStreamAsync(videoId, cancellationToken);

    /// <summary>Удаляет видеозапись выполненной работы.</summary>
    public Task<ServiceResult<bool>> DeleteWorkVideoAsync(
        Guid videoId,
        Guid userId,
        CancellationToken cancellationToken) =>
        DeleteMediaAsync(
            "work-video", videoId, userId,
            repository.DeleteWorkVideoAsync, cancellationToken);

    /// <summary>Добавляет заявку на закупку запчастей для неисправности.</summary>
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

        var errors = ValidatePartsRequest(request);
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

    /// <summary>Обновляет реквизиты и состояние заявки на закупку запчастей.</summary>
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

    /// <summary>Удаляет заявку на закупку запчастей.</summary>
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

    /// <summary>Проверяет даты и текстовые поля заявки на закупку запчастей.</summary>
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
        if (request.CompletedDate.HasValue && request.CompletedDate.Value < request.RequestDate)
        {
            errors[nameof(request.CompletedDate)] = ["Дата выполнения не может быть раньше даты заявки."];
        }
        return errors;
    }

    /// <summary>Удаляет лишние пробелы в полях заявки на закупку запчастей.</summary>
    private static VehiclePartsRequest NormalizePartsRequest(VehiclePartsRequest request) =>
        request with
        {
            RequestNumber = Clean(request.RequestNumber),
            Description = request.Description.Trim(),
            RequiredParts = request.RequiredParts?.Trim() ?? ""
        };

    /// <summary>Проверяет результат валидации и существование техники перед созданием записи.</summary>
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

    /// <summary>Проверяет поля записи о закупке для техники.</summary>
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

    /// <summary>Проверяет данные неисправности перед сохранением.</summary>
    private static string? ValidateDefect(
        VehicleDefectRequest request,
        string symptoms,
        DateTimeOffset? downtimeStartedAt) =>
        ValidateText(symptoms, "Симптомы")
        ?? (Clean(request.ErrorCode).Length > 100
            ? "Код ошибки не должен превышать 100 символов."
            : request.OperationalState is not ("operating" or "stopped")
                ? "Укажите, работает машина или остановлена."
                : request.OperationalState == "operating" ? null
                : downtimeStartedAt is null || downtimeStartedAt == default(DateTimeOffset)
                ? "Укажите время начала простоя."
                : downtimeStartedAt > DateTimeOffset.UtcNow.AddMinutes(5)
                    ? "Время начала простоя не может быть в будущем."
                    : null);

    /// <summary>Проверяет описание, место и состояние выполненной работы.</summary>
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
        var operationalError = request.OperationalState is not (null or "operating" or "stopped")
            ? "Укажите, работает машина или остановлена."
            : request.OperationalState == "stopped" && request.DowntimeStartedAt is null
                ? "Укажите время начала простоя."
                : request.DowntimeStartedAt > DateTimeOffset.UtcNow.AddMinutes(5)
                    ? "Время начала простоя не может быть в будущем." : null;
        var locationError = Clean(request.Location) is not ("" or "КАРЬЕР" or "РММ")
            ? "Место работ должно быть КАРЬЕР или РММ." : null;
        return description ?? failure ?? repairDateTimeError ?? operationalError ?? locationError ??
            (status is not ("repaired" or "faulty" or "awaiting_parts")
                ? "Статус должен быть repaired, faulty или awaiting_parts."
                : status == "awaiting_parts" &&
                  Clean(request.RequiredParts).Length == 0
                    ? "Для статуса awaiting_parts укажите необходимые запчасти."
                    : null);
    }

    /// <summary>Проверяет обязательность и допустимую длину текстового поля.</summary>
    private static string? ValidateText(string value, string label) =>
        Clean(value).Length is < 1 or > 4000
            ? $"{label} должно содержать от 1 до 4000 символов."
            : null;

    /// <summary>Обрезает пробелы и приводит пустое текстовое значение к принятому формату.</summary>
    private static string Clean(string? value) => value?.Trim() ?? string.Empty;

    /// <summary>Удаляет компоненты пути из имени загруженного файла.</summary>
    private static string SafeFileName(string value)
    {
        var name = value.Replace('\\', '/').Split('/').Last();
        name = new string(name.Where(character => !char.IsControl(character)).ToArray());
        return name.Length <= 255 ? name : name[..255];
    }

    /// <summary>Проверяет количество, размеры и формат загружаемых фотографий.</summary>
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

    /// <summary>Читает строки импорта моточасов из CSV.</summary>
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

            AddImportedHoursRow(columns, lineNumber, rows, errors);
        }

        return rows;
    }

    /// <summary>Добавляет распознанное показание либо ошибку строки в результат разбора импорта.</summary>
    private static void AddImportedHoursRow(IReadOnlyList<string> columns, int line, List<VehicleHoursImportRow> rows, List<VehicleHoursImportError> errors)
    {
        if (columns.Count is not (3 or 4) || !int.TryParse(columns[0].Trim(), out var garage)) {
            errors.Add(new(line, "Ожидаются колонки: garage_number;model;engine_1;engine_2 (или три колонки с моточасами).")); return;
        }
        var values = columns.Count == 4 ? new[] { columns[2], columns[3] } : columns[2].Split('/');
        if (values.Length > 2) { errors.Add(new(line, "Укажите не более двух ДВС.")); return; }
        var parsed = new decimal?[values.Length];
        for (var i=0;i<values.Length;i++) if (!TryParseEngineHours(values[i], out parsed[i])) {errors.Add(new(line, "Некорректное значение моточасов.")); return;}
        for (var i=0;i<values.Length;i++) {
            if (values.Length==2 && parsed[i] is null) continue;
            rows.Add(new(line, garage, columns[1].Trim(), parsed[i], values.Length==2 ? i+1 : 0));
        }
    }

    /// <summary>Читает показания моточасов из книги Excel.</summary>
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
                if (equipment.Equals("garage_number", StringComparison.OrdinalIgnoreCase)) continue;
                if (reader.FieldCount >= 3 && int.TryParse(equipment, out _)) {
                    var columns=Enumerable.Range(0, Math.Min(4, reader.FieldCount)).Select(i=>Convert.ToString(reader.GetValue(i),CultureInfo.InvariantCulture) ?? "").ToArray();
                    AddImportedHoursRow(columns,lineNumber,rows,errors); continue;
                }
                if (!TryMapExcelVehicle(equipment, out var garageNumber, out var model)) {
                    errors.Add(new(lineNumber, $"Неизвестное обозначение техники «{equipment}».")); continue;
                }
                var rawHours=reader.FieldCount>1 ? Convert.ToString(reader.GetValue(1),CultureInfo.InvariantCulture) ?? "" : "";
                AddImportedHoursRow(new[]{garageNumber.ToString(CultureInfo.InvariantCulture),model,rawHours},lineNumber,rows,errors);
            }
        }
        catch (HeaderException)
        {
            errors.Add(new(0, "Не удалось прочитать XLSX-файл."));
        }

        return rows;
    }

    /// <summary>Сопоставляет строку Excel с техникой и её двигателями.</summary>
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

    /// <summary>Разбирает значение моточасов из ячейки импорта.</summary>
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

    /// <summary>Определяет разделитель столбцов CSV.</summary>
    private static char DetectSeparator(string line)
    {
        var semicolons = line.Count(character => character == ';');
        var commas = line.Count(character => character == ',');
        return semicolons >= commas ? ';' : ',';
    }

    /// <summary>Разбирает строку CSV с учётом кавычек и разделителей.</summary>
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

    /// <summary>Определяет, является ли строка заголовком таблицы импорта.</summary>
    private static bool IsHeader(IReadOnlyList<string> columns) =>
        columns.Count >= 3 &&
        columns[0].Trim().Equals(
            "garage_number",
            StringComparison.OrdinalIgnoreCase) &&
        columns[1].Trim().Equals(
            "model",
            StringComparison.OrdinalIgnoreCase);

    /// <summary>Разбирает десятичное число в поддерживаемых форматах записи.</summary>
    private static bool TryParseDecimal(string value, out decimal result) =>
        decimal.TryParse(
            value.Trim().Replace(',', '.'),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out result);

    /// <summary>Создаёт результат ошибки проверки загружаемых фотографий.</summary>
    private static ServiceResult<IReadOnlyList<Guid>> PhotoValidation(
        string message) =>
        ServiceResult<IReadOnlyList<Guid>>.Validation(
            new Dictionary<string, string[]> { ["photos"] = [message] });

    /// <summary>Проверяет наличие у пользователя требуемой профессии.</summary>
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

    /// <summary>Проверяет административную роль пользователя.</summary>
    private static bool IsAdministrator(MyApp.Application.Models.User user) =>
        user.Role.Trim().Equals(
            "administrator", StringComparison.OrdinalIgnoreCase);

    /// <summary>Проверяет и сохраняет загружаемые видео через переданные операции доступа к данным.</summary>
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

    /// <summary>Проверяет сигнатуру содержимого изображения.</summary>
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

    /// <summary>Проверяет сигнатуру содержимого видео.</summary>
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

    /// <summary>Проверяет право пользователя управлять указанной неисправностью.</summary>
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

    /// <summary>Проверяет право пользователя удалить указанную работу.</summary>
    private async Task<bool> CanDeleteWorkAsync(Guid workId, Guid userId, CancellationToken cancellationToken)
    {
        var user = await userRepository.FindByIdAsync(userId, cancellationToken);
        return user is not null && (IsAdministrator(user) ||
            await repository.IsWorkCreatorAsync(workId, userId, cancellationToken));
    }

    /// <summary>Проверяет право пользователя управлять указанной работой.</summary>
    private async Task<bool> CanManageWorkAsync(
        Guid workId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.FindByIdAsync(userId, cancellationToken);
        return user is not null &&
            (IsAdministrator(user) ||
             await repository.AuxiliaryWorkExistsAsync(workId, cancellationToken) &&
             await repository.IsWorkCreatorAsync(workId, userId, cancellationToken) ||
             ExecutorProfessions.Contains(user.Profession.Name.Trim()) &&
             await repository.IsWorkPerformerAsync(workId, userId, cancellationToken));
    }

    /// <summary>Удаляет медиафайл и связанные с ним данные.</summary>
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

    /// <summary>Распознанное показание моточасов из импортируемого файла.</summary>
    private sealed record VehicleHoursImportRow(
        int Line,
        int GarageNumber,
        string Model,
        decimal? EngineHours,
        int EngineNumber = 0);
}
