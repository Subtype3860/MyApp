namespace MyApp.Application.Security;

public static class MaintenanceLibraryAccess
{
    public static bool Allows(string? profession) => profession?.Trim().ToLowerInvariant() is
        "механик" or "старший механик" or "главный механик" or "инженер ппр";
}
