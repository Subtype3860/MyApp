namespace MyApp.Application.DTO;

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

public sealed record CreateUserResponse(Guid Id, string TemporaryPassword);

public sealed record UpdateUserResponse(Guid Id);

public sealed record UserProfileResponse(
    Guid Id,
    string FirstName,
    string MiddleName,
    string LastName,
    string UserName,
    string Position,
    bool HasAvatar);

public sealed record UpdateProfileRequest(
    string UserName,
    string? CurrentPassword,
    string? NewPassword);

public sealed record UserAvatarResponse(
    byte[] Content,
    string ContentType);
