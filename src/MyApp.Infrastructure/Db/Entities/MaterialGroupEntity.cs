namespace MyApp.Infrastructure.Db.Entities;

public sealed class MaterialGroupEntity
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public ICollection<MaterialGroupItemEntity> Items { get; set; } = [];
}
