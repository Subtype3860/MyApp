using MyApp.Application.Models;

namespace MyApp.Application.Security;

/// <summary>Общие правила определения прав пользователя по роли и профессии.</summary>
public static class UserAccess
{
    public const string SessionStampClaim = "session_stamp";
    public const string HoursPermission = "vehicles.hours";

    /// <summary>Определяет принадлежность пользователя к профессии механика.</summary>
    public static bool IsMechanic(User user) =>
        user.Profession?.Name.Trim() is string profession &&
        (profession.Equals("Механик", StringComparison.OrdinalIgnoreCase) ||
         profession.Equals("Старший механик", StringComparison.OrdinalIgnoreCase));

    /// <summary>Проверяет право пользователя изменять показания моточасов.</summary>
    public static bool CanManageHours(User user) =>
        user.Role.Equals("administrator", StringComparison.OrdinalIgnoreCase) ||
        user.Role.Equals("user", StringComparison.OrdinalIgnoreCase) &&
        EffectivePermissions(user)
            .Contains(HoursPermission, StringComparer.OrdinalIgnoreCase);

    /// <summary>Возвращает фактический набор разрешений с учётом правил доступа пользователя.</summary>
    public static IReadOnlyList<string> EffectivePermissions(User user) =>
        user.Role.Equals("administrator", StringComparison.OrdinalIgnoreCase)
            ? Permissions.All
            : Permissions.Expand((user.Profession?.PermissionOverride ?? user.Permissions)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Concat(user.Profession?.PermissionOverride is null && IsMechanic(user) ? [HoursPermission] : Array.Empty<string>()))
                .Concat((user.Profession?.HiddenPageBlocks ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(key => PageBlocks.HiddenPrefix + key)).ToArray();
}
