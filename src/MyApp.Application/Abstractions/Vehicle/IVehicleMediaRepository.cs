using MyApp.Application.DTO;

namespace MyApp.Application.Abstractions;

public interface IVehicleMediaRepository
{
    Task<int> GetDefectPhotoCountAsync(
        Guid defectId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> AddDefectPhotosAsync(
        Guid defectId,
        IReadOnlyList<VehicleWorkPhotoUpload> photos,
        CancellationToken cancellationToken);

    Task<VehicleWorkPhotoContent?> GetDefectPhotoAsync(
        Guid photoId,
        CancellationToken cancellationToken);

    Task<bool> DeleteDefectPhotoAsync(
        Guid photoId,
        CancellationToken cancellationToken);

    Task<int> GetDefectVideoCountAsync(Guid defectId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> AddDefectVideosAsync(
        Guid defectId, IReadOnlyList<VehicleMediaUpload> videos,
        CancellationToken cancellationToken);

    Task<VehicleWorkPhotoContent?> GetDefectVideoAsync(
        Guid videoId, CancellationToken cancellationToken);

    Task<bool> DeleteDefectVideoAsync(Guid videoId, CancellationToken cancellationToken);

    Task<bool> CanManageMediaAsync(
        string category, Guid mediaId, Guid userId,
        CancellationToken cancellationToken);

    Task<int> GetWorkPhotoCountAsync(
        Guid workId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> AddWorkPhotosAsync(
        Guid workId,
        IReadOnlyList<VehicleWorkPhotoUpload> photos,
        CancellationToken cancellationToken);

    Task<VehicleWorkPhotoContent?> GetWorkPhotoAsync(
        Guid photoId,
        CancellationToken cancellationToken);

    Task<bool> DeleteWorkPhotoAsync(
        Guid photoId,
        CancellationToken cancellationToken);

    Task<int> GetWorkVideoCountAsync(Guid workId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> AddWorkVideosAsync(
        Guid workId, IReadOnlyList<VehicleMediaUpload> videos,
        CancellationToken cancellationToken);

    Task<VehicleWorkPhotoContent?> GetWorkVideoAsync(
        Guid videoId, CancellationToken cancellationToken);

    Task<bool> DeleteWorkVideoAsync(Guid videoId, CancellationToken cancellationToken);
}
