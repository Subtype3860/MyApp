using MyApp.Application.Common;
using MyApp.Application.DTO;

namespace MyApp.Application.Services;

/// <summary>Прикладные операции пользователей.</summary>
public interface IUserService
{
    /// <summary>Возвращает список пользователей.</summary>
    Task<IReadOnlyList<UserListItem>> GetAllAsync(
        CancellationToken cancellationToken);

    /// <summary>Создаёт пользователя.</summary>
    Task<ServiceResult<CreateUserResponse>> CreateAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken);

    /// <summary>Обновляет данные пользователя.</summary>
    Task<ServiceResult<UpdateUserResponse>> UpdateAsync(
        Guid id,
        UpdateUserRequest request,
        CancellationToken cancellationToken);

    /// <summary>Возвращает профиль указанного пользователя.</summary>
    Task<ServiceResult<UserProfileResponse>> GetProfileAsync(
        Guid id,
        CancellationToken cancellationToken);

    /// <summary>Обновляет личные данные пользователя с проверкой запроса.</summary>
    Task<ServiceResult<UserProfileResponse>> UpdateProfileAsync(
        Guid id,
        UpdateProfileRequest request,
        CancellationToken cancellationToken);

    /// <summary>Возвращает изображение аватара пользователя.</summary>
    Task<ServiceResult<UserAvatarResponse>> GetAvatarAsync(
        Guid id,
        CancellationToken cancellationToken);

    /// <summary>Проверяет и сохраняет новое изображение аватара пользователя.</summary>
    Task<ServiceResult<UserProfileResponse>> UpdateAvatarAsync(
        Guid id,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken);

    /// <summary>Удаляет аватар пользователя.</summary>
    Task<ServiceResult<UserProfileResponse>> DeleteAvatarAsync(
        Guid id,
        CancellationToken cancellationToken);
}
