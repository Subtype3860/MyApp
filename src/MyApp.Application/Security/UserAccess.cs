using MyApp.Domain.Entities;

namespace MyApp.Application.Security;

public static class UserAccess
{
    public const string SessionStampClaim = "session_stamp";
    public const string HoursPermission = "vehicles.hours";

    public static bool IsMechanic(User user) =>
        user.Profession?.Name.Trim() is string profession &&
        (profession.Equals("Механик", StringComparison.OrdinalIgnoreCase) ||
         profession.Equals("Старший механик", StringComparison.OrdinalIgnoreCase));

    public static bool CanManageHours(User user) =>
        user.Role.Equals("administrator", StringComparison.OrdinalIgnoreCase) ||
        IsMechanic(user) ||
        user.Role.Equals("user", StringComparison.OrdinalIgnoreCase) &&
        Permissions.Expand(user.Permissions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Contains(HoursPermission, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> EffectivePermissions(User user) =>
        user.Role.Equals("administrator", StringComparison.OrdinalIgnoreCase)
            ? Permissions.All
            : Permissions.Expand(user.Permissions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Concat(IsMechanic(user) ? [HoursPermission] : Array.Empty<string>()));
}
