namespace MyApp.Infrastructure.Db.Entities;

public sealed class MaterialGroupItemEntity
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public required string SourceTable { get; set; }
    public required string MaterialName { get; set; }
    public MaterialGroupEntity Group { get; set; } = null!;
}
