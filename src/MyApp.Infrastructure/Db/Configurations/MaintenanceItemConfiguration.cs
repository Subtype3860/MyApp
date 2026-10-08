using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyApp.Infrastructure.Db.Entities;

namespace MyApp.Infrastructure.Db.Configurations;

public sealed class MaintenanceItemConfiguration : IEntityTypeConfiguration<MaintenanceItemEntity>
{
    public void Configure(EntityTypeBuilder<MaintenanceItemEntity> entity)
    {
        entity.ToTable("maintenance_interval_items");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(x => x.IntervalId).HasColumnName("interval_id");
        entity.Property(x => x.MaterialName).HasColumnName("material_name")
            .HasColumnType("text").IsRequired();
        entity.Property(x => x.Quantity).HasColumnName("quantity")
            .HasColumnType("numeric");
        entity.Property(x => x.SortOrder).HasColumnName("sort_order").HasDefaultValue(0);
        // PostgreSQL owns the positive quantity check and normalized unique index.
    }
}
