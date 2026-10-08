using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyApp.Infrastructure.Db.Entities;

namespace MyApp.Infrastructure.Db.Configurations;

public sealed class MaintenanceEquipmentConfiguration : IEntityTypeConfiguration<MaintenanceEquipmentEntity>
{
    public void Configure(EntityTypeBuilder<MaintenanceEquipmentEntity> entity)
    {
        entity.ToTable("maintenance_equipment");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        entity.Property(x => x.SortOrder).HasColumnName("sort_order").HasDefaultValue(0);
        entity.HasMany(x => x.Intervals)
            .WithOne(x => x.Equipment)
            .HasForeignKey(x => x.EquipmentId)
            .OnDelete(DeleteBehavior.Cascade);
        // PostgreSQL owns ux_maintenance_equipment_name = LOWER(BTRIM(name)).
    }
}
