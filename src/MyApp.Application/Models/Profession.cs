namespace MyApp.Application.Models;

/// <summary>Профессия сотрудника и связанные с ней учётные записи.</summary>
public class Profession
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public ICollection<User> Users { get; set; } = [];
}
