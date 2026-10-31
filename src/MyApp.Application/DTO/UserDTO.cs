namespace MyApp.Application.DTO;

/// <summary>Данные пользователя для административного списка.</summary>
public sealed record UserListItem(
    Guid Id,
    string FirstName,
    string MiddleName,
    string LastName,
    string UserName,
    Guid PositionId,
    string Position,
    string Role,
    IReadOnlyList<string> Permissions,
    DateTime CreatedAt);

/// <summary>Результат создания пользователя, возвращаемый администратору.</summary>
public sealed record CreateUserResponse(Guid Id, string TemporaryPassword);

/// <summary>Результат обновления учётной записи пользователя.</summary>
public sealed record UpdateUserResponse(Guid Id);

/// <summary>Личные данные и сведения о доступе текущего пользователя.</summary>
public sealed record UserProfileResponse(
    Guid Id,
    string FirstName,
    string MiddleName,
    string LastName,
    string UserName,
    string Position,
    bool HasAvatar);

/// <summary>Изменения личных данных и пароля пользователя.</summary>
public sealed record UpdateProfileRequest(
    string UserName,
    string? CurrentPassword,
    string? NewPassword);

/// <summary>Содержимое аватара и его MIME-тип.</summary>
public sealed record UserAvatarResponse(
    byte[] Content,
    string ContentType);
