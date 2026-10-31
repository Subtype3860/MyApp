using System.Data;
using Microsoft.EntityFrameworkCore;
using MyApp.Application.Abstractions;
using MyApp.Application.DTO;
using MyApp.Application.Common;
using MyApp.Application.Storage;
using MyApp.Infrastructure.Db;
using MyApp.Infrastructure.Db.Entities;

namespace MyApp.Infrastructure.Repositories;

/// <summary>
/// Реализация <see cref="IVehicleRepository"/> на EF Core для журналов
/// техники и связанных медиафайлов.
/// </summary>
public sealed class VehicleRepository(
    AppDbContext dbContext,
    IMediaStorageService mediaStorage,
    MediaStorageOptions mediaStorageOptions) : IVehicleRepository
{
    /// <summary>Проверяет ежедневные показания либо сохраняет их с контролем предварительного результата.</summary>
    public async Task<ServiceResult<DailyHoursPreview>> ProcessDailyHoursAsync(DailyHoursRequest request,
        bool commit, Guid userId, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        // Coordinate with manual entry and legacy imports as well as other daily imports.
        // The lock is held only for the final validation and atomic write, never during preview UI.
        if (commit) await dbContext.Database.ExecuteSqlRawAsync(
            "LOCK TABLE vehicle_hour_readings IN SHARE ROW EXCLUSIVE MODE", cancellationToken);
        var vehicles = await GetVehiclesAsync(cancellationToken);
        var numbers = request.Rows.Select(r => r.GarageNumber).ToHashSet();
        var ids = vehicles.Where(v => v.GarageNumber.HasValue && numbers.Contains(v.GarageNumber.Value)).Select(v => v.Id).ToArray();
        var history = await dbContext.VehicleHourReadings.AsNoTracking().Where(h => ids.Contains(h.VehicleId))
            .Select(h => new DailyHoursHistory(h.VehicleId, h.ReadingDate, h.EngineNumber, h.EngineHours, h.CreatedAt))
            .ToArrayAsync(cancellationToken);
        var preview = MyApp.Application.Services.DailyHoursPlanner.Build(request.Rows, vehicles, history);
        if (!commit) return ServiceResult<DailyHoursPreview>.Success(preview);
        if (request.Fingerprint != preview.Fingerprint)
            return ServiceResult<DailyHoursPreview>.Conflict("Данные изменились. Повторите предварительную проверку.");
        if (!request.AcceptWarnings && preview.Rows.Any(r => r.Status is "warning" or "error"))
            return ServiceResult<DailyHoursPreview>.Conflict("Подтвердите предупреждения в предварительном просмотре.");
        var ready = preview.Rows.Where(r => r.Status is "ready" or "warning").ToArray();
        foreach (var row in ready) dbContext.VehicleHourReadings.Add(new VehicleHourReadingRecord {
            Id = Guid.NewGuid(), VehicleId = row.VehicleId!.Value, ReadingDate = row.ReadingDate,
            ReadingTime = new TimeOnly(8, 0), EngineNumber = row.EngineNumber, EngineHours = row.EngineHours!.Value,
            Note = $"Импорт на 08:00; {row.Source}; {row.Message}", CreatedBy = userId
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ServiceResult<DailyHoursPreview>.Success(preview with { Imported = ready.Length });
    }

    /// <summary>Возвращает справочники для редактирования парка и сведения о доступных действиях.</summary>
    public async Task<VehicleCatalogOptions> GetVehicleCatalogOptionsAsync(CancellationToken cancellationToken) => new(false,
        await dbContext.VehicleGroups.AsNoTracking().OrderBy(item => item.FullName).Select(item => new VehicleCatalogOption(item.Id, item.FullName)).ToListAsync(cancellationToken),
        await dbContext.VehicleTypes.AsNoTracking().OrderBy(item => item.FullName).Select(item => new VehicleCatalogOption(item.Id, item.FullName, item.CarGroupId)).ToListAsync(cancellationToken),
        await dbContext.VehicleModels.AsNoTracking().OrderBy(item => item.FullName).Select(item => new VehicleCatalogOption(item.Id, item.FullName, item.CarTypeId)).ToListAsync(cancellationToken));

    /// <summary>Создаёт или обновляет карточку техники в каталоге парка.</summary>
    public async Task<ServiceResult<Guid>> SaveVehicleCatalogAsync(Guid? id, VehicleCatalogRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        // Serialize catalogue resolution and VIN checks across concurrent mechanic requests.
        await dbContext.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(740410112)", cancellationToken);
        NumberCarRecord vehicle;
        if (id is Guid existingId) {
            var existing = await dbContext.Vehicles.SingleOrDefaultAsync(item => item.Id == existingId && !item.IsArchived, cancellationToken);
            if (existing is null) return ServiceResult<Guid>.NotFound();
            vehicle = existing;
        } else vehicle = new NumberCarRecord { Id = Guid.NewGuid() };
        if (!string.IsNullOrWhiteSpace(request.Vin) && request.Vin is not ("-" or "—") &&
            await dbContext.Vehicles.AnyAsync(item => !item.IsArchived && item.Id != vehicle.Id &&
                item.Vin != null && item.Vin.Trim().ToUpper() == request.Vin.ToUpperInvariant(), cancellationToken))
            return ServiceResult<Guid>.Conflict("Техника с таким VIN уже есть в парке.");
        var groupKey = request.GroupName.ToLowerInvariant();
        var group = await dbContext.VehicleGroups.FirstOrDefaultAsync(item => item.FullName.Trim().ToLower() == groupKey, cancellationToken);
        if (group is null) { group = new GroupCarRecord { Id = Guid.NewGuid(), FullName = request.GroupName }; dbContext.VehicleGroups.Add(group); }
        var typeKey = request.TypeName.ToLowerInvariant();
        var type = await dbContext.VehicleTypes.FirstOrDefaultAsync(item => item.CarGroupId == group.Id && item.FullName.Trim().ToLower() == typeKey, cancellationToken);
        if (type is null) { type = new TypeCarRecord { Id = Guid.NewGuid(), FullName = request.TypeName, CarGroupId = group.Id }; dbContext.VehicleTypes.Add(type); }
        var modelKey = request.ModelName.ToLowerInvariant();
        var model = await dbContext.VehicleModels.FirstOrDefaultAsync(item => item.CarTypeId == type.Id && item.FullName.Trim().ToLower() == modelKey, cancellationToken);
        if (model is null) { model = new ModelCarRecord { Id = Guid.NewGuid(), FullName = request.ModelName, CarTypeId = type.Id }; dbContext.VehicleModels.Add(model); }
        // Persist parents first: the original schema has FKs but no EF navigation properties.
        await dbContext.SaveChangesAsync(cancellationToken);
        vehicle.CarModeId = model.Id;
        vehicle.GarageNumber = request.GarageNumber;
        vehicle.StateNumber = request.StateNumber;
        vehicle.Vin = request.Vin;
        if (id is null) dbContext.Vehicles.Add(vehicle);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ServiceResult<Guid>.Success(vehicle.Id);
    }

    /// <summary>Переводит технику в архив с сохранением связанных записей истории.</summary>
    public async Task<bool> ArchiveVehicleAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(740410112)", cancellationToken);
        var count = await dbContext.Vehicles.Where(item => item.Id == id && !item.IsArchived)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsArchived, true), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return count > 0;
    }

    /// <summary>Возвращает вспомогательные работы за выбранный период.</summary>
    public async Task<IReadOnlyList<VehicleWorkResponse>> GetAuxiliaryWorksAsync(CancellationToken cancellationToken)
    {
        var works = await GetWorkResponsesAsync(dbContext.VehicleWorks.AsNoTracking()
            .Where(work => work.WorkKind == "auxiliary").OrderByDescending(work => work.CompletedAt ?? work.CreatedAt), cancellationToken);
        return works.Select(item => item.Response).ToArray();
    }

    /// <summary>Проверяет, существует ли указанная вспомогательная работа.</summary>
    public Task<bool> AuxiliaryWorkExistsAsync(Guid workId, CancellationToken cancellationToken) =>
        dbContext.VehicleWorks.AsNoTracking().AnyAsync(work => work.Id == workId && work.WorkKind == "auxiliary", cancellationToken);

    /// <summary>Проверяет принадлежность медиафайла указанной вспомогательной работе.</summary>
    public Task<bool> AuxiliaryMediaExistsAsync(Guid mediaId, bool video, CancellationToken cancellationToken) => video
        ? (from media in dbContext.VehicleWorkVideos join work in dbContext.VehicleWorks on media.WorkId equals work.Id
            where media.Id == mediaId && work.WorkKind == "auxiliary" select media.Id).AnyAsync(cancellationToken)
        : (from media in dbContext.VehicleWorkPhotos join work in dbContext.VehicleWorks on media.WorkId equals work.Id
            where media.Id == mediaId && work.WorkKind == "auxiliary" select media.Id).AnyAsync(cancellationToken);

    /// <summary>Создаёт запись о вспомогательной работе сотрудника.</summary>
    public async Task<Guid> AddAuxiliaryWorkAsync(AuxiliaryWorkRequest request, Guid userId, CancellationToken cancellationToken)
    {
        var work = new VehicleWorkRecord {
            Id = Guid.NewGuid(), VehicleId = null, WorkKind = "auxiliary", Title = request.Title,
            Description = request.Description, Location = request.Location,
            WorkDate = DateOnly.FromDateTime(request.PerformedAt.Date), CompletedAt = request.PerformedAt,
            RepairStatus = "repaired", CreatedBy = userId, PerformedBy = userId
        };
        dbContext.VehicleWorks.Add(work);
        dbContext.VehicleWorkParticipants.Add(new VehicleWorkParticipantRecord {
            Id = Guid.NewGuid(), WorkId = work.Id, UserId = userId, Status = "pending", RequestedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return work.Id;
    }

    /// <summary>Возвращает участников работы и состояние согласования их участия.</summary>
    public async Task<IReadOnlyList<VehicleWorkParticipantResponse>> GetWorkParticipantsAsync(Guid workId, CancellationToken cancellationToken)
    {
        var responses = await GetParticipantResponsesAsync([workId], cancellationToken);
        return responses.GetValueOrDefault(workId) ?? [];
    }

    /// <summary>Создаёт заявку текущего сотрудника на участие в указанной работе.</summary>
    public async Task<bool> RequestWorkParticipationAsync(Guid workId, Guid userId, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var count = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO vehicle_work_participants (id, work_id, user_id, status, requested_at)
            SELECT {id}, {workId}, {userId}, 'pending', {now}
            WHERE EXISTS (SELECT 1 FROM vehicle_works WHERE id = {workId})
            ON CONFLICT (work_id, user_id) DO UPDATE
            SET status = 'pending', requested_at = EXCLUDED.requested_at, reviewed_by = NULL, reviewed_at = NULL
            WHERE vehicle_work_participants.status = 'rejected'
            """, cancellationToken);
        return count > 0;
    }

    /// <summary>Сохраняет решение по заявке на участие в работе.</summary>
    public async Task<bool> ReviewWorkParticipationAsync(Guid workId, Guid participantId, bool approved, Guid reviewerId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        return await dbContext.VehicleWorkParticipants
            .Where(row => row.Id == participantId && row.WorkId == workId && row.Status == "pending")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.Status, approved ? "approved" : "rejected")
                .SetProperty(row => row.ReviewedBy, reviewerId)
                .SetProperty(row => row.ReviewedAt, now), cancellationToken) > 0;
    }

    /// <summary>Преобразует записи участников работы в данные для клиента.</summary>
    private async Task<Dictionary<Guid, IReadOnlyList<VehicleWorkParticipantResponse>>> GetParticipantResponsesAsync(Guid[] workIds, CancellationToken cancellationToken)
    {
        var rows = await dbContext.VehicleWorkParticipants.AsNoTracking()
            .Where(row => workIds.Contains(row.WorkId))
            .OrderBy(row => row.RequestedAt).ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0) return [];
        var userIds = rows.SelectMany(row => row.ReviewedBy is Guid reviewer ? new[] { row.UserId, reviewer } : new[] { row.UserId }).Distinct().ToArray();
        var names = await GetUserNamesAsync(userIds, cancellationToken);
        var positions = await dbContext.Users.AsNoTracking().Where(user => userIds.Contains(user.Id))
            .Select(user => new { user.Id, Position = user.Profession.Name })
            .ToDictionaryAsync(user => user.Id, user => user.Position, cancellationToken);
        return rows.GroupBy(row => row.WorkId).ToDictionary(group => group.Key,
            group => (IReadOnlyList<VehicleWorkParticipantResponse>)group.Select(row => new VehicleWorkParticipantResponse(
                row.Id, row.UserId, names.GetValueOrDefault(row.UserId) ?? "ФИО не указано",
                positions.GetValueOrDefault(row.UserId) ?? "", row.Status, row.RequestedAt,
                row.ReviewedBy, row.ReviewedAt, row.ReviewedBy is Guid reviewer ? names.GetValueOrDefault(reviewer) ?? "" : "")).ToArray());
    }

    /// <summary>Возвращает список техники.</summary>
    public async Task<IReadOnlyList<VehicleResponse>> GetVehiclesAsync(
        CancellationToken cancellationToken, bool includeArchived = false)
    {
        var vehicles = await (
            from vehicle in dbContext.Vehicles.AsNoTracking().Where(vehicle => includeArchived || !vehicle.IsArchived)
            join modelValue in dbContext.VehicleModels.AsNoTracking()
                on vehicle.CarModeId equals modelValue.Id into models
            from model in models.DefaultIfEmpty()
            join typeValue in dbContext.VehicleTypes.AsNoTracking()
                on (model == null ? null : model.CarTypeId)
                equals (Guid?)typeValue.Id into types
            from type in types.DefaultIfEmpty()
            join groupValue in dbContext.VehicleGroups.AsNoTracking()
                on (type == null ? null : type.CarGroupId)
                equals (Guid?)groupValue.Id into groups
            from vehicleGroup in groups.DefaultIfEmpty()
            orderby vehicleGroup == null ? string.Empty : vehicleGroup.FullName,
                type == null ? string.Empty : type.FullName,
                model == null ? string.Empty : model.FullName,
                vehicle.GarageNumber == null,
                vehicle.GarageNumber
            select new VehicleResponse(
                vehicle.Id,
                vehicleGroup == null ? string.Empty : vehicleGroup.FullName,
                type == null ? string.Empty : type.FullName,
                model == null ? string.Empty : model.FullName,
                vehicle.GarageNumber,
                vehicle.StateNumber ?? string.Empty,
                vehicle.Vin ?? string.Empty, "healthy", vehicle.IsArchived,
                type == null ? null : type.IconPath,
                vehicleGroup == null ? 0 : vehicleGroup.ReportSortOrder))
            .ToListAsync(cancellationToken);

        var states = await GetOperationalStatesAsync(vehicles.Select(vehicle => vehicle.Id).ToArray(), cancellationToken);
        return vehicles.Select(vehicle => vehicle with { OperationalState = states.GetValueOrDefault(vehicle.Id) ?? "healthy" }).ToArray();
    }

    /// <summary>Возвращает журнал событий выбранной техники.</summary>
    public async Task<VehicleJournalResponse?> GetJournalAsync(
        Guid vehicleId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        var vehicle = await GetVehicleAsync(vehicleId, cancellationToken);
        if (vehicle is null)
        {
            return null;
        }

        var states = await GetOperationalStatesAsync([vehicleId], cancellationToken);
        vehicle = vehicle with { OperationalState = states.GetValueOrDefault(vehicleId) ?? "healthy" };
        var purchases = await GetPurchasesAsync(
            vehicleId, from, to, cancellationToken);
        var defects = await GetDefectsAsync(
            vehicleId, from, to, cancellationToken);
        var hours = await GetHoursAsync(
            vehicleId, from, to, cancellationToken);
        var works = await GetWorksAsync(
            vehicleId, from, to, cancellationToken);
        return new VehicleJournalResponse(
            vehicle, purchases, defects, hours, works);
    }

    /// <summary>Возвращает журналы ремонта с учётом заданных фильтров.</summary>
    public async Task<IReadOnlyList<VehicleJournalResponse>> GetRepairJournalsAsync(
        CancellationToken cancellationToken, bool includeArchived = false)
    {
        var vehicles = await GetVehiclesAsync(cancellationToken, includeArchived);
        if (vehicles.Count == 0)
        {
            return [];
        }

        var vehicleIds = vehicles.Select(vehicle => vehicle.Id).ToArray();
        var defects = await GetDefectResponsesAsync(
            dbContext.VehicleDefects.AsNoTracking()
                .Where(defect => vehicleIds.Contains(defect.VehicleId))
                .OrderByDescending(defect => defect.CreatedAt),
            cancellationToken);
        var works = await GetWorkResponsesAsync(
            dbContext.VehicleWorks.AsNoTracking()
                .Where(work => work.VehicleId.HasValue && vehicleIds.Contains(work.VehicleId.Value))
                .OrderByDescending(work => work.CreatedAt),
            cancellationToken);
        var defectsByVehicle = defects
            .GroupBy(defect => defect.VehicleId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<VehicleDefectResponse>)group
                .Select(item => item.Response)
                .ToArray());
        var worksByVehicle = works
            .GroupBy(work => work.VehicleId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<VehicleWorkResponse>)group
                .Select(item => item.Response)
                .ToArray());

        var latestHours = await dbContext.VehicleHourReadings.AsNoTracking()
            .Where(reading => vehicleIds.Contains(reading.VehicleId))
            .GroupBy(reading => new { reading.VehicleId, reading.EngineNumber })
            .Select(group => group.OrderByDescending(reading => reading.ReadingDate)
                .ThenByDescending(reading => reading.CreatedAt).ThenByDescending(reading => reading.Id)
                .First())
            .ToListAsync(cancellationToken);
        var hoursByVehicle = latestHours.GroupBy(reading => reading.VehicleId).ToDictionary(group => group.Key,
            group => (IReadOnlyList<VehicleHoursResponse>)group.Select(reading => new VehicleHoursResponse(
                reading.Id, reading.ReadingDate, reading.EngineHours, reading.Note, reading.CreatedAt, reading.EngineNumber, reading.ReadingTime)).ToArray());

        var partsRequests = await (
            from request in dbContext.VehiclePartsRequests.AsNoTracking()
            join defect in dbContext.VehicleDefects.AsNoTracking() on request.DefectId equals defect.Id
            where vehicleIds.Contains(defect.VehicleId) && request.CompletedDate == null
            orderby request.RequestDate, request.CreatedAt
            select new {
                defect.VehicleId,
                Response = new VehiclePartsRequestResponse(request.Id, request.DefectId,
                    request.RequestNumber, request.RequestDate, request.Description,
                    request.RequiredParts, request.CreatedAt, request.CompletedDate)
            }).ToListAsync(cancellationToken);
        var partsByVehicle = partsRequests.GroupBy(item => item.VehicleId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<VehiclePartsRequestResponse>)group.Select(item => item.Response).ToArray());

        return vehicles.Select(vehicle => new VehicleJournalResponse(
            vehicle,
            [],
            defectsByVehicle.GetValueOrDefault(vehicle.Id) ?? [],
            hoursByVehicle.GetValueOrDefault(vehicle.Id) ?? [],
            worksByVehicle.GetValueOrDefault(vehicle.Id) ?? [],
            partsByVehicle.GetValueOrDefault(vehicle.Id) ?? [])).ToArray();
    }

    /// <summary>Определяет состояние техники по незавершённым неисправностям и этапам ремонта.</summary>
    private async Task<Dictionary<Guid, string>> GetOperationalStatesAsync(Guid[] vehicleIds, CancellationToken cancellationToken)
    {
        var defects = await dbContext.VehicleDefects.AsNoTracking()
            .Where(defect => vehicleIds.Contains(defect.VehicleId))
            .Select(defect => new {
                defect.VehicleId,
                InitialState = defect.OperationalState,
                Status = dbContext.VehicleWorks.Where(work => work.DefectId == defect.Id && work.CompletedAt != null)
                    .OrderByDescending(work => work.CompletedAt).ThenByDescending(work => work.CreatedAt)
                    .Select(work => work.RepairStatus).FirstOrDefault(),
                State = dbContext.VehicleWorks.Where(work => work.DefectId == defect.Id && work.CompletedAt != null)
                    .OrderByDescending(work => work.CompletedAt).ThenByDescending(work => work.CreatedAt)
                    .Select(work => work.OperationalState).FirstOrDefault()
            }).ToListAsync(cancellationToken);
        return defects.GroupBy(defect => defect.VehicleId).ToDictionary(group => group.Key,
            group => MyApp.Application.Models.VehicleOperationalState.Resolve(
                group.Select(defect => (defect.Status, defect.State ?? defect.InitialState))));
    }

    /// <summary>Проверяет существование техники с указанным идентификатором.</summary>
    public Task<bool> VehicleExistsAsync(
        Guid vehicleId,
        CancellationToken cancellationToken) =>
        dbContext.Vehicles.AsNoTracking()
            .AnyAsync(vehicle => vehicle.Id == vehicleId && !vehicle.IsArchived, cancellationToken);

    /// <summary>Добавляет запись о закупке для техники.</summary>
    public async Task<Guid> AddPurchaseAsync(
        Guid vehicleId,
        VehiclePurchaseRequest request,
        Guid createdBy,
        CancellationToken cancellationToken)
    {
        var entity = new VehiclePurchaseRequestRecord
        {
            Id = Guid.NewGuid(),
            VehicleId = vehicleId,
            RequestDate = request.RequestDate,
            RequestNumber = request.RequestNumber,
            ItemName = request.ItemName,
            Quantity = request.Quantity,
            Status = request.Status,
            Note = request.Note,
            CreatedBy = createdBy
        };
        dbContext.VehiclePurchaseRequests.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    /// <summary>Регистрирует неисправность техники.</summary>
    public async Task<Guid> AddDefectAsync(
        Guid vehicleId,
        VehicleDefectRequest request,
        Guid createdBy,
        CancellationToken cancellationToken)
    {
        var entity = new VehicleDefectRecord
        {
            Id = Guid.NewGuid(),
            VehicleId = vehicleId,
            NodeName = request.NodeName,
            FailureReason = request.FailureReason,
            ErrorCode = request.ErrorCode ?? string.Empty,
            Symptoms = request.Symptoms,
            DowntimeStartedAt = request.OperationalState == "operating" ? null : request.DowntimeStartedAt,
            OperationalState = request.OperationalState,
            CreatedBy = createdBy
        };
        dbContext.VehicleDefects.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    /// <summary>Проверяет существование неисправности.</summary>
    public Task<bool> DefectExistsAsync(
        Guid defectId,
        CancellationToken cancellationToken) =>
        dbContext.VehicleDefects.AsNoTracking()
            .AnyAsync(defect => defect.Id == defectId, cancellationToken);

    /// <summary>Проверяет принадлежность неисправности указанной технике.</summary>
    public Task<bool> DefectBelongsToVehicleAsync(
        Guid defectId,
        Guid vehicleId,
        CancellationToken cancellationToken) =>
        dbContext.VehicleDefects.AsNoTracking()
            .AnyAsync(
                defect => defect.Id == defectId && defect.VehicleId == vehicleId,
                cancellationToken);

    /// <summary>Закрепляет неисправность за исполнителем.</summary>
    public async Task<bool> ClaimDefectAsync(
        Guid defectId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var updated = await dbContext.VehicleDefects
            .Where(defect =>
                defect.Id == defectId &&
                defect.AssignedTo == null &&
                !dbContext.VehicleWorks.Any(work =>
                    work.DefectId == defectId && work.CompletedAt != null))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(defect => defect.AssignedTo, userId)
                    .SetProperty(defect => defect.RepairStartedAt, now),
                cancellationToken);
        return updated == 1;
    }

    /// <summary>Проверяет, создал ли пользователь указанную неисправность.</summary>
    public Task<bool> IsDefectCreatorAsync(
        Guid defectId,
        Guid userId,
        CancellationToken cancellationToken) =>
        dbContext.VehicleDefects.AsNoTracking()
            .AnyAsync(
                defect => defect.Id == defectId && defect.CreatedBy == userId,
                cancellationToken);

    /// <summary>Возвращает количество фотографий неисправности.</summary>
    public Task<int> GetDefectPhotoCountAsync(
        Guid defectId,
        CancellationToken cancellationToken) =>
        dbContext.VehicleDefectPhotos.AsNoTracking()
            .CountAsync(photo => photo.DefectId == defectId, cancellationToken);

    /// <summary>Сохраняет фотографии неисправности.</summary>
    public Task<IReadOnlyList<Guid>> AddDefectPhotosAsync(
        Guid defectId,
        IReadOnlyList<VehicleWorkPhotoUpload> photos,
        CancellationToken cancellationToken) =>
        AddPhotosAsync(
            photos,
            mediaStorage.SavePhotoAsync,
            (id, item, path) => new VehicleDefectPhotoRecord
            {
                Id = id,
                DefectId = defectId,
                FileName = SafeFileName(item.FileName),
                ContentType = item.ContentType,
                StoragePath = path,
                Size = item.Content.LongLength
            },
            entity => dbContext.VehicleDefectPhotos.Add(entity),
            cancellationToken);

    /// <summary>Возвращает содержимое фотографии неисправности.</summary>
    public Task<VehicleWorkPhotoContent?> GetDefectPhotoAsync(
        Guid photoId,
        CancellationToken cancellationToken) =>
        GetMediaAsync(
            dbContext.VehicleDefectPhotos.AsNoTracking(),
            photoId,
            entity => entity.FileName,
            entity => entity.ContentType,
            entity => entity.Content,
            entity => entity.StoragePath,
            cancellationToken);

    /// <summary>Удаляет фотографию неисправности.</summary>
    public Task<bool> DeleteDefectPhotoAsync(
        Guid photoId,
        CancellationToken cancellationToken) =>
        DeleteMediaAsync(
            dbContext.VehicleDefectPhotos,
            photoId,
            entity => entity.StoragePath,
            cancellationToken);

    /// <summary>Возвращает количество видеозаписей неисправности.</summary>
    public Task<int> GetDefectVideoCountAsync(
        Guid defectId,
        CancellationToken cancellationToken) =>
        dbContext.VehicleDefectVideos.AsNoTracking()
            .CountAsync(video => video.DefectId == defectId, cancellationToken);

    /// <summary>Сохраняет видеозаписи неисправности.</summary>
    public Task<IReadOnlyList<Guid>> AddDefectVideosAsync(
        Guid defectId,
        IReadOnlyList<VehicleMediaUpload> videos,
        CancellationToken cancellationToken) =>
        AddMediaAsync(
            videos,
            mediaStorage.SaveVideoAsync,
            (id, item, path) => new VehicleDefectVideoRecord
            {
                Id = id,
                DefectId = defectId,
                FileName = SafeFileName(item.FileName),
                ContentType = item.ContentType,
                StoragePath = path,
                Size = item.Content.LongLength
            },
            entity => dbContext.VehicleDefectVideos.Add(entity),
            cancellationToken);

    /// <summary>Открывает поток чтения видео неисправности.</summary>
    public Task<VehicleMediaStream?> GetDefectVideoStreamAsync(
        Guid videoId,
        CancellationToken cancellationToken) =>
        GetMediaStreamAsync(
            dbContext.VehicleDefectVideos.AsNoTracking(),
            videoId,
            entity => entity.FileName,
            entity => entity.ContentType,
            entity => entity.Content,
            entity => entity.StoragePath,
            cancellationToken);

    /// <summary>Удаляет видеозапись неисправности.</summary>
    public Task<bool> DeleteDefectVideoAsync(
        Guid videoId,
        CancellationToken cancellationToken) =>
        DeleteMediaAsync(
            dbContext.VehicleDefectVideos,
            videoId,
            entity => entity.StoragePath,
            cancellationToken);

    /// <summary>Добавляет показание моточасов техники.</summary>
    public async Task<Guid> AddHoursAsync(
        Guid vehicleId,
        VehicleHoursRequest request,
        Guid createdBy,
        CancellationToken cancellationToken)
    {
        var entity = new VehicleHourReadingRecord
        {
            Id = Guid.NewGuid(),
            VehicleId = vehicleId,
            ReadingDate = request.ReadingDate,
            EngineHours = request.EngineHours,
            EngineNumber = request.EngineNumber,
            Note = request.Note,
            CreatedBy = createdBy
        };
        dbContext.VehicleHourReadings.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    /// <summary>
    /// CSV import is serialized by reading date to avoid duplicate inserts
    /// from simultaneous imports using this repository. Direct AddHoursAsync
    /// calls and external SQL do not participate in the advisory lock.
    /// </summary>
    public async Task ImportHoursAsync(
        DateOnly readingDate,
        IReadOnlyList<VehicleHoursImportItem> items,
        Guid createdBy,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken);

        // Keep MAX/UPSERT operations in this transaction and serialize all
        // imports for one date. The lock is released automatically on commit
        // or rollback, even if a row violates a database constraint.
        const int lockNamespace = 4386203;
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockNamespace}, {readingDate.DayNumber})",
            cancellationToken);

        var blankVehicles=items.Where(x=>!x.EngineHours.HasValue).Select(x=>x.VehicleId).Distinct().ToArray();
        var carried=new Dictionary<(Guid VehicleId,int EngineNumber),decimal>();
        if(blankVehicles.Length>0) {
            var readings=await dbContext.VehicleHourReadings.AsNoTracking().Where(x=>blankVehicles.Contains(x.VehicleId) && x.ReadingDate<=readingDate)
                .GroupBy(x=>new{x.VehicleId,x.EngineNumber})
                .Select(group=>group.OrderByDescending(x=>x.ReadingDate).ThenByDescending(x=>x.CreatedAt).First()).ToArrayAsync(cancellationToken);
            foreach(var reading in readings)carried[(reading.VehicleId,reading.EngineNumber)]=reading.EngineHours;
        }
        foreach (var item in items) {
            var key=(item.VehicleId,item.EngineNumber);
            var value=item.EngineHours;
            if (!value.HasValue) {
                if(carried.TryGetValue(key,out var previous))value=previous;
                else if(item.EngineNumber==0)value=0;
                else continue;
            }
            var updated=await dbContext.VehicleHourReadings.Where(x=>x.VehicleId==item.VehicleId && x.EngineNumber==item.EngineNumber && x.ReadingDate==readingDate)
                .ExecuteUpdateAsync(setters=>setters.SetProperty(x=>x.EngineHours,value.Value).SetProperty(x=>x.Note,"Импорт файла").SetProperty(x=>x.CreatedBy,createdBy).SetProperty(x=>x.CreatedAt,DateTimeOffset.UtcNow),cancellationToken);
            if(updated==0) {
                dbContext.VehicleHourReadings.Add(new VehicleHourReadingRecord{Id=Guid.NewGuid(),VehicleId=item.VehicleId,EngineNumber=item.EngineNumber,ReadingDate=readingDate,EngineHours=value.Value,Note="Импорт файла",CreatedBy=createdBy});
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            carried[key]=value.Value;
        }
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Добавляет запись о выполненной работе по технике.</summary>
    public async Task<Guid> AddWorkAsync(
        Guid vehicleId,
        VehicleWorkRequest request,
        Guid createdBy,
        CancellationToken cancellationToken)
    {
        var entity = new VehicleWorkRecord
        {
            Id = Guid.NewGuid(),
            VehicleId = vehicleId,
            WorkDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Description = request.Description,
            Performer = string.Empty,
            Note = string.Empty,
            DefectId = request.DefectId,
            FailureCause = string.Empty,
            RepairStatus = "repaired",
            RequiredParts = string.Empty,
            CreatedBy = createdBy
        };
        dbContext.VehicleWorks.Add(entity);
        dbContext.VehicleWorkParticipants.Add(new VehicleWorkParticipantRecord {
            Id = Guid.NewGuid(), WorkId = entity.Id, UserId = entity.PerformedBy ?? entity.CreatedBy,
            Status = "pending", RequestedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    /// <summary>Фиксирует завершение устранения неисправности.</summary>
    public async Task<Guid?> CompleteDefectAsync(
        Guid defectId,
        VehicleWorkRequest request,
        Guid performedBy,
        bool administrator,
        CancellationToken cancellationToken)
    {
        // Mobile sync owns the surrounding transaction so the receipt and work commit together.
        await using var transaction = dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
        var defect = await dbContext.VehicleDefects
            .AsNoTracking()
            .SingleOrDefaultAsync(
                entity => entity.Id == defectId, cancellationToken);
        if (defect is null)
        {
            return null;
        }

        var completedAt = request.RepairDateTime ?? DateTimeOffset.UtcNow;
        var entity = new VehicleWorkRecord
        {
            Id = Guid.NewGuid(),
            VehicleId = defect.VehicleId,
            WorkDate = DateOnly.FromDateTime(completedAt.Date),
            Description = request.Description,
            Location = request.Location?.Trim() ?? string.Empty,
            Performer = string.Empty,
            Note = string.Empty,
            DefectId = defectId,
            FailureCause = request.Cause,
            RepairStatus = request.Status!,
            RequiredParts = request.RequiredParts ?? string.Empty,
            PerformedBy = performedBy,
            CompletedAt = completedAt,
            OperationalState = request.OperationalState,
            DowntimeStartedAt = request.OperationalState == "operating" ? null : request.DowntimeStartedAt,
            CreatedBy = performedBy
        };
        dbContext.VehicleWorks.Add(entity);
        dbContext.VehicleWorkParticipants.Add(new VehicleWorkParticipantRecord {
            Id = Guid.NewGuid(), WorkId = entity.Id, UserId = entity.PerformedBy ?? entity.CreatedBy,
            Status = "pending", RequestedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return entity.Id;
    }

    /// <summary>Проверяет существование записи о работе.</summary>
    public Task<bool> WorkExistsAsync(
        Guid workId,
        CancellationToken cancellationToken) =>
        dbContext.VehicleWorks.AsNoTracking()
            .AnyAsync(work => work.Id == workId, cancellationToken);

    /// <summary>Проверяет, является ли пользователь исполнителем работы.</summary>
    public Task<bool> IsWorkPerformerAsync(
        Guid workId,
        Guid userId,
        CancellationToken cancellationToken) =>
        dbContext.VehicleWorks.AsNoTracking()
            .AnyAsync(
                work => work.Id == workId &&
                    (work.PerformedBy == userId || work.CreatedBy == userId),
                cancellationToken);

    /// <summary>Проверяет право пользователя изменять медиафайлы работы.</summary>
    public Task<bool> CanManageMediaAsync(
        string category,
        Guid mediaId,
        Guid userId,
        CancellationToken cancellationToken) =>
        category switch
        {
            "defect-photo" => dbContext.VehicleDefectPhotos.AsNoTracking()
                .AnyAsync(photo => photo.Id == mediaId &&
                    dbContext.VehicleDefects.Any(defect =>
                        defect.Id == photo.DefectId &&
                        defect.CreatedBy == userId), cancellationToken),
            "defect-video" => dbContext.VehicleDefectVideos.AsNoTracking()
                .AnyAsync(video => video.Id == mediaId &&
                    dbContext.VehicleDefects.Any(defect =>
                        defect.Id == video.DefectId &&
                        defect.CreatedBy == userId), cancellationToken),
            "work-photo" => dbContext.VehicleWorkPhotos.AsNoTracking()
                .AnyAsync(photo => photo.Id == mediaId &&
                    dbContext.VehicleWorks.Any(work =>
                        work.Id == photo.WorkId &&
                        work.PerformedBy == userId), cancellationToken),
            "work-video" => dbContext.VehicleWorkVideos.AsNoTracking()
                .AnyAsync(video => video.Id == mediaId &&
                    dbContext.VehicleWorks.Any(work =>
                        work.Id == video.WorkId &&
                        work.PerformedBy == userId), cancellationToken),
            _ => Task.FromResult(false)
        };

    /// <summary>Возвращает количество фотографий выполненной работы.</summary>
    public Task<int> GetWorkPhotoCountAsync(
        Guid workId,
        CancellationToken cancellationToken) =>
        dbContext.VehicleWorkPhotos.AsNoTracking()
            .CountAsync(photo => photo.WorkId == workId, cancellationToken);

    /// <summary>Сохраняет фотографии выполненной работы.</summary>
    public Task<IReadOnlyList<Guid>> AddWorkPhotosAsync(
        Guid workId,
        IReadOnlyList<VehicleWorkPhotoUpload> photos,
        CancellationToken cancellationToken) =>
        AddPhotosAsync(
            photos,
            mediaStorage.SavePhotoAsync,
            (id, item, path) => new VehicleWorkPhotoRecord
            {
                Id = id,
                WorkId = workId,
                FileName = SafeFileName(item.FileName),
                ContentType = item.ContentType,
                StoragePath = path,
                Size = item.Content.LongLength
            },
            entity => dbContext.VehicleWorkPhotos.Add(entity),
            cancellationToken);

    /// <summary>Возвращает содержимое фотографии выполненной работы.</summary>
    public Task<VehicleWorkPhotoContent?> GetWorkPhotoAsync(
        Guid photoId,
        CancellationToken cancellationToken) =>
        GetMediaAsync(
            dbContext.VehicleWorkPhotos.AsNoTracking(),
            photoId,
            entity => entity.FileName,
            entity => entity.ContentType,
            entity => entity.Content,
            entity => entity.StoragePath,
            cancellationToken);

    /// <summary>Удаляет фотографию выполненной работы.</summary>
    public Task<bool> DeleteWorkPhotoAsync(
        Guid photoId,
        CancellationToken cancellationToken) =>
        DeleteMediaAsync(
            dbContext.VehicleWorkPhotos,
            photoId,
            entity => entity.StoragePath,
            cancellationToken);

    /// <summary>Возвращает количество видеозаписей выполненной работы.</summary>
    public Task<int> GetWorkVideoCountAsync(
        Guid workId,
        CancellationToken cancellationToken) =>
        dbContext.VehicleWorkVideos.AsNoTracking()
            .CountAsync(video => video.WorkId == workId, cancellationToken);

    /// <summary>Сохраняет видеозаписи выполненной работы.</summary>
    public Task<IReadOnlyList<Guid>> AddWorkVideosAsync(
        Guid workId,
        IReadOnlyList<VehicleMediaUpload> videos,
        CancellationToken cancellationToken) =>
        AddMediaAsync(
            videos,
            mediaStorage.SaveVideoAsync,
            (id, item, path) => new VehicleWorkVideoRecord
            {
                Id = id,
                WorkId = workId,
                FileName = SafeFileName(item.FileName),
                ContentType = item.ContentType,
                StoragePath = path,
                Size = item.Content.LongLength
            },
            entity => dbContext.VehicleWorkVideos.Add(entity),
            cancellationToken);

    /// <summary>Открывает поток чтения видео выполненной работы.</summary>
    public Task<VehicleMediaStream?> GetWorkVideoStreamAsync(
        Guid videoId,
        CancellationToken cancellationToken) =>
        GetMediaStreamAsync(
            dbContext.VehicleWorkVideos.AsNoTracking(),
            videoId,
            entity => entity.FileName,
            entity => entity.ContentType,
            entity => entity.Content,
            entity => entity.StoragePath,
            cancellationToken);

    /// <summary>Удаляет видеозапись выполненной работы.</summary>
    public Task<bool> DeleteWorkVideoAsync(
        Guid videoId,
        CancellationToken cancellationToken) =>
        DeleteMediaAsync(
            dbContext.VehicleWorkVideos,
            videoId,
            entity => entity.StoragePath,
            cancellationToken);

    /// <summary>Добавляет заявку на закупку запчастей для неисправности.</summary>
    public async Task<Guid?> AddPartsRequestAsync(
        Guid defectId,
        VehiclePartsRequest request,
        Guid createdBy,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var canAdd = await dbContext.VehicleDefects.AsNoTracking()
            .AnyAsync(defect =>
                defect.Id == defectId &&
                dbContext.VehicleWorks.Any(work =>
                    work.DefectId == defect.Id &&
                    work.RepairStatus == "awaiting_parts"),
                cancellationToken);
        if (!canAdd)
        {
            return null;
        }

        var entity = new VehiclePartsRequestRecord
        {
            Id = Guid.NewGuid(),
            DefectId = defectId,
            RequestDate = request.RequestDate,
            RequestNumber = request.RequestNumber,
            Description = request.Description,
            RequiredParts = request.RequiredParts ?? string.Empty,
            CompletedDate = request.CompletedDate,
            CreatedBy = createdBy
        };
        dbContext.VehiclePartsRequests.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return entity.Id;
    }

    /// <summary>Обновляет реквизиты и состояние заявки на закупку запчастей.</summary>
    public async Task<bool> UpdatePartsRequestAsync(
        Guid requestId,
        VehiclePartsRequest request,
        CancellationToken cancellationToken)
    {
        var updatedRows = await dbContext.VehiclePartsRequests
            .Where(partsRequest => partsRequest.Id == requestId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        partsRequest => partsRequest.RequestDate,
                        request.RequestDate)
                    .SetProperty(
                        partsRequest => partsRequest.RequestNumber,
                        request.RequestNumber)
                    .SetProperty(
                        partsRequest => partsRequest.Description,
                        request.Description)
                    .SetProperty(
                        partsRequest => partsRequest.RequiredParts,
                        request.RequiredParts ?? string.Empty)
                    .SetProperty(partsRequest => partsRequest.CompletedDate, request.CompletedDate),
                cancellationToken);
        return updatedRows > 0;
    }

    /// <summary>Возвращает заявки на закупку запчастей для выбранной неисправности.</summary>
    public async Task<IReadOnlyList<VehiclePartsRequestResponse>> GetPartsRequestsAsync(
        Guid defectId,
        CancellationToken cancellationToken)
    {
        return await dbContext.VehiclePartsRequests.AsNoTracking()
            .Where(request => request.DefectId == defectId)
            .OrderBy(request => request.CreatedAt)
            .Select(request => new VehiclePartsRequestResponse(
                request.Id,
                request.DefectId,
                request.RequestNumber,
                request.RequestDate,
                request.Description,
                request.RequiredParts,
                request.CreatedAt,
                request.CompletedDate))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Удаляет заявку на закупку запчастей.</summary>
    public async Task<bool> DeletePartsRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken) =>
        await dbContext.VehiclePartsRequests
            .Where(request => request.Id == requestId)
            .ExecuteDeleteAsync(cancellationToken) > 0;

    /// <summary>Проверяет, создал ли пользователь указанную работу.</summary>
    public Task<bool> IsWorkCreatorAsync(Guid workId, Guid userId, CancellationToken cancellationToken) =>
        dbContext.VehicleWorks.AnyAsync(work => work.Id == workId && work.CreatedBy == userId, cancellationToken);

    /// <summary>Обновляет редактируемые данные записи истории техники.</summary>
    public async Task<bool> UpdateHistoryEntryAsync(string category, Guid id, string description, CancellationToken cancellationToken)
    {
        var count = category switch
        {
            "defects" => await dbContext.VehicleDefects.Where(entry => entry.Id == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(entry => entry.Symptoms, description), cancellationToken),
            "works" => await dbContext.VehicleWorks.Where(entry => entry.Id == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(entry => entry.Description, description), cancellationToken),
            _ => 0
        };
        return count > 0;
    }

    /// <summary>Удаляет указанную запись истории техники.</summary>
    public async Task<bool> DeleteEntryAsync(
        string category,
        Guid id,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> paths = category switch
        {
            "defects" => await GetDefectMediaPathsAsync(id, cancellationToken),
            "works" => await GetWorkMediaPathsAsync(id, cancellationToken),
            _ => []
        };

        var deleted = category switch
        {
            "purchases" => await dbContext.VehiclePurchaseRequests
                .Where(entry => entry.Id == id)
                .ExecuteDeleteAsync(cancellationToken),
            "defects" => await dbContext.VehicleDefects
                .Where(entry => entry.Id == id)
                .ExecuteDeleteAsync(cancellationToken),
            "hours" => await dbContext.VehicleHourReadings
                .Where(entry => entry.Id == id)
                .ExecuteDeleteAsync(cancellationToken),
            "works" => await dbContext.VehicleWorks
                .Where(entry => entry.Id == id)
                .ExecuteDeleteAsync(cancellationToken),
            _ => 0
        };
        if (deleted == 0)
        {
            return false;
        }

        foreach (var path in paths)
        {
            DeleteFile(path);
        }
        return true;
    }

    /// <summary>Читает карточку техники по идентификатору.</summary>
    private async Task<VehicleResponse?> GetVehicleAsync(
        Guid vehicleId,
        CancellationToken cancellationToken) =>
        await (
            from vehicle in dbContext.Vehicles.AsNoTracking()
            join modelValue in dbContext.VehicleModels.AsNoTracking()
                on vehicle.CarModeId equals modelValue.Id into models
            from model in models.DefaultIfEmpty()
            join typeValue in dbContext.VehicleTypes.AsNoTracking()
                on (model == null ? null : model.CarTypeId)
                equals (Guid?)typeValue.Id into types
            from type in types.DefaultIfEmpty()
            join groupValue in dbContext.VehicleGroups.AsNoTracking()
                on (type == null ? null : type.CarGroupId)
                equals (Guid?)groupValue.Id into groups
            from vehicleGroup in groups.DefaultIfEmpty()
            where vehicle.Id == vehicleId
            select new VehicleResponse(
                vehicle.Id,
                vehicleGroup == null ? string.Empty : vehicleGroup.FullName,
                type == null ? string.Empty : type.FullName,
                model == null ? string.Empty : model.FullName,
                vehicle.GarageNumber,
                vehicle.StateNumber ?? string.Empty,
                vehicle.Vin ?? string.Empty, "healthy", vehicle.IsArchived,
                type == null ? null : type.IconPath,
                vehicleGroup == null ? 0 : vehicleGroup.ReportSortOrder))
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>Читает записи о закупках для техники.</summary>
    private async Task<IReadOnlyList<VehiclePurchaseResponse>> GetPurchasesAsync(
        Guid vehicleId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        var query = dbContext.VehiclePurchaseRequests.AsNoTracking()
            .Where(request => request.VehicleId == vehicleId);
        if (from.HasValue)
        {
            query = query.Where(request => request.RequestDate >= from.Value);
        }
        if (to.HasValue)
        {
            query = query.Where(request => request.RequestDate <= to.Value);
        }

        return await query
            .OrderByDescending(request => request.RequestDate)
            .ThenByDescending(request => request.CreatedAt)
            .Select(request => new VehiclePurchaseResponse(
                request.Id,
                request.RequestDate,
                request.RequestNumber,
                request.ItemName,
                request.Quantity,
                request.Status,
                request.Note,
                request.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Подготавливает данные неисправностей с вложениями для ответа клиенту.</summary>
    private async Task<IReadOnlyList<(Guid VehicleId, VehicleDefectResponse Response)>>
        GetDefectResponsesAsync(
            IQueryable<VehicleDefectRecord> query,
            CancellationToken cancellationToken)
    {
        var defects = await query.ToListAsync(cancellationToken);
        if (defects.Count == 0)
        {
            return [];
        }

        var userIds = defects.Select(defect => defect.CreatedBy)
            .Concat(defects.Where(defect => defect.AssignedTo.HasValue)
                .Select(defect => defect.AssignedTo!.Value))
            .Distinct()
            .ToArray();
        var userNames = await GetUserNamesAsync(userIds, cancellationToken);
        var defectIds = defects.Select(defect => defect.Id).ToArray();
        var latestCompletedWorks = await dbContext.VehicleWorks.AsNoTracking()
            .Where(work =>
                work.DefectId.HasValue &&
                defectIds.Contains(work.DefectId.Value) &&
                work.CompletedAt.HasValue)
            .OrderByDescending(work => work.CompletedAt)
            .ThenByDescending(work => work.CreatedAt)
            .Select(work => new
            {
                DefectId = work.DefectId!.Value,
                work.RepairStatus,
                work.OperationalState,
                work.DowntimeStartedAt,
                work.CompletedAt
            })
            .ToListAsync(cancellationToken);
        var latestByDefect = latestCompletedWorks
            .GroupBy(work => work.DefectId)
            .ToDictionary(group => group.Key, group => group.First());
        var photos = await dbContext.VehicleDefectPhotos.AsNoTracking()
            .Where(photo => defectIds.Contains(photo.DefectId))
            .OrderBy(photo => photo.CreatedAt)
            .ThenBy(photo => photo.Id)
            .Select(photo => new
            {
                photo.DefectId,
                photo.Id,
                photo.FileName,
                photo.ContentType,
                photo.Size,
                ContentLength = photo.Content == null
                    ? 0L
                    : (long)photo.Content.Length
            })
            .ToListAsync(cancellationToken);
        var videos = await dbContext.VehicleDefectVideos.AsNoTracking()
            .Where(video => defectIds.Contains(video.DefectId))
            .OrderBy(video => video.CreatedAt)
            .ThenBy(video => video.Id)
            .Select(video => new
            {
                video.DefectId,
                Response = new VehicleMediaResponse(
                    video.Id, video.FileName, video.ContentType, video.Size)
            })
            .ToListAsync(cancellationToken);
        var photosByDefect = photos.GroupBy(photo => photo.DefectId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<VehicleWorkPhotoResponse>)group
                    .Select(item => new VehicleWorkPhotoResponse(
                        item.Id,
                        item.FileName,
                        item.ContentType,
                        item.Size == 0 ? item.ContentLength : item.Size))
                    .ToArray());
        var videosByDefect = videos.GroupBy(video => video.DefectId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<VehicleMediaResponse>)group
                    .Select(item => item.Response)
                    .ToArray());

        return defects.Select(defect =>
        {
            var createdByName = userNames.TryGetValue(defect.CreatedBy, out var creator)
                ? creator
                : string.Empty;
            var assignedToName = defect.AssignedTo is Guid assignedId &&
                userNames.TryGetValue(assignedId, out var assignee)
                    ? assignee
                    : string.Empty;
            var status = latestByDefect.TryGetValue(defect.Id, out var completed)
                ? completed.RepairStatus
                : defect.AssignedTo is null ? "new" : "in_progress";
            var response = new VehicleDefectResponse(
                defect.Id,
                defect.ErrorCode,
                defect.Symptoms,
                status,
                defect.CreatedAt,
                status == "repaired" || completed?.OperationalState == "operating" ? null : completed?.DowntimeStartedAt ?? defect.DowntimeStartedAt,
                defect.CreatedBy,
                createdByName,
                defect.AssignedTo,
                assignedToName,
                defect.RepairStartedAt,
                photosByDefect.GetValueOrDefault(defect.Id) ?? [],
                videosByDefect.GetValueOrDefault(defect.Id) ?? [],
                defect.NodeName,
                defect.FailureReason,
                status == "repaired" ? "healthy" : completed?.OperationalState ?? defect.OperationalState);
            return (defect.VehicleId, response);
        }).ToArray();
    }

    /// <summary>Читает показания моточасов техники.</summary>
    private async Task<IReadOnlyList<VehicleHoursResponse>> GetHoursAsync(
        Guid vehicleId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        var query = dbContext.VehicleHourReadings.AsNoTracking()
            .Where(reading => reading.VehicleId == vehicleId);
        if (from.HasValue)
        {
            query = query.Where(reading => reading.ReadingDate >= from.Value);
        }
        if (to.HasValue)
        {
            query = query.Where(reading => reading.ReadingDate <= to.Value);
        }
        return await query
            .OrderByDescending(reading => reading.ReadingDate)
            .ThenByDescending(reading => reading.CreatedAt)
            .Select(reading => new VehicleHoursResponse(
                reading.Id,
                reading.ReadingDate,
                reading.EngineHours,
                reading.Note,
                reading.CreatedAt,
                reading.EngineNumber, reading.ReadingTime))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Подготавливает данные работ с участниками и вложениями для ответа клиенту.</summary>
    private async Task<IReadOnlyList<(Guid VehicleId, VehicleWorkResponse Response)>>
        GetWorkResponsesAsync(
            IQueryable<VehicleWorkRecord> query,
            CancellationToken cancellationToken)
    {
        var works = await query.ToListAsync(cancellationToken);
        if (works.Count == 0)
        {
            return [];
        }

        var userIds = works.Select(work => work.PerformedBy ?? work.CreatedBy)
            .Distinct()
            .ToArray();
        var userNames = await GetUserNamesAsync(userIds, cancellationToken);
        var positions = await dbContext.Users.AsNoTracking().Where(user => userIds.Contains(user.Id))
            .Select(user => new { user.Id, Position = user.Profession.Name })
            .ToDictionaryAsync(user => user.Id, user => user.Position, cancellationToken);
        var defectIds = works.Where(work => work.DefectId.HasValue)
            .Select(work => work.DefectId!.Value)
            .Distinct()
            .ToArray();
        var defectNames = await dbContext.VehicleDefects.AsNoTracking()
            .Where(defect => defectIds.Contains(defect.Id))
            .ToDictionaryAsync(defect => defect.Id, defect => defect.NodeName,
                cancellationToken);
        var workIds = works.Select(work => work.Id).ToArray();
        var participantsByWork = await GetParticipantResponsesAsync(workIds, cancellationToken);
        var photos = await dbContext.VehicleWorkPhotos.AsNoTracking()
            .Where(photo => workIds.Contains(photo.WorkId))
            .OrderBy(photo => photo.CreatedAt)
            .ThenBy(photo => photo.Id)
            .Select(photo => new
            {
                photo.WorkId,
                photo.Id,
                photo.FileName,
                photo.ContentType,
                photo.Size,
                ContentLength = photo.Content == null
                    ? 0L
                    : (long)photo.Content.Length
            })
            .ToListAsync(cancellationToken);
        var videos = await dbContext.VehicleWorkVideos.AsNoTracking()
            .Where(video => workIds.Contains(video.WorkId))
            .OrderBy(video => video.CreatedAt)
            .ThenBy(video => video.Id)
            .Select(video => new
            {
                video.WorkId,
                Response = new VehicleMediaResponse(
                    video.Id, video.FileName, video.ContentType, video.Size)
            })
            .ToListAsync(cancellationToken);
        var requests = defectIds.Length == 0
            ? []
            : await dbContext.VehiclePartsRequests.AsNoTracking()
                .Where(request => defectIds.Contains(request.DefectId))
                .OrderBy(request => request.CreatedAt)
                .Select(request => new
                {
                    request.DefectId,
                    Response = new VehiclePartsRequestResponse(
                        request.Id,
                        request.DefectId,
                        request.RequestNumber,
                        request.RequestDate,
                        request.Description,
                        request.RequiredParts,
                        request.CreatedAt,
                        request.CompletedDate)
                })
                .ToListAsync(cancellationToken);
        var photosByWork = photos.GroupBy(photo => photo.WorkId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<VehicleWorkPhotoResponse>)group
                    .Select(item => new VehicleWorkPhotoResponse(
                        item.Id,
                        item.FileName,
                        item.ContentType,
                        item.Size == 0 ? item.ContentLength : item.Size))
                    .ToArray());
        var videosByWork = videos.GroupBy(video => video.WorkId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<VehicleMediaResponse>)group
                    .Select(item => item.Response)
                    .ToArray());
        var requestsByDefect = requests.GroupBy(request => request.DefectId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<VehiclePartsRequestResponse>)group
                    .Select(item => item.Response)
                    .ToArray());

        return works.Select(work =>
        {
            var performerId = work.PerformedBy ?? work.CreatedBy;
            var performerName = userNames.TryGetValue(performerId, out var performer)
                ? performer : work.Performer;
            var defectNodeName = work.DefectId is Guid workDefectId &&
                defectNames.TryGetValue(workDefectId, out var nodeName)
                    ? nodeName
                    : string.Empty;
            var workRequests = work.DefectId is Guid partsDefectId
                ? requestsByDefect.GetValueOrDefault(partsDefectId)
                : null;
            var response = new VehicleWorkResponse(
                work.Id,
                work.DefectId,
                defectNodeName,
                work.Description,
                work.CreatedAt,
                photosByWork.GetValueOrDefault(work.Id) ?? [],
                work.FailureCause,
                work.RepairStatus,
                work.RequiredParts,
                work.PerformedBy,
                performerName,
                work.CompletedAt,
                workRequests,
                videosByWork.GetValueOrDefault(work.Id) ?? [],
                work.CreatedBy,
                positions.GetValueOrDefault(performerId) ?? "",
                work.Location,
                work.OperationalState,
                participantsByWork.GetValueOrDefault(work.Id) ?? [],
                work.Title, work.WorkKind);
            return (work.VehicleId ?? Guid.Empty, response);
        }).ToArray();
    }

    /// <summary>Читает записи неисправностей техники.</summary>
    private async Task<IReadOnlyList<VehicleDefectResponse>> GetDefectsAsync(
        Guid vehicleId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        var query = dbContext.VehicleDefects.AsNoTracking()
            .Where(defect => defect.VehicleId == vehicleId);
        query = ApplyCreatedAtRange(query, from, to);
        var result = await GetDefectResponsesAsync(
            query.OrderByDescending(defect => defect.CreatedAt),
            cancellationToken);
        return result.Select(item => item.Response).ToArray();
    }

    /// <summary>Читает записи о работах по технике.</summary>
    private async Task<IReadOnlyList<VehicleWorkResponse>> GetWorksAsync(
        Guid vehicleId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        var query = dbContext.VehicleWorks.AsNoTracking()
            .Where(work => work.VehicleId == vehicleId);
        query = ApplyCreatedAtRange(query, from, to);
        var result = await GetWorkResponsesAsync(
            query.OrderByDescending(work => work.CreatedAt),
            cancellationToken);
        return result.Select(item => item.Response).ToArray();
    }

    /// <summary>Ограничивает запрос заданным диапазоном времени создания записей.</summary>
    private IQueryable<VehicleDefectRecord> ApplyCreatedAtRange(
        IQueryable<VehicleDefectRecord> query,
        DateOnly? from,
        DateOnly? to)
    {
        if (from.HasValue)
        {
            var start = from.Value.ToDateTime(TimeOnly.MinValue);
            query = query.Where(defect => defect.CreatedAt.Date >= start);
        }
        if (to.HasValue)
        {
            var end = to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue);
            query = query.Where(defect => defect.CreatedAt.Date < end);
        }
        return query;
    }

    /// <summary>Ограничивает запрос заданным диапазоном времени создания записей.</summary>
    private IQueryable<VehicleWorkRecord> ApplyCreatedAtRange(
        IQueryable<VehicleWorkRecord> query,
        DateOnly? from,
        DateOnly? to)
    {
        if (from.HasValue)
        {
            var start = from.Value.ToDateTime(TimeOnly.MinValue);
            query = query.Where(work => work.CreatedAt.Date >= start);
        }
        if (to.HasValue)
        {
            var end = to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue);
            query = query.Where(work => work.CreatedAt.Date < end);
        }
        return query;
    }

    /// <summary>Добавляет фотографии к указанной записи техники.</summary>
    private Task<IReadOnlyList<Guid>> AddPhotosAsync<TEntity>(
        IReadOnlyList<VehicleWorkPhotoUpload> photos,
        Func<byte[], string, CancellationToken, Task<string>> saveAsync,
        Func<Guid, VehicleMediaUpload, string, TEntity> create,
        Action<TEntity> add,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var uploads = photos.Select(photo =>
            new VehicleMediaUpload(
                photo.FileName, photo.ContentType, photo.Content)).ToArray();
        return AddMediaAsync(
            uploads,
            saveAsync,
            create,
            add,
            cancellationToken);
    }

    /// <summary>Сохраняет медиафайлы и записи об их принадлежности.</summary>
    private async Task<IReadOnlyList<Guid>> AddMediaAsync<TEntity>(
        IReadOnlyList<VehicleMediaUpload> items,
        Func<byte[], string, CancellationToken, Task<string>> saveAsync,
        Func<Guid, VehicleMediaUpload, string, TEntity> create,
        Action<TEntity> add,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var writtenPaths = new List<string>(items.Count);
        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken);
        try
        {
            var ids = new List<Guid>(items.Count);
            foreach (var item in items)
            {
                var id = Guid.NewGuid();
                var extension = GetSafeExtension(item.FileName, item.ContentType);
                var path = await saveAsync(
                    item.Content, $"upload{extension}", cancellationToken);
                writtenPaths.Add(path);
                add(create(id, item, path));
                ids.Add(id);
            }
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ids;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            foreach (var path in writtenPaths)
            {
                DeleteFile(path);
            }
            throw;
        }
    }

    /// <summary>Читает содержимое и метаданные медиафайла.</summary>
    private async Task<VehicleWorkPhotoContent?> GetMediaAsync<TEntity>(
        IQueryable<TEntity> query,
        Guid id,
        Func<TEntity, string> fileName,
        Func<TEntity, string> contentType,
        Func<TEntity, byte[]?> content,
        Func<TEntity, string?> storagePath,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var entity = await query.SingleOrDefaultAsync(
            item => EF.Property<Guid>(item, "Id") == id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        return await ReadMediaContentAsync(
            fileName(entity),
            contentType(entity),
            content(entity),
            storagePath(entity),
            cancellationToken);
    }

    /// <summary>Открывает поток чтения сохранённого медиафайла.</summary>
    private async Task<VehicleMediaStream?> GetMediaStreamAsync<TEntity>(
        IQueryable<TEntity> query,
        Guid id,
        Func<TEntity, string> fileName,
        Func<TEntity, string> contentType,
        Func<TEntity, byte[]?> content,
        Func<TEntity, string?> storagePath,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var entity = await query.SingleOrDefaultAsync(
            item => EF.Property<Guid>(item, "Id") == id,
            cancellationToken);
        if (entity is null)
        {
            return null;
        }

        var storedPath = storagePath(entity);
        if (storedPath is not null)
        {
            if (!IsManagedPath(storedPath))
            {
                return null;
            }

            try
            {
                var stream = new FileStream(
                    storedPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                return new VehicleMediaStream(
                    fileName(entity),
                    contentType(entity),
                    stream);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
        }

        return content(entity) is { } bytes
            ? new VehicleMediaStream(
                fileName(entity),
                contentType(entity),
                new MemoryStream(bytes, writable: false))
            : null;
    }

    /// <summary>Удаляет медиафайл и связанные с ним данные.</summary>
    private async Task<bool> DeleteMediaAsync<TEntity>(
        DbSet<TEntity> set,
        Guid id,
        Func<TEntity, string?> storagePath,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var entity = await set.SingleOrDefaultAsync(
            item => EF.Property<Guid>(item, "Id") == id, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        set.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (storagePath(entity) is string path)
        {
            DeleteFile(path);
        }
        return true;
    }

    /// <summary>Собирает пути медиафайлов неисправности.</summary>
    private async Task<IReadOnlyList<string>> GetDefectMediaPathsAsync(
        Guid defectId,
        CancellationToken cancellationToken)
    {
        var workIds = await dbContext.VehicleWorks.AsNoTracking()
            .Where(work => work.DefectId == defectId)
            .Select(work => work.Id)
            .ToArrayAsync(cancellationToken);
        var paths = await dbContext.VehicleDefectPhotos.AsNoTracking()
            .Where(photo => photo.DefectId == defectId && photo.StoragePath != null)
            .Select(photo => photo.StoragePath!)
            .ToListAsync(cancellationToken);
        paths.AddRange(await dbContext.VehicleDefectVideos.AsNoTracking()
            .Where(video => video.DefectId == defectId)
            .Select(video => video.StoragePath)
            .ToListAsync(cancellationToken));
        if (workIds.Length > 0)
        {
            paths.AddRange(await dbContext.VehicleWorkPhotos.AsNoTracking()
                .Where(photo => workIds.Contains(photo.WorkId) &&
                    photo.StoragePath != null)
                .Select(photo => photo.StoragePath!)
                .ToListAsync(cancellationToken));
            paths.AddRange(await dbContext.VehicleWorkVideos.AsNoTracking()
                .Where(video => workIds.Contains(video.WorkId))
                .Select(video => video.StoragePath)
                .ToListAsync(cancellationToken));
        }
        return paths;
    }

    /// <summary>Собирает пути медиафайлов выполненной работы.</summary>
    private async Task<IReadOnlyList<string>> GetWorkMediaPathsAsync(
        Guid workId,
        CancellationToken cancellationToken)
    {
        var photoPaths = await dbContext.VehicleWorkPhotos.AsNoTracking()
            .Where(photo => photo.WorkId == workId && photo.StoragePath != null)
            .Select(photo => photo.StoragePath!)
            .ToListAsync(cancellationToken);
        photoPaths.AddRange(await dbContext.VehicleWorkVideos.AsNoTracking()
            .Where(video => video.WorkId == workId)
            .Select(video => video.StoragePath)
            .ToListAsync(cancellationToken));
        return photoPaths;
    }

    /// <summary>Читает байты медиафайла из хранилища.</summary>
    private async Task<VehicleWorkPhotoContent?> ReadMediaContentAsync(
        string fileName,
        string mimeType,
        byte[]? content,
        string? path,
        CancellationToken cancellationToken)
    {
        if (content is not null)
        {
            return new(fileName, mimeType, content);
        }
        if (path is null || !IsManagedPath(path))
        {
            return null;
        }
        try
        {
            return new(
                fileName,
                mimeType,
                await File.ReadAllBytesAsync(path, cancellationToken),
                path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Удаляет файл из управляемого хранилища.</summary>
    private void DeleteFile(string path)
    {
        if (!IsManagedPath(path))
        {
            return;
        }
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Database deletion is already committed; filesystem cleanup is best effort.
        }
        catch (UnauthorizedAccessException)
        {
            // Database deletion is already committed; filesystem cleanup is best effort.
        }
    }

    /// <summary>Проверяет, относится ли путь к каталогам, которыми управляет приложение.</summary>
    private bool IsManagedPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return IsWithinDirectory(fullPath, mediaStorageOptions.PhotoDirectory) ||
               IsWithinDirectory(fullPath, mediaStorageOptions.VideoDirectory) ||
               IsWithinDirectory(fullPath, mediaStorageOptions.StagingPhotoDirectory) ||
               IsWithinDirectory(fullPath, mediaStorageOptions.StagingVideoDirectory);
    }

    /// <summary>Проверяет нахождение файла внутри разрешённого каталога.</summary>
    private static bool IsWithinDirectory(string path, string directory) =>
        path.StartsWith(
            Path.GetFullPath(directory) + Path.DirectorySeparatorChar,
            StringComparison.Ordinal);

    /// <summary>Получает отображаемые имена пользователей по идентификаторам.</summary>
    private async Task<IReadOnlyDictionary<Guid, string>> GetUserNamesAsync(
        Guid[] userIds,
        CancellationToken cancellationToken)
    {
        if (userIds.Length == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var users = await dbContext.Users.AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .Select(user => new
            {
                user.Id,
                user.LastName,
                user.FirstName,
                user.MiddleName
            })
            .ToListAsync(cancellationToken);
        return users.ToDictionary(
            user => user.Id,
            user => FormatUserName(
                user.LastName, user.FirstName, user.MiddleName));
    }

    /// <summary>Форматирует имя пользователя для журнала техники.</summary>
    private static string FormatUserName(
        string lastName,
        string firstName,
        string middleName) =>
        string.Join(
            ' ',
            new[] { lastName, firstName, middleName }
                .Where(part => !string.IsNullOrEmpty(part)));

    /// <summary>Выбирает допустимое расширение медиафайла.</summary>
    private static string GetSafeExtension(string fileName, string contentType)
    {
        var allowed = contentType.ToLowerInvariant() switch
        {
            "image/jpeg" => new[] { ".jpg", ".jpeg" },
            "image/png" => new[] { ".png" },
            "image/webp" => new[] { ".webp" },
            "video/mp4" => new[] { ".mp4" },
            "video/webm" => new[] { ".webm" },
            "video/quicktime" => new[] { ".mov" },
            _ => Array.Empty<string>()
        };
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return allowed.Contains(extension, StringComparer.OrdinalIgnoreCase)
            ? extension
            : allowed.FirstOrDefault() ?? string.Empty;
    }

    /// <summary>Удаляет компоненты пути из имени загруженного файла.</summary>
    private static string SafeFileName(string value)
    {
        var name = value.Replace('\\', '/').Split('/').Last();
        name = new string(name.Where(character => !char.IsControl(character)).ToArray());
        return name.Length <= 255 ? name : name[..255];
    }
}
