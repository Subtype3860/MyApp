using Microsoft.EntityFrameworkCore;
using MyApp.Infrastructure.Db.Entities;

namespace MyApp.Infrastructure.Db;
public partial class AppDbContext
{
    public DbSet<MaintenanceCardTemplate> MaintenanceCardTemplates => Set<MaintenanceCardTemplate>();
    public DbSet<MaintenanceCard> MaintenanceCards => Set<MaintenanceCard>();
    public DbSet<MaintenanceCardPhoto> MaintenanceCardPhotos => Set<MaintenanceCardPhoto>();
    private void ConfigureMaintenanceCards(ModelBuilder model)
    {
        model.Entity<MaintenanceCardTemplate>(e => {
            e.ToTable("maintenance_card_templates"); e.HasKey(x => x.Id);
            e.Property(x => x.Snapshot).HasColumnType("jsonb");
        });
        model.Entity<MaintenanceCard>(e => {
            e.ToTable("maintenance_cards"); e.HasKey(x => x.Id);
            e.Property(x => x.Template).HasColumnType("jsonb"); e.Property(x => x.Data).HasColumnType("jsonb");
            e.Property(x => x.Audit).HasColumnType("jsonb"); e.Property(x => x.Defects).HasColumnType("jsonb");
            e.Property(x => x.Readings).HasColumnType("jsonb");
            e.HasQueryFilter(x => !RestrictFleet || AccessibleVehicleIds.Contains(x.VehicleId));
        });
        model.Entity<MaintenanceCardPhoto>(e => { e.ToTable("maintenance_card_photos"); e.HasKey(x => x.Id); });
    }
}
