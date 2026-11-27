using Microsoft.EntityFrameworkCore;
using MyApp.Infrastructure.Db.Entities;

namespace MyApp.Infrastructure.Db;

public partial class AppDbContext
{
    // Background jobs and schema initialization have no HTTP identity. HTTP middleware always sets a scope.
    public bool RestrictFleet { get; set; }
    public Guid[] AccessibleVehicleIds { get; set; } = [];
    public DbSet<FleetRegion> FleetRegions => Set<FleetRegion>();
    public DbSet<FleetRegionGrant> FleetRegionGrants => Set<FleetRegionGrant>();
    public DbSet<VehicleRegionAssignment> VehicleRegionAssignments => Set<VehicleRegionAssignment>();

    public async Task SetFleetScopeAsync(Guid? actor, CancellationToken ct)
    {
        RestrictFleet = true;
        AccessibleVehicleIds = [];
        if (actor is null) return;
        var role = await Users.AsNoTracking().Where(u => u.Id == actor).Select(u => u.Role).SingleOrDefaultAsync(ct);
        if (string.Equals(role, "administrator", StringComparison.OrdinalIgnoreCase)) { RestrictFleet = false; return; }
        AccessibleVehicleIds = await (from assignment in VehicleRegionAssignments.IgnoreQueryFilters()
            join grant in FleetRegionGrants on assignment.RegionId equals grant.RegionId
            where grant.UserId == actor && assignment.EndedAt == null
            select assignment.VehicleId).Distinct().ToArrayAsync(ct);
    }

    private void ConfigureFleetRegions(ModelBuilder model)
    {
        model.Entity<FleetRegion>(e => {
            e.ToTable("fleet_regions"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(200);
            e.Property(x => x.IsArchived).HasColumnName("is_archived");
        });
        model.Entity<FleetRegionGrant>(e => {
            e.ToTable("fleet_region_grants"); e.HasKey(x => new { x.UserId, x.RegionId });
            e.Property(x => x.UserId).HasColumnName("user_id"); e.Property(x => x.RegionId).HasColumnName("region_id");
        });
        model.Entity<VehicleRegionAssignment>(e => {
            e.ToTable("vehicle_region_assignments"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id"); e.Property(x => x.VehicleId).HasColumnName("vehicle_id");
            e.Property(x => x.RegionId).HasColumnName("region_id"); e.Property(x => x.StartedAt).HasColumnName("started_at");
            e.Property(x => x.EndedAt).HasColumnName("ended_at"); e.Property(x => x.CreatedBy).HasColumnName("created_by");
            e.Property(x => x.RecordedAt).HasColumnName("recorded_at"); e.Property(x => x.Note).HasColumnName("note").HasMaxLength(2000);
            e.HasIndex(x => x.VehicleId).IsUnique().HasFilter("ended_at IS NULL");
            e.HasQueryFilter(x => !RestrictFleet || AccessibleVehicleIds.Contains(x.VehicleId));
        });
        model.Entity<NumberCarRecord>().HasQueryFilter(x => !RestrictFleet || AccessibleVehicleIds.Contains(x.Id));
        model.Entity<VehicleDefectRecord>().HasQueryFilter(x => !RestrictFleet || AccessibleVehicleIds.Contains(x.VehicleId));
        model.Entity<VehicleHourReadingRecord>().HasQueryFilter(x => !RestrictFleet || AccessibleVehicleIds.Contains(x.VehicleId));
        model.Entity<VehiclePurchaseRequestRecord>().HasQueryFilter(x => !RestrictFleet || AccessibleVehicleIds.Contains(x.VehicleId));
        model.Entity<VehicleWorkRecord>().HasQueryFilter(x => !RestrictFleet || x.VehicleId == null || AccessibleVehicleIds.Contains(x.VehicleId.Value));
        model.Entity<VehicleMaintenancePlan>().HasQueryFilter(x => !RestrictFleet || AccessibleVehicleIds.Contains(x.VehicleId));
        model.Entity<VehicleMaintenanceEvent>().HasQueryFilter(x => !RestrictFleet || AccessibleVehicleIds.Contains(x.VehicleId));
        model.Entity<VehicleShiftOutcome>().HasQueryFilter(x => !RestrictFleet || AccessibleVehicleIds.Contains(x.VehicleId));
        model.Entity<VehicleEngineSettings>().HasQueryFilter(x => !RestrictFleet || AccessibleVehicleIds.Contains(x.VehicleId));
        model.Entity<EcuReplacementRecord>().HasQueryFilter(x => !RestrictFleet || AccessibleVehicleIds.Contains(x.VehicleId));
        model.Entity<GtoVehicleSettings>().HasQueryFilter(x => !RestrictFleet || AccessibleVehicleIds.Contains(x.VehicleId));
        model.Entity<GtoDefectSettings>().HasQueryFilter(x => !RestrictFleet || VehicleDefects.Any(d => d.Id == x.DefectId));
        model.Entity<VehiclePartsRequestRecord>().HasQueryFilter(x => !RestrictFleet || VehicleDefects.Any(d => d.Id == x.DefectId));
        model.Entity<VehicleDefectPhotoRecord>().HasQueryFilter(x => !RestrictFleet || VehicleDefects.Any(d => d.Id == x.DefectId));
        model.Entity<VehicleDefectVideoRecord>().HasQueryFilter(x => !RestrictFleet || VehicleDefects.Any(d => d.Id == x.DefectId));
        model.Entity<VehicleWorkPhotoRecord>().HasQueryFilter(x => !RestrictFleet || VehicleWorks.Any(w => w.Id == x.WorkId));
        model.Entity<VehicleWorkVideoRecord>().HasQueryFilter(x => !RestrictFleet || VehicleWorks.Any(w => w.Id == x.WorkId));
        model.Entity<VehicleWorkParticipantRecord>().HasQueryFilter(x => !RestrictFleet || VehicleWorks.Any(w => w.Id == x.WorkId));
    }
}
