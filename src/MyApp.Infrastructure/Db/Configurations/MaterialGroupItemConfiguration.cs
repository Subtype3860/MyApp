using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyApp.Infrastructure.Db.Entities;

namespace MyApp.Infrastructure.Db.Configurations;

public sealed class MaterialGroupItemConfiguration : IEntityTypeConfiguration<MaterialGroupItemEntity>
{
    public void Configure(EntityTypeBuilder<MaterialGroupItemEntity> entity)
    {
        entity.ToTable("material_group_items");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(x => x.GroupId).HasColumnName("group_id");
        entity.Property(x => x.SourceTable).HasColumnName("source_table")
            .IsRequired().HasMaxLength(20);
        entity.Property(x => x.MaterialName).HasColumnName("material_name")
            .HasColumnType("text").IsRequired();
        // Source constraints and expression index are enforced by PostgreSQL.
    }
}
