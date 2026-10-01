using System.Text;
using MyApp.Application.Abstractions;
using MyApp.Application.Common;
using MyApp.Application.DTO;
using MyApp.Domain.Entities;
using MyApp.Application.Security;

namespace MyApp.Application.Services;

public sealed class UserService(
    IUserRepository userRepository,
    IProfessionRepository professionRepository,
    IPasswordHasher passwordHasher) : IUserService
{
    public async Task<IReadOnlyList<UserListItem>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        var users = await userRepository.GetAllAsync(cancellationToken);
        return users.Select(user => new UserListItem(
            user.Id,
            user.FirstName,
            user.MiddleName,
            user.LastName,
            user.UserName,
            user.PositionId,
            user.Profession.Name,
            user.Role,
            GetPermissions(user),
            user.CreatedAt)).ToArray();
    }

    public async Task<ServiceResult<CreateUserResponse>> CreateAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var errors = ValidateCreateRequest(request);
        if (errors.Count > 0)
        {
            return ServiceResult<CreateUserResponse>.Validation(errors);
        }

        var userName = BuildUserName(
            request.FirstName,
            request.MiddleName,
            request.LastName);
        if (await userRepository.UserNameExistsAsync(
            userName,
            null,
            cancellationToken))
        {
            return ServiceResult<CreateUserResponse>.Conflict(
                "Пользователь с таким логином уже существует.");
        }

        if (!await professionRepository.ExistsAsync(
            request.ProfessionId,
            cancellationToken))
        {
            return ServiceResult<CreateUserResponse>.Validation(
                new Dictionary<string, string[]>
                {
                    [nameof(request.ProfessionId)] =
                        ["Выбранная профессия не найдена."]
                });
        }

        var temporaryPassword = BuildTemporaryPassword(DateTime.Now);
        var user = new User
        {
            Id = Guid.NewGuid(),
            FirstName = request.FirstName.Trim(),
            MiddleName = request.MiddleName.Trim(),
            LastName = request.LastName.Trim(),
            UserName = userName,
            Email = $"{userName.ToLowerInvariant()}@local",
            PasswordHash = passwordHasher.Hash(temporaryPassword),
            PositionId = request.ProfessionId,
            Role = "user",
            Permissions = string.Join(',', Permissions.All),
            CreatedAt = DateTime.UtcNow
        };

        await userRepository.AddAsync(user, cancellationToken);
        await userRepository.SaveChangesAsync(cancellationToken);

        return ServiceResult<CreateUserResponse>.Success(
            new CreateUserResponse(user.Id, temporaryPassword));
    }

    public async Task<ServiceResult<UpdateUserResponse>> UpdateAsync(
        Guid id,
        UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        var errors = ValidateUpdateRequest(request);
        if (errors.Count > 0)
        {
            return ServiceResult<UpdateUserResponse>.Validation(errors);
        }

        var user = await userRepository.FindByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return ServiceResult<UpdateUserResponse>.NotFound();
        }

        var userName = request.UserName.Trim();
        if (user.UserName.Equals("boora", StringComparison.OrdinalIgnoreCase) &&
            !userName.Equals("boora", StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult<UpdateUserResponse>.Conflict(
                "Логин системного администратора изменять нельзя.");
        }

        if (await userRepository.UserNameExistsAsync(
            userName,
            id,
            cancellationToken))
        {
            return ServiceResult<UpdateUserResponse>.Conflict(
                "Пользователь с таким логином уже существует.");
        }

        if (!await professionRepository.ExistsAsync(
            request.ProfessionId,
            cancellationToken))
        {
            return ServiceResult<UpdateUserResponse>.Validation(
                new Dictionary<string, string[]>
                {
                    [nameof(request.ProfessionId)] =
                        ["Выбранная профессия не найдена."]
                });
        }

        user.FirstName = request.FirstName.Trim();
        user.MiddleName = request.MiddleName.Trim();
        user.LastName = request.LastName.Trim();
        user.UserName = userName;
        user.Email = $"{userName.ToLowerInvariant()}@local";
        user.PositionId = request.ProfessionId;
        user.Role = userName.Equals("boora", StringComparison.OrdinalIgnoreCase)
            ? "administrator"
            : request.Role;
        user.Permissions = user.Role.Equals("administrator", StringComparison.OrdinalIgnoreCase)
            ? string.Join(',', Permissions.All)
            : string.Join(',', NormalizePermissions(request.Permissions));

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            user.PasswordHash = passwordHasher.Hash(request.Password);
        }

        await userRepository.SaveChangesAsync(cancellationToken);
        return ServiceResult<UpdateUserResponse>.Success(
            new UpdateUserResponse(user.Id));
    }

    public async Task<ServiceResult<UserProfileResponse>> GetProfileAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.FindByIdAsync(id, cancellationToken);
        return user is null
            ? ServiceResult<UserProfileResponse>.NotFound()
            : ServiceResult<UserProfileResponse>.Success(ToProfile(user));
    }

    public async Task<ServiceResult<UserProfileResponse>> UpdateProfileAsync(
        Guid id,
        UpdateProfileRequest request,
        CancellationToken cancellationToken)
    {
        var userName = request.UserName.Trim();
        var errors = new Dictionary<string, string[]>();
        if (userName.Length is < 3 or > 50)
        {
            errors[nameof(request.UserName)] =
                ["Логин должен содержать от 3 до 50 символов."];
        }
        if (!string.IsNullOrEmpty(request.NewPassword) &&
            request.NewPassword.Length < 6)
        {
            errors[nameof(request.NewPassword)] =
                ["Новый пароль должен содержать не менее 6 символов."];
        }
        if (errors.Count > 0)
        {
            return ServiceResult<UserProfileResponse>.Validation(errors);
        }

        var user = await userRepository.FindByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return ServiceResult<UserProfileResponse>.NotFound();
        }
        if (await userRepository.UserNameExistsAsync(
                userName, id, cancellationToken))
        {
            return ServiceResult<UserProfileResponse>.Conflict(
                "Пользователь с таким логином уже существует.");
        }
        if (!string.IsNullOrEmpty(request.NewPassword))
        {
            if (string.IsNullOrEmpty(request.CurrentPassword) ||
                !passwordHasher.Verify(
                    request.CurrentPassword,
                    user.PasswordHash))
            {
                return ServiceResult<UserProfileResponse>.Validation(
                    new Dictionary<string, string[]>
                    {
                        [nameof(request.CurrentPassword)] =
                            ["Текущий пароль указан неверно."]
                    });
            }
            user.PasswordHash = passwordHasher.Hash(request.NewPassword);
        }

        user.UserName = userName;
        user.Email = $"{userName.ToLowerInvariant()}@local";
        await userRepository.SaveChangesAsync(cancellationToken);
        return ServiceResult<UserProfileResponse>.Success(ToProfile(user));
    }

    public async Task<ServiceResult<UserAvatarResponse>> GetAvatarAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.FindByIdAsync(id, cancellationToken);
        return user?.AvatarContent is null ||
               string.IsNullOrWhiteSpace(user.AvatarContentType)
            ? ServiceResult<UserAvatarResponse>.NotFound()
            : ServiceResult<UserAvatarResponse>.Success(
                new UserAvatarResponse(
                    user.AvatarContent,
                    user.AvatarContentType));
    }

    public async Task<ServiceResult<UserProfileResponse>> UpdateAvatarAsync(
        Guid id,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.FindByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return ServiceResult<UserProfileResponse>.NotFound();
        }
        user.AvatarContent = content;
        user.AvatarContentType = contentType;
        await userRepository.SaveChangesAsync(cancellationToken);
        return ServiceResult<UserProfileResponse>.Success(ToProfile(user));
    }

    public async Task<ServiceResult<UserProfileResponse>> DeleteAvatarAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.FindByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return ServiceResult<UserProfileResponse>.NotFound();
        }
        user.AvatarContent = null;
        user.AvatarContentType = null;
        await userRepository.SaveChangesAsync(cancellationToken);
        return ServiceResult<UserProfileResponse>.Success(ToProfile(user));
    }

    private static UserProfileResponse ToProfile(User user) =>
        new(
            user.Id,
            user.FirstName,
            user.MiddleName,
            user.LastName,
            user.UserName,
            user.Profession.Name,
            user.AvatarContent is not null);

    private static Dictionary<string, string[]> ValidateCreateRequest(
        CreateUserRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        AddRequiredError(
            errors,
            nameof(request.FirstName),
            request.FirstName,
            "Укажите имя.");
        AddRequiredError(
            errors,
            nameof(request.MiddleName),
            request.MiddleName,
            "Укажите отчество.");
        AddRequiredError(
            errors,
            nameof(request.LastName),
            request.LastName,
            "Укажите фамилию.");
        if (request.ProfessionId == Guid.Empty)
        {
            errors[nameof(request.ProfessionId)] = ["Выберите профессию."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateUpdateRequest(
        UpdateUserRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        AddRequiredError(
            errors,
            nameof(request.FirstName),
            request.FirstName,
            "Укажите имя.");
        AddRequiredError(
            errors,
            nameof(request.LastName),
            request.LastName,
            "Укажите фамилию.");
        AddRequiredError(
            errors,
            nameof(request.UserName),
            request.UserName,
            "Укажите логин.");
        if (request.ProfessionId == Guid.Empty)
        {
            errors[nameof(request.ProfessionId)] = ["Выберите профессию."];
        }

        if (request.Role is not ("user" or "administrator"))
        {
            errors[nameof(request.Role)] = ["Укажите допустимую роль."];
        }

        if (request.Permissions is not null &&
            request.Permissions.Any(permission => !Permissions.IsKnown(permission)))
        {
            errors[nameof(request.Permissions)] = ["Указано неизвестное право доступа."];
        }

        if (!string.IsNullOrEmpty(request.Password) && request.Password.Length < 6)
        {
            errors[nameof(request.Password)] =
                ["Пароль должен содержать не менее 6 символов."];
        }

        return errors;
    }

    private static IReadOnlyList<string> GetPermissions(User user) =>
        user.Role.Equals("administrator", StringComparison.OrdinalIgnoreCase)
            ? Permissions.All
            : NormalizePermissions(user.Permissions.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static IReadOnlyList<string> NormalizePermissions(IEnumerable<string>? permissions) =>
        Permissions.Expand(permissions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static void AddRequiredError(
        IDictionary<string, string[]> errors,
        string field,
        string? value,
        string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[field] = [message];
        }
    }

    private static string BuildTemporaryPassword(DateTime createdAt) =>
        $"{createdAt:ddMMyyyy}{createdAt.Minute:00}";

    private static string BuildUserName(
        string firstName,
        string middleName,
        string lastName)
    {
        return string.Join(
            ".",
            new[] { firstName, middleName, lastName }
                .Select(part => Transliterate(part.Trim().Take(2))));
    }

    private static string Transliterate(IEnumerable<char> characters)
    {
        return characters
            .Select(char.ToLowerInvariant)
            .Aggregate(
                new StringBuilder(),
                (result, character) => result.Append(character switch
                {
                    'а' => "a", 'б' => "b", 'в' => "v", 'г' => "g",
                    'д' => "d", 'е' => "e", 'ё' => "yo", 'ж' => "zh",
                    'з' => "z", 'и' => "i", 'й' => "y", 'к' => "k",
                    'л' => "l", 'м' => "m", 'н' => "n", 'о' => "o",
                    'п' => "p", 'р' => "r", 'с' => "s", 'т' => "t",
                    'у' => "u", 'ф' => "f", 'х' => "h", 'ц' => "ts",
                    'ч' => "ch", 'ш' => "sh", 'щ' => "sch", 'ъ' => "",
                    'ы' => "y", 'ь' => "", 'э' => "e", 'ю' => "yu",
                    'я' => "ya",
                    >= 'a' and <= 'z' or >= '0' and <= '9' =>
                        character.ToString(),
                    _ => ""
                }))
            .ToString();
    }
}
