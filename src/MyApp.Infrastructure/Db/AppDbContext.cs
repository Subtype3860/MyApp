using Microsoft.EntityFrameworkCore;
using MyApp.Domain.Entities;
using MyApp.Infrastructure.Db.Configurations;
using MyApp.Infrastructure.Db.Entities;

namespace MyApp.Infrastructure.Db;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Profession> Professions => Set<Profession>();
    public DbSet<VehiclePurchaseEntity> VehiclePurchases => Set<VehiclePurchaseEntity>();
    public DbSet<MaterialGroupEntity> MaterialGroups => Set<MaterialGroupEntity>();
    public DbSet<MaterialGroupItemEntity> MaterialGroupItems => Set<MaterialGroupItemEntity>();
    public DbSet<MaintenanceEquipmentEntity> MaintenanceEquipment => Set<MaintenanceEquipmentEntity>();
    public DbSet<MaintenanceIntervalEntity> MaintenanceIntervals => Set<MaintenanceIntervalEntity>();
    public DbSet<MaintenanceItemEntity> MaintenanceItems => Set<MaintenanceItemEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new ProfessionConfiguration());
        modelBuilder.ApplyConfiguration(new VehiclePurchaseConfiguration());
        modelBuilder.ApplyConfiguration(new MaterialGroupConfiguration());
        modelBuilder.ApplyConfiguration(new MaterialGroupItemConfiguration());
        modelBuilder.ApplyConfiguration(new MaintenanceEquipmentConfiguration());
        modelBuilder.ApplyConfiguration(new MaintenanceIntervalConfiguration());
        modelBuilder.ApplyConfiguration(new MaintenanceItemConfiguration());
    }
}
