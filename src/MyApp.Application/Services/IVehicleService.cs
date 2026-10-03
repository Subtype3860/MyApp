using MyApp.Application.Common;
using MyApp.Application.DTO;

namespace MyApp.Application.Services;

/// <summary>
/// Определяет бизнес-операции над транспортом: журнал эксплуатации,
/// закупки, неисправности, моточасы, ремонтные работы и связанные с ними
/// фото- и видеовложения.
/// </summary>
public interface IVehicleService
{
    /// <summary>Возвращает список всей учтённой техники.</summary>
    Task<IReadOnlyList<VehicleResponse>> GetVehiclesAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// Возвращает полный журнал техники: закупки, неисправности, моточасы
    /// и ремонтные работы за указанный период.
    /// </summary>
    /// <param name="vehicleId">Идентификатор техники.</param>
    /// <param name="from">Начало периода (включительно), либо null.</param>
    /// <param name="to">Конец периода (включительно), либо null.</param>
    /// <returns>Журнал техники или null, если техника не найдена.</returns>
    Task<VehicleJournalResponse?> GetJournalAsync(
        Guid vehicleId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken);

    /// <summary>Returns repair journals for all vehicles in one batch.</summary>
    Task<IReadOnlyList<VehicleJournalResponse>> GetRepairJournalsAsync(
        CancellationToken cancellationToken);

    /// <summary>Регистрирует заявку на закупку материалов для техники.</summary>
    /// <param name="vehicleId">Идентификатор техники.</param>
    /// <param name="request">Данные заявки на закупку.</param>
    /// <param name="createdBy">Идентификатор пользователя, создавшего заявку.</param>
    Task<ServiceResult<Guid>> AddPurchaseAsync(
        Guid vehicleId,
        VehiclePurchaseRequest request,
        Guid createdBy,
        CancellationToken cancellationToken);

    /// <summary>Создаёт заявку на ремонт (неисправность) техники.</summary>
    /// <param name="vehicleId">Идентификатор техники.</param>
    /// <param name="request">Описание неисправности и время начала простоя.</param>
    /// <param name="createdBy">Идентификатор пользователя, создавшего заявку.</param>
    Task<ServiceResult<Guid>> AddDefectAsync(
        Guid vehicleId,
        VehicleDefectRequest request,
        Guid createdBy,
        CancellationToken cancellationToken);

    /// <summary>Назначает неисправность на текущего пользователя (принять в работу).</summary>
    /// <param name="defectId">Идентификатор неисправности.</param>
    /// <param name="userId">Идентификатор пользователя, принимающего задачу.</param>
    Task<ServiceResult<bool>> ClaimDefectAsync(
        Guid defectId, Guid userId, CancellationToken cancellationToken);

    /// <summary>Добавляет фотографии неисправности.</summary>
    /// <param name="defectId">Идентификатор неисправности.</param>
    /// <param name="photos">Загружаемые файлы фотографий.</param>
    /// <param name="userId">Идентификатор пользователя, выполняющего загрузку.</param>
    /// <returns>Идентификаторы созданных записей фотографий.</returns>
    Task<ServiceResult<IReadOnlyList<Guid>>> AddDefectPhotosAsync(
        Guid defectId,
        IReadOnlyList<VehicleWorkPhotoUpload> photos,
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>Возвращает содержимое фотографии неисправности по её идентификатору.</summary>
    Task<VehicleWorkPhotoContent?> GetDefectPhotoAsync(
        Guid photoId,
        CancellationToken cancellationToken);

    /// <summary>Удаляет фотографию неисправности.</summary>
    /// <param name="photoId">Идентификатор фотографии.</param>
    /// <param name="userId">Идентификатор пользователя, выполняющего удаление.</param>
    Task<ServiceResult<bool>> DeleteDefectPhotoAsync(
        Guid photoId, Guid userId, CancellationToken cancellationToken);

    /// <summary>Добавляет видеозаписи неисправности.</summary>
    /// <param name="defectId">Идентификатор неисправности.</param>
    /// <param name="videos">Загружаемые видеофайлы.</param>
    /// <param name="userId">Идентификатор пользователя, выполняющего загрузку.</param>
    /// <returns>Идентификаторы созданных записей видео.</returns>
    Task<ServiceResult<IReadOnlyList<Guid>>> AddDefectVideosAsync(
        Guid defectId, IReadOnlyList<VehicleMediaUpload> videos, Guid userId,
        CancellationToken cancellationToken);

    /// <summary>Возвращает содержимое видеозаписи неисправности по её идентификатору.</summary>
    Task<VehicleWorkPhotoContent?> GetDefectVideoAsync(
        Guid videoId, CancellationToken cancellationToken);

    /// <summary>Удаляет видеозапись неисправности.</summary>
    /// <param name="videoId">Идентификатор видеозаписи.</param>
    /// <param name="userId">Идентификатор пользователя, выполняющего удаление.</param>
    Task<ServiceResult<bool>> DeleteDefectVideoAsync(
        Guid videoId, Guid userId, CancellationToken cancellationToken);

    /// <summary>Добавляет запись о показаниях моточасов техники.</summary>
    /// <param name="vehicleId">Идентификатор техники.</param>
    /// <param name="request">Дата снятия показаний и значение моточасов.</param>
    /// <param name="createdBy">Идентификатор пользователя, создавшего запись.</param>
    Task<ServiceResult<Guid>> AddHoursAsync(
        Guid vehicleId,
        VehicleHoursRequest request,
        Guid createdBy,
        CancellationToken cancellationToken);

    /// <summary>Импортирует показания моточасов из CSV- или XLSX-файла.</summary>
    /// <param name="file">Поток с содержимым файла.</param>
    /// <param name="fileExtension">Расширение файла (".csv" или ".xlsx").</param>
    /// <param name="createdBy">Идентификатор пользователя, выполняющего импорт.</param>
    /// <param name="readingDate">Дата, к которой относятся импортируемые показания.</param>
    /// <returns>Количество импортированных записей и список ошибок по строкам.</returns>
    Task<VehicleHoursImportResponse> ImportHoursAsync(
        Stream file,
        string fileExtension,
        Guid createdBy,
        DateOnly readingDate,
        CancellationToken cancellationToken);

    /// <summary>Регистрирует выполненную ремонтную работу по неисправности.</summary>
    /// <param name="vehicleId">Идентификатор техники.</param>
    /// <param name="request">Данные о выполненных работах и итоговом статусе.</param>
    /// <param name="createdBy">Идентификатор пользователя, создавшего запись.</param>
    Task<ServiceResult<Guid>> AddWorkAsync(
        Guid vehicleId,
        VehicleWorkRequest request,
        Guid createdBy,
        CancellationToken cancellationToken);

    /// <summary>Завершает неисправность, фиксируя итоговый статус ремонта.</summary>
    /// <param name="defectId">Идентификатор неисправности.</param>
    /// <param name="request">Данные о причине, описании и статусе ремонта.</param>
    /// <param name="userId">Идентификатор пользователя, завершающего задачу.</param>
    Task<ServiceResult<Guid>> CompleteDefectAsync(
        Guid defectId, VehicleWorkRequest request, Guid userId,
        CancellationToken cancellationToken);

    /// <summary>Добавляет фотографии ремонтных работ.</summary>
    /// <param name="workId">Идентификатор записи о ремонтных работах.</param>
    /// <param name="photos">Загружаемые файлы фотографий.</param>
    /// <param name="userId">Идентификатор пользователя, выполняющего загрузку.</param>
    /// <returns>Идентификаторы созданных записей фотографий.</returns>
    Task<ServiceResult<IReadOnlyList<Guid>>> AddWorkPhotosAsync(
        Guid workId,
        IReadOnlyList<VehicleWorkPhotoUpload> photos,
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>Возвращает содержимое фотографии ремонтных работ по её идентификатору.</summary>
    Task<VehicleWorkPhotoContent?> GetWorkPhotoAsync(
        Guid photoId,
        CancellationToken cancellationToken);

    /// <summary>Удаляет фотографию ремонтных работ.</summary>
    /// <param name="photoId">Идентификатор фотографии.</param>
    /// <param name="userId">Идентификатор пользователя, выполняющего удаление.</param>
    Task<ServiceResult<bool>> DeleteWorkPhotoAsync(
        Guid photoId, Guid userId, CancellationToken cancellationToken);

    /// <summary>Добавляет видеозаписи ремонтных работ.</summary>
    /// <param name="workId">Идентификатор записи о ремонтных работах.</param>
    /// <param name="videos">Загружаемые видеофайлы.</param>
    /// <param name="userId">Идентификатор пользователя, выполняющего загрузку.</param>
    /// <returns>Идентификаторы созданных записей видео.</returns>
    Task<ServiceResult<IReadOnlyList<Guid>>> AddWorkVideosAsync(
        Guid workId, IReadOnlyList<VehicleMediaUpload> videos, Guid userId,
        CancellationToken cancellationToken);

    /// <summary>Возвращает содержимое видеозаписи ремонтных работ по её идентификатору.</summary>
    Task<VehicleWorkPhotoContent?> GetWorkVideoAsync(
        Guid videoId, CancellationToken cancellationToken);

    /// <summary>Удаляет видеозапись ремонтных работ.</summary>
    /// <param name="videoId">Идентификатор видеозаписи.</param>
    /// <param name="userId">Идентификатор пользователя, выполняющего удаление.</param>
    Task<ServiceResult<bool>> DeleteWorkVideoAsync(
        Guid videoId, Guid userId, CancellationToken cancellationToken);

    Task<ServiceResult<Guid>> AddPartsRequestAsync(
        Guid defectId, VehiclePartsRequest request, Guid userId,
        CancellationToken cancellationToken);
    Task<ServiceResult<bool>> UpdatePartsRequestAsync(
        Guid requestId, VehiclePartsRequest request, Guid userId,
        CancellationToken cancellationToken);
    Task<ServiceResult<bool>> DeletePartsRequestAsync(
        Guid requestId, Guid userId, CancellationToken cancellationToken);

    /// <summary>Удаляет запись журнала (закупку, неисправность или работу) по категории.</summary>
    /// <param name="category">Категория записи ("purchases", "defects" или "works").</param>
    /// <param name="id">Идентификатор удаляемой записи.</param>
    /// <param name="userId">Идентификатор пользователя, выполняющего удаление.</param>
    /// <returns>true, если запись была удалена.</returns>
    Task<bool> DeleteEntryAsync(
        string category,
        Guid id,
        Guid userId,
        CancellationToken cancellationToken);
}
