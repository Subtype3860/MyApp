using MyApp.Application.Security;
using Xunit;

namespace MyApp.Infrastructure.Tests;
public sealed class MaintenanceLibraryAccessTests
{
    [Theory]
    [InlineData("Механик", true)]
    [InlineData("Старший механик", true)]
    [InlineData("Главный механик", true)]
    [InlineData(" Инженер ППР ", true)]
    [InlineData("Администратор", false)]
    [InlineData("Водитель", false)]
    [InlineData("Инженер", false)]
    [InlineData(null, false)]
    public void Library_depends_on_exact_profession(string? profession, bool expected) =>
        Assert.Equal(expected, MaintenanceLibraryAccess.Allows(profession));
}
