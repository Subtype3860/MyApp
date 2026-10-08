using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyApp.Infrastructure.Db.Entities;

namespace MyApp.Infrastructure.Db.Configurations;

public sealed class MaterialGroupConfiguration : IEntityTypeConfiguration<MaterialGroupEntity>
{
    public void Configure(EntityTypeBuilder<MaterialGroupEntity> entity)
    {
        entity.ToTable("material_groups");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(x => x.Name).HasColumnName("name").IsRequired().HasMaxLength(100);
        entity.HasMany(x => x.Items)
            .WithOne(x => x.Group)
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.Cascade);
        // The existing unique name index is LOWER(BTRIM(name)).
        // This expression index is maintained by DatabaseInitializer.
    }
}
