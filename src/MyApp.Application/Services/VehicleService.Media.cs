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
