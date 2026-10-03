using MyApp.Application.DTO;

namespace MyApp.Application.Abstractions;

/// <summary>
/// Репозиторий доступа к данным журнала техники: закупки, неисправности,
/// моточасы, ремонтные работы и их медиавложения (фото/видео), заявки на запчасти.
/// </summary>
public interface IVehicleRepository
{
    /// <summary>Возвращает список всей техники.</summary>
    Task<IReadOnlyList<VehicleResponse>> GetVehiclesAsync(
        CancellationToken cancellationToken);

    /// <summary>Возвращает журнал техники (закупки, неисправности, моточасы, работы) за период.</summary>
    Task<VehicleJournalResponse?> GetJournalAsync(
        Guid vehicleId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns repair journals for all vehicles using batched queries.
    /// </summary>
    Task<IReadOnlyList<VehicleJournalResponse>> GetRepairJournalsAsync(
        CancellationToken cancellationToken);

    /// <summary>Проверяет существование единицы техники.</summary>
    Task<bool> VehicleExistsAsync(Guid vehicleId, CancellationToken cancellationToken);

    /// <summary>Добавляет запись о закупке техники.</summary>
    Task<Guid> AddPurchaseAsync(
        Guid vehicleId,
        VehiclePurchaseRequest request,
        Guid createdBy,
        CancellationToken cancellationToken);

    /// <summary>Добавляет запись о неисправности техники.</summary>
    Task<Guid> AddDefectAsync(
        Guid vehicleId,
        VehicleDefectRequest request,
        Guid createdBy,
        CancellationToken cancellationToken);

    /// <summary>Проверяет существование неисправности.</summary>
    Task<bool> DefectExistsAsync(Guid defectId, CancellationToken cancellationToken);

    /// <summary>Проверяет принадлежность неисправности указанной технике.</summary>
    Task<bool> DefectBelongsToVehicleAsync(
        Guid defectId,
        Guid vehicleId,
        CancellationToken cancellationToken);

    /// <summary>Переводит неисправность в статус «в работе», закрепляя её за пользователем.</summary>
    Task<bool> ClaimDefectAsync(
        Guid defectId,
        Guid userId,
        CancellationToken cancellationToken);
    /// <summary>Проверяет, является ли пользователь автором неисправности.</summary>
    Task<bool> IsDefectCreatorAsync(
        Guid defectId, Guid userId, CancellationToken cancellationToken);

    /// <summary>Возвращает количество фотографий, прикреплённых к неисправности.</summary>
    Task<int> GetDefectPhotoCountAsync(
        Guid defectId,
        CancellationToken cancellationToken);

    /// <summary>Сохраняет фотографии неисправности и возвращает их идентификаторы.</summary>
    Task<IReadOnlyList<Guid>> AddDefectPhotosAsync(
        Guid defectId,
        IReadOnlyList<VehicleWorkPhotoUpload> photos,
        CancellationToken cancellationToken);

    /// <summary>Возвращает содержимое фотографии неисправности по идентификатору.</summary>
    Task<VehicleWorkPhotoContent?> GetDefectPhotoAsync(
        Guid photoId,
        CancellationToken cancellationToken);

    /// <summary>Удаляет фотографию неисправности.</summary>
    Task<bool> DeleteDefectPhotoAsync(
        Guid photoId,
        CancellationToken cancellationToken);

    /// <summary>Возвращает количество видео, прикреплённых к неисправности.</summary>
    Task<int> GetDefectVideoCountAsync(Guid defectId, CancellationToken cancellationToken);
    /// <summary>Сохраняет видео неисправности и возвращает их идентификаторы.</summary>
    Task<IReadOnlyList<Guid>> AddDefectVideosAsync(
        Guid defectId, IReadOnlyList<VehicleMediaUpload> videos,
        CancellationToken cancellationToken);
    /// <summary>Возвращает содержимое видео неисправности по идентификатору.</summary>
    Task<VehicleWorkPhotoContent?> GetDefectVideoAsync(
        Guid videoId, CancellationToken cancellationToken);
    /// <summary>Удаляет видео неисправности.</summary>
    Task<bool> DeleteDefectVideoAsync(Guid videoId, CancellationToken cancellationToken);

    /// <summary>Добавляет запись о моточасах техники.</summary>
    Task<Guid> AddHoursAsync(
        Guid vehicleId,
        VehicleHoursRequest request,
        Guid createdBy,
        CancellationToken cancellationToken);

    /// <summary>Импортирует несколько записей о моточасах за одну дату.</summary>
    Task ImportHoursAsync(
        DateOnly readingDate,
        IReadOnlyList<VehicleHoursImportItem> items,
        Guid createdBy,
        CancellationToken cancellationToken);

    /// <summary>Добавляет запись о ремонтных работах для техники.</summary>
    Task<Guid> AddWorkAsync(
        Guid vehicleId,
        VehicleWorkRequest request,
        Guid createdBy,
        CancellationToken cancellationToken);

    /// <summary>Завершает неисправность, создавая запись выполненных работ.</summary>
    Task<Guid?> CompleteDefectAsync(
        Guid defectId,
        VehicleWorkRequest request,
        Guid performedBy,
        bool administrator,
        CancellationToken cancellationToken);

    /// <summary>Проверяет существование записи о ремонтных работах.</summary>
    Task<bool> WorkExistsAsync(Guid workId, CancellationToken cancellationToken);
    /// <summary>Проверяет, является ли пользователь исполнителем ремонтных работ.</summary>
    Task<bool> IsWorkPerformerAsync(
        Guid workId, Guid userId, CancellationToken cancellationToken);
    /// <summary>Проверяет, может ли пользователь управлять медиавложением указанной категории.</summary>
    Task<bool> CanManageMediaAsync(
        string category, Guid mediaId, Guid userId,
        CancellationToken cancellationToken);

    /// <summary>Возвращает количество фотографий, прикреплённых к ремонтным работам.</summary>
    Task<int> GetWorkPhotoCountAsync(
        Guid workId,
        CancellationToken cancellationToken);

    /// <summary>Сохраняет фотографии ремонтных работ и возвращает их идентификаторы.</summary>
    Task<IReadOnlyList<Guid>> AddWorkPhotosAsync(
        Guid workId,
        IReadOnlyList<VehicleWorkPhotoUpload> photos,
        CancellationToken cancellationToken);

    /// <summary>Возвращает содержимое фотографии ремонтных работ по идентификатору.</summary>
    Task<VehicleWorkPhotoContent?> GetWorkPhotoAsync(
        Guid photoId,
        CancellationToken cancellationToken);

    /// <summary>Удаляет фотографию ремонтных работ.</summary>
    Task<bool> DeleteWorkPhotoAsync(
        Guid photoId,
        CancellationToken cancellationToken);

    /// <summary>Возвращает количество видео, прикреплённых к ремонтным работам.</summary>
    Task<int> GetWorkVideoCountAsync(Guid workId, CancellationToken cancellationToken);
    /// <summary>Сохраняет видео ремонтных работ и возвращает их идентификаторы.</summary>
    Task<IReadOnlyList<Guid>> AddWorkVideosAsync(
        Guid workId, IReadOnlyList<VehicleMediaUpload> videos,
        CancellationToken cancellationToken);
    /// <summary>Возвращает содержимое видео ремонтных работ по идентификатору.</summary>
    Task<VehicleWorkPhotoContent?> GetWorkVideoAsync(
        Guid videoId, CancellationToken cancellationToken);
    /// <summary>Удаляет видео ремонтных работ.</summary>
    Task<bool> DeleteWorkVideoAsync(Guid videoId, CancellationToken cancellationToken);

    Task<Guid?> AddPartsRequestAsync(
        Guid defectId,
        VehiclePartsRequest request,
        Guid createdBy,
        CancellationToken cancellationToken);
    Task<bool> UpdatePartsRequestAsync(
        Guid requestId,
        VehiclePartsRequest request,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<VehiclePartsRequestResponse>> GetPartsRequestsAsync(
        Guid defectId,
        CancellationToken cancellationToken);
    Task<bool> DeletePartsRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken);
    /// <summary>Удаляет запись журнала техники (закупка, неисправность, моточасы или работа).</summary>
    Task<bool> DeleteEntryAsync(
        string category,
        Guid id,
        CancellationToken cancellationToken);
}
