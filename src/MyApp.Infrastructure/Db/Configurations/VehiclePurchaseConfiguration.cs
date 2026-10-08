using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyApp.Infrastructure.Db.Entities;

namespace MyApp.Infrastructure.Db.Configurations;

public sealed class VehiclePurchaseConfiguration : IEntityTypeConfiguration<VehiclePurchaseEntity>
{
    public void Configure(EntityTypeBuilder<VehiclePurchaseEntity> entity)
    {
        entity.ToTable("vehicle_purchase_requests");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(x => x.VehicleId).HasColumnName("vehicle_id");
        entity.Property(x => x.RequestDate).HasColumnName("request_date");
        entity.Property(x => x.RequestNumber).HasColumnName("request_number").IsRequired().HasMaxLength(100);
        entity.Property(x => x.ItemName).HasColumnName("item_name").IsRequired().HasMaxLength(500);
        entity.Property(x => x.Quantity).HasColumnName("quantity").HasColumnType("numeric").IsRequired();
        entity.Property(x => x.Status).HasColumnName("status").IsRequired().HasMaxLength(100);
        entity.Property(x => x.Note).HasColumnName("note").HasColumnType("text").IsRequired();
        entity.Property(x => x.CreatedBy).HasColumnName("created_by");
        entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
    }
}
