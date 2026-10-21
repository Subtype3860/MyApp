using MyApp.Application.Security;

namespace MyApp.Infrastructure.Tests;

/// <summary>Тесты прав доступа к разделам навигации.</summary>
public sealed class NavigationPermissionsTests
{
    /// <summary>Проверяет преобразование прежнего разрешения техники в актуальный ключ навигации.</summary>
    [Xunit.Fact]
    public void Legacy_vehicle_permission_resolves_to_current_navigation_key()
    {
        var expanded = Permissions.Expand(["vehicles.view"]);
        Xunit.Assert.Contains(Permissions.VehiclesView, expanded);
        Xunit.Assert.DoesNotContain("vehicles.view", expanded);
    }

    /// <summary>Проверяет включение родительского меню при выборе дочернего разрешения.</summary>
    [Xunit.Fact]
    public void Selecting_a_child_makes_its_parent_menu_visible()
    {
        var expanded = Permissions.Expand(["vehicles.hours"]);
        Xunit.Assert.Contains("vehicles.hours", expanded);
        Xunit.Assert.Contains(Permissions.VehiclesView, expanded);
        Xunit.Assert.DoesNotContain("vehicles.works", expanded);
    }

    /// <summary>Проверяет отклонение неизвестных разрешений.</summary>
    [Xunit.Fact]
    public void Unknown_permissions_are_rejected()
    {
        var expanded = Permissions.Expand(["vehicles.hours", "unknown.permission"]);
        Xunit.Assert.DoesNotContain("unknown.permission", expanded);
        Xunit.Assert.Equal(2, expanded.Count);
        Xunit.Assert.False(Permissions.IsKnown("unknown.permission"));
    }

    /// <summary>Проверяет сопоставление разрешений без учёта регистра.</summary>
    [Xunit.Fact]
    public void Permission_matching_is_case_insensitive()
    {
        var expanded = Permissions.Expand(["VEHICLES.HOURS", "Vehicles.View"]);
        Xunit.Assert.Contains(expanded, permission =>
            string.Equals(permission, Permissions.VehiclesView,
                StringComparison.OrdinalIgnoreCase));
        Xunit.Assert.Equal(2, expanded.Count);
        Xunit.Assert.True(Permissions.IsKnown("VEHICLES.HOURS"));
    }

    /// <summary>Проверяет отсутствие ограниченных прав навигации при пустом выборе.</summary>
    [Xunit.Fact]
    public void Empty_and_null_selections_have_no_restricted_navigation_permissions()
    {
        Xunit.Assert.Empty(Permissions.Expand(null));
        Xunit.Assert.Empty(Permissions.Expand([]));
    }
    /// <summary>Проверяет, что доступ к вспомогательным работам не открывает раздел техники.</summary>
    [Xunit.Fact]
    public void Auxiliary_work_menu_does_not_grant_vehicle_access()
    {
        var expanded = Permissions.Expand([Permissions.AuxiliaryWorksView]);
        Xunit.Assert.Contains(Permissions.AuxiliaryWorksView, expanded);
        Xunit.Assert.DoesNotContain(Permissions.VehiclesView, expanded);
        Xunit.Assert.Single(expanded);
    }
}
