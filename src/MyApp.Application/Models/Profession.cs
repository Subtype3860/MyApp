namespace MyApp.Application.Models;

/// <summary>Профессия сотрудника и связанные с ней учётные записи.</summary>
public class Profession
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? PermissionOverride { get; set; }
    public string HiddenPageBlocks { get; set; } = "";
    public int AccessRevision { get; set; }
    public ICollection<User> Users { get; set; } = [];
}
