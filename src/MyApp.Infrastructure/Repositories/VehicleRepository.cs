using System.Data;
using Microsoft.EntityFrameworkCore;
using MyApp.Application.Abstractions;
using MyApp.Application.DTO;
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
    public async Task<IReadOnlyList<VehicleResponse>> GetVehiclesAsync(
        CancellationToken cancellationToken)
    {
        var vehicles = await (
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
                vehicle.Vin ?? string.Empty))
            .ToListAsync(cancellationToken);

        return vehicles;
    }

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

    public async Task<IReadOnlyList<VehicleJournalResponse>> GetRepairJournalsAsync(
        CancellationToken cancellationToken)
    {
        var vehicles = await GetVehiclesAsync(cancellationToken);
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
                .Where(work => vehicleIds.Contains(work.VehicleId))
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

        return vehicles.Select(vehicle => new VehicleJournalResponse(
            vehicle,
            [],
            defectsByVehicle.GetValueOrDefault(vehicle.Id) ?? [],
            [],
            worksByVehicle.GetValueOrDefault(vehicle.Id) ?? [])).ToArray();
    }

    public Task<bool> VehicleExistsAsync(
        Guid vehicleId,
        CancellationToken cancellationToken) =>
        dbContext.Vehicles.AsNoTracking()
            .AnyAsync(vehicle => vehicle.Id == vehicleId, cancellationToken);

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
            NodeName = string.Empty,
            FailureReason = request.Symptoms,
            ErrorCode = request.ErrorCode ?? string.Empty,
            Symptoms = request.Symptoms,
            DowntimeStartedAt = request.DowntimeStartedAt,
            CreatedBy = createdBy
        };
        dbContext.VehicleDefects.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public Task<bool> DefectExistsAsync(
        Guid defectId,
        CancellationToken cancellationToken) =>
        dbContext.VehicleDefects.AsNoTracking()
            .AnyAsync(defect => defect.Id == defectId, cancellationToken);

    public Task<bool> DefectBelongsToVehicleAsync(
        Guid defectId,
        Guid vehicleId,
        CancellationToken cancellationToken) =>
        dbContext.VehicleDefects.AsNoTracking()
            .AnyAsync(
                defect => defect.Id == defectId && defect.VehicleId == vehicleId,
                cancellationToken);

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

    public Task<bool> IsDefectCreatorAsync(
        Guid defectId,
        Guid userId,
        CancellationToken cancellationToken) =>
        dbContext.VehicleDefects.AsNoTracking()
            .AnyAsync(
                defect => defect.Id == defectId && defect.CreatedBy == userId,
                cancellationToken);

    public Task<int> GetDefectPhotoCountAsync(
        Guid defectId,
        CancellationToken cancellationToken) =>
        dbContext.VehicleDefectPhotos.AsNoTracking()
            .CountAsync(photo => photo.DefectId == defectId, cancellationToken);

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

    public Task<bool> DeleteDefectPhotoAsync(
        Guid photoId,
        CancellationToken cancellationToken) =>
        DeleteMediaAsync(
            dbContext.VehicleDefectPhotos,
            photoId,
            entity => entity.StoragePath,
            cancellationToken);

    public Task<int> GetDefectVideoCountAsync(
        Guid defectId,
        CancellationToken cancellationToken) =>
        dbContext.VehicleDefectVideos.AsNoTracking()
            .CountAsync(video => video.DefectId == defectId, cancellationToken);

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

    public Task<bool> DeleteDefectVideoAsync(
        Guid videoId,
        CancellationToken cancellationToken) =>
        DeleteMediaAsync(
            dbContext.VehicleDefectVideos,
            videoId,
            entity => entity.StoragePath,
            cancellationToken);

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

        // A blank CSV cell carries forward the most recent value on or before
        // the import date. Fetch one most-recent row for each distinct vehicle
        // in a SINGLE PostgreSQL query rather than querying inside the loop.
        var blankVehicleIds = items
            .Where(item => !item.EngineHours.HasValue)
            .Select(item => item.VehicleId)
            .Distinct()
            .ToArray();
        var carriedHours = new Dictionary<Guid, decimal>();
        if (blankVehicleIds.Length > 0)
        {
            var lastReadings = await dbContext.VehicleHourReadings
                .AsNoTracking()
                .Where(reading =>
                    blankVehicleIds.Contains(reading.VehicleId) &&
                    reading.ReadingDate <= readingDate)
                .GroupBy(reading => reading.VehicleId)
                .Select(group => group
                    .OrderByDescending(reading => reading.ReadingDate)
                    .ThenByDescending(reading => reading.CreatedAt)
                    .Select(reading => new {
                        reading.VehicleId,
                        reading.EngineHours
                    })
                    .First())
                .ToArrayAsync(cancellationToken);

            foreach (var reading in lastReadings)
            {
                carriedHours[reading.VehicleId] = reading.EngineHours;
            }
        }

        foreach (var item in items)
        {
            // Repeated rows for the same vehicle must observe earlier rows of
            // this import, including an inserted or overwritten reading.
            var engineHours = item.EngineHours ??
                carriedHours.GetValueOrDefault(item.VehicleId);
            var now = DateTimeOffset.UtcNow;
            var updated = await dbContext.VehicleHourReadings
                .Where(reading =>
                    reading.VehicleId == item.VehicleId &&
                    reading.ReadingDate == readingDate)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(reading => reading.EngineHours, engineHours)
                        .SetProperty(reading => reading.Note, "Импорт CSV")
                        .SetProperty(reading => reading.CreatedBy, createdBy)
                        .SetProperty(reading => reading.CreatedAt, now),
                    cancellationToken);
            if (updated > 0)
            {
                carriedHours[item.VehicleId] = engineHours;
                continue;
            }

            dbContext.VehicleHourReadings.Add(new VehicleHourReadingRecord
            {
                Id = Guid.NewGuid(),
                VehicleId = item.VehicleId,
                ReadingDate = readingDate,
                EngineHours = engineHours,
                Note = "Импорт CSV",
                CreatedBy = createdBy
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            carriedHours[item.VehicleId] = engineHours;
        }
        await transaction.CommitAsync(cancellationToken);
    }

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
        await dbContext.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task<Guid?> CompleteDefectAsync(
        Guid defectId,
        VehicleWorkRequest request,
        Guid performedBy,
        bool administrator,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
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
            Performer = string.Empty,
            Note = string.Empty,
            DefectId = defectId,
            FailureCause = request.Cause,
            RepairStatus = request.Status!,
            RequiredParts = request.RequiredParts ?? string.Empty,
            PerformedBy = performedBy,
            CompletedAt = completedAt,
            CreatedBy = performedBy
        };
        dbContext.VehicleWorks.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return entity.Id;
    }

    public Task<bool> WorkExistsAsync(
        Guid workId,
        CancellationToken cancellationToken) =>
        dbContext.VehicleWorks.AsNoTracking()
            .AnyAsync(work => work.Id == workId, cancellationToken);

    public Task<bool> IsWorkPerformerAsync(
        Guid workId,
        Guid userId,
        CancellationToken cancellationToken) =>
        dbContext.VehicleWorks.AsNoTracking()
            .AnyAsync(
                work => work.Id == workId &&
                    (work.PerformedBy == userId || work.CreatedBy == userId),
                cancellationToken);

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

    public Task<int> GetWorkPhotoCountAsync(
        Guid workId,
        CancellationToken cancellationToken) =>
        dbContext.VehicleWorkPhotos.AsNoTracking()
            .CountAsync(photo => photo.WorkId == workId, cancellationToken);

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

    public Task<bool> DeleteWorkPhotoAsync(
        Guid photoId,
        CancellationToken cancellationToken) =>
        DeleteMediaAsync(
            dbContext.VehicleWorkPhotos,
            photoId,
            entity => entity.StoragePath,
            cancellationToken);

    public Task<int> GetWorkVideoCountAsync(
        Guid workId,
        CancellationToken cancellationToken) =>
        dbContext.VehicleWorkVideos.AsNoTracking()
            .CountAsync(video => video.WorkId == workId, cancellationToken);

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

    public Task<bool> DeleteWorkVideoAsync(
        Guid videoId,
        CancellationToken cancellationToken) =>
        DeleteMediaAsync(
            dbContext.VehicleWorkVideos,
            videoId,
            entity => entity.StoragePath,
            cancellationToken);

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
            CreatedBy = createdBy
        };
        dbContext.VehiclePartsRequests.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return entity.Id;
    }

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
                        request.RequiredParts ?? string.Empty),
                cancellationToken);
        return updatedRows > 0;
    }

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
                request.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> DeletePartsRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken) =>
        await dbContext.VehiclePartsRequests
            .Where(request => request.Id == requestId)
            .ExecuteDeleteAsync(cancellationToken) > 0;

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
                vehicle.Vin ?? string.Empty))
            .SingleOrDefaultAsync(cancellationToken);

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
            .Select(work => new
            {
                DefectId = work.DefectId!.Value,
                work.RepairStatus,
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
                defect.DowntimeStartedAt,
                defect.CreatedBy,
                createdByName,
                defect.AssignedTo,
                assignedToName,
                defect.RepairStartedAt,
                photosByDefect.GetValueOrDefault(defect.Id) ?? [],
                videosByDefect.GetValueOrDefault(defect.Id) ?? [],
                defect.NodeName,
                defect.FailureReason);
            return (defect.VehicleId, response);
        }).ToArray();
    }

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
                reading.CreatedAt))
            .ToListAsync(cancellationToken);
    }

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

        var userIds = works.Where(work => work.PerformedBy.HasValue)
            .Select(work => work.PerformedBy!.Value)
            .Distinct()
            .ToArray();
        var userNames = await GetUserNamesAsync(userIds, cancellationToken);
        var defectIds = works.Where(work => work.DefectId.HasValue)
            .Select(work => work.DefectId!.Value)
            .Distinct()
            .ToArray();
        var defectNames = await dbContext.VehicleDefects.AsNoTracking()
            .Where(defect => defectIds.Contains(defect.Id))
            .ToDictionaryAsync(defect => defect.Id, defect => defect.NodeName,
                cancellationToken);
        var workIds = works.Select(work => work.Id).ToArray();
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
                        request.CreatedAt)
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
            var performerName = work.PerformedBy is Guid performerId &&
                userNames.TryGetValue(performerId, out var performer)
                    ? performer
                    : string.Empty;
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
                videosByWork.GetValueOrDefault(work.Id) ?? []);
            return (work.VehicleId, response);
        }).ToArray();
    }

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

    private bool IsManagedPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return IsWithinDirectory(fullPath, mediaStorageOptions.PhotoDirectory) ||
               IsWithinDirectory(fullPath, mediaStorageOptions.VideoDirectory) ||
               IsWithinDirectory(fullPath, mediaStorageOptions.StagingPhotoDirectory) ||
               IsWithinDirectory(fullPath, mediaStorageOptions.StagingVideoDirectory);
    }

    private static bool IsWithinDirectory(string path, string directory) =>
        path.StartsWith(
            Path.GetFullPath(directory) + Path.DirectorySeparatorChar,
            StringComparison.Ordinal);

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

    private static string FormatUserName(
        string lastName,
        string firstName,
        string middleName) =>
        string.Join(
            ' ',
            new[] { lastName, firstName, middleName }
                .Where(part => !string.IsNullOrEmpty(part)));

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

    private static string SafeFileName(string value)
    {
        var name = value.Replace('\\', '/').Split('/').Last();
        name = new string(name.Where(character => !char.IsControl(character)).ToArray());
        return name.Length <= 255 ? name : name[..255];
    }
}
