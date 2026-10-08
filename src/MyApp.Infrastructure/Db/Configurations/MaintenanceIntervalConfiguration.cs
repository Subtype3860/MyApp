using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyApp.Infrastructure.Db.Entities;

namespace MyApp.Infrastructure.Db.Configurations;

public sealed class MaintenanceIntervalConfiguration : IEntityTypeConfiguration<MaintenanceIntervalEntity>
{
    public void Configure(EntityTypeBuilder<MaintenanceIntervalEntity> entity)
    {
        entity.ToTable("maintenance_intervals");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(x => x.EquipmentId).HasColumnName("equipment_id");
        entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        entity.Property(x => x.SortOrder).HasColumnName("sort_order").HasDefaultValue(0);
        entity.HasMany(x => x.Items)
            .WithOne(x => x.Interval)
            .HasForeignKey(x => x.IntervalId)
            .OnDelete(DeleteBehavior.Cascade);
        // PostgreSQL owns ux_maintenance_intervals_name.
    }
}
