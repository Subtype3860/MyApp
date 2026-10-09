using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyApp.Domain.Entities;
using MyApp.Infrastructure.Db.Entities;

namespace MyApp.Infrastructure.Db {
    public class AppDbContext : DbContext {
        public DbSet<User> Users => Set<User>();
        public DbSet<Profession> Professions => Set<Profession>();
        public DbSet<GroupCarRecord> VehicleGroups => Set<GroupCarRecord>();
        public DbSet<TypeCarRecord> VehicleTypes => Set<TypeCarRecord>();
        public DbSet<ModelCarRecord> VehicleModels => Set<ModelCarRecord>();
        public DbSet<NumberCarRecord> Vehicles => Set<NumberCarRecord>();
        public DbSet<VehiclePurchaseRequestRecord> VehiclePurchaseRequests =>
            Set<VehiclePurchaseRequestRecord>();
        public DbSet<VehicleDefectRecord> VehicleDefects =>
            Set<VehicleDefectRecord>();
        public DbSet<VehicleHourReadingRecord> VehicleHourReadings =>
            Set<VehicleHourReadingRecord>();
        public DbSet<VehicleWorkRecord> VehicleWorks => Set<VehicleWorkRecord>();
        public DbSet<VehicleWorkPhotoRecord> VehicleWorkPhotos =>
            Set<VehicleWorkPhotoRecord>();
        public DbSet<VehicleDefectPhotoRecord> VehicleDefectPhotos =>
            Set<VehicleDefectPhotoRecord>();
        public DbSet<VehicleWorkVideoRecord> VehicleWorkVideos =>
            Set<VehicleWorkVideoRecord>();
        public DbSet<VehicleDefectVideoRecord> VehicleDefectVideos =>
            Set<VehicleDefectVideoRecord>();
        public DbSet<VehiclePartsRequestRecord> VehiclePartsRequests =>
            Set<VehiclePartsRequestRecord>();
        public DbSet<ComponentRequirementRecord> ComponentRequirements =>
            Set<ComponentRequirementRecord>();
        public DbSet<ComponentRequirementItemRecord> ComponentRequirementItems =>
            Set<ComponentRequirementItemRecord>();
        public DbSet<EmployeeSignatureRecord> EmployeeSignatures =>
            Set<EmployeeSignatureRecord>();
        public DbSet<EmployeeDirectoryRecord> Employees =>
            Set<EmployeeDirectoryRecord>();
        public DbSet<MaintenanceEquipmentRecord> MaintenanceEquipment =>
            Set<MaintenanceEquipmentRecord>();
        public DbSet<MaintenanceIntervalRecord> MaintenanceIntervals =>
            Set<MaintenanceIntervalRecord>();
        public DbSet<MaintenanceIntervalItemRecord> MaintenanceIntervalItems =>
            Set<MaintenanceIntervalItemRecord>();
        public DbSet<MaterialGroupRecord> MaterialGroups =>
            Set<MaterialGroupRecord>();
        public DbSet<MaterialGroupItemRecord> MaterialGroupItems =>
            Set<MaterialGroupItemRecord>();
        public DbSet<MediaStorageSettingsRecord> MediaStorageSettings =>
            Set<MediaStorageSettingsRecord>();
        public DbSet<FullStockRecord> FullStocks => Set<FullStockRecord>();
        public DbSet<MechanicalStockRecord> MechanicalStocks =>
            Set<MechanicalStockRecord>();
        public DbSet<FullStockLegacyRecord> FullStockLegacy =>
            Set<FullStockLegacyRecord>();
        public DbSet<MechanicalStockLegacyRecord> MechanicalStockLegacy =>
            Set<MechanicalStockLegacyRecord>();
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) {}
        protected override void OnModelCreating(ModelBuilder modelBuilder) {
            modelBuilder.Entity<User>(entity => {
                entity.ToTable("app_users");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Id).HasColumnName("id");
                entity.Property(x => x.FirstName).HasColumnName("first_name").IsRequired().HasMaxLength(100);
                entity.Property(x => x.MiddleName).HasColumnName("middle_name").IsRequired().HasMaxLength(100);
                entity.Property(x => x.LastName).HasColumnName("last_name").IsRequired().HasMaxLength(100);
                entity.Property(x => x.UserName).HasColumnName("user_name").IsRequired().HasMaxLength(50);
                entity.Property(x => x.Email).HasColumnName("email").IsRequired().HasMaxLength(100);
                entity.Property(x => x.PasswordHash).HasColumnName("password_hash").IsRequired();
                entity.Property(x => x.PositionId).HasColumnName("position").IsRequired();
                entity.Property(x => x.Role).HasColumnName("role").IsRequired().HasMaxLength(30);
                entity.Property(x => x.Permissions).HasColumnName("permissions").IsRequired();
                entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
                entity.Property(x => x.AvatarContent).HasColumnName("avatar_content");
                entity.Property(x => x.AvatarContentType)
                    .HasColumnName("avatar_content_type")
                    .HasMaxLength(100);
                entity.HasIndex(x => x.Email).IsUnique();
                entity.HasIndex(x => x.UserName).IsUnique();
                entity.HasOne(x => x.Profession)
                    .WithMany(x => x.Users)
                    .HasForeignKey(x => x.PositionId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Profession>(entity => {
                entity.ToTable("professions");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Id)
                    .HasColumnName("id")
                    .HasDefaultValueSql("gen_random_uuid()");
                entity.Property(x => x.Name)
                    .HasColumnName("profession")
                    .IsRequired()
                    .HasMaxLength(40);
            });

            modelBuilder.Entity<VehiclePartsRequestRecord>(entity => {
                entity.ToTable("vehicle_parts_requests");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Id).HasColumnName("id");
                entity.Property(x => x.DefectId).HasColumnName("defect_id");
                entity.Property(x => x.RequestDate)
                    .HasColumnName("request_date")
                    .HasColumnType("date");
                entity.Property(x => x.RequestNumber)
                    .HasColumnName("request_number")
                    .HasMaxLength(100);
                entity.Property(x => x.Description)
                    .HasColumnName("description");
                entity.Property(x => x.RequiredParts)
                    .HasColumnName("required_parts");
                entity.Property(x => x.CreatedBy).HasColumnName("created_by");
                entity.Property(x => x.CreatedAt)
                    .HasColumnName("created_at")
                    .HasDefaultValueSql("NOW()");
            });

            modelBuilder.Entity<GroupCarRecord>(entity => {
                entity.ToTable("group_car");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Id).HasColumnName("id");
                entity.Property(x => x.FullName).HasColumnName("full_name");
            });
            modelBuilder.Entity<TypeCarRecord>(entity => {
                entity.ToTable("type_car");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Id).HasColumnName("id");
                entity.Property(x => x.FullName).HasColumnName("full_name");
                entity.Property(x => x.CarGroupId).HasColumnName("car_group_id");
            });
            modelBuilder.Entity<ModelCarRecord>(entity => {
                entity.ToTable("model_car");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Id).HasColumnName("id");
                entity.Property(x => x.FullName).HasColumnName("full_name");
                entity.Property(x => x.CarTypeId).HasColumnName("car_type_id");
            });
            modelBuilder.Entity<NumberCarRecord>(entity => {
                entity.ToTable("number_car");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Id).HasColumnName("id");
                entity.Property(x => x.CarModeId).HasColumnName("car_mode_id");
                entity.Property(x => x.GarageNumber).HasColumnName("gar_number");
                entity.Property(x => x.StateNumber).HasColumnName("gos_number");
                entity.Property(x => x.Vin).HasColumnName("vim");
            });
            ConfigureCreatedAt(modelBuilder.Entity<VehiclePurchaseRequestRecord>(),
                "vehicle_purchase_requests");
            modelBuilder.Entity<VehiclePurchaseRequestRecord>(entity => {
                entity.Property(x => x.VehicleId).HasColumnName("vehicle_id");
                entity.Property(x => x.RequestDate).HasColumnName("request_date").HasColumnType("date");
                entity.Property(x => x.RequestNumber).HasColumnName("request_number");
                entity.Property(x => x.ItemName).HasColumnName("item_name");
                entity.Property(x => x.Quantity).HasColumnName("quantity");
                entity.Property(x => x.Status).HasColumnName("status");
                entity.Property(x => x.Note).HasColumnName("note");
                entity.Property(x => x.CreatedBy).HasColumnName("created_by");
            });
            ConfigureCreatedAt(modelBuilder.Entity<VehicleDefectRecord>(), "vehicle_defects");
            modelBuilder.Entity<VehicleDefectRecord>(entity => {
                entity.Property(x => x.VehicleId).HasColumnName("vehicle_id");
                entity.Property(x => x.NodeName).HasColumnName("node_name");
                entity.Property(x => x.FailureReason).HasColumnName("failure_reason");
                entity.Property(x => x.ErrorCode).HasColumnName("error_code");
                entity.Property(x => x.Symptoms).HasColumnName("symptoms");
                entity.Property(x => x.DowntimeStartedAt).HasColumnName("downtime_started_at");
                entity.Property(x => x.AssignedTo).HasColumnName("assigned_to");
                entity.Property(x => x.RepairStartedAt).HasColumnName("repair_started_at");
                entity.Property(x => x.CreatedBy).HasColumnName("created_by");
            });
            ConfigureCreatedAt(modelBuilder.Entity<VehicleHourReadingRecord>(),
                "vehicle_hour_readings");
            modelBuilder.Entity<VehicleHourReadingRecord>(entity => {
                entity.Property(x => x.VehicleId).HasColumnName("vehicle_id");
                entity.Property(x => x.ReadingDate).HasColumnName("reading_date").HasColumnType("date");
                entity.Property(x => x.EngineHours).HasColumnName("engine_hours");
                entity.Property(x => x.Note).HasColumnName("note");
                entity.Property(x => x.CreatedBy).HasColumnName("created_by");
            });
            ConfigureCreatedAt(modelBuilder.Entity<VehicleWorkRecord>(), "vehicle_works");
            modelBuilder.Entity<VehicleWorkRecord>(entity => {
                entity.Property(x => x.VehicleId).HasColumnName("vehicle_id");
                entity.Property(x => x.WorkDate).HasColumnName("work_date").HasColumnType("date");
                entity.Property(x => x.Description).HasColumnName("description");
                entity.Property(x => x.EngineHours).HasColumnName("engine_hours");
                entity.Property(x => x.Performer).HasColumnName("performer");
                entity.Property(x => x.Note).HasColumnName("note");
                entity.Property(x => x.DefectId).HasColumnName("defect_id");
                entity.Property(x => x.FailureCause).HasColumnName("failure_cause");
                entity.Property(x => x.RepairStatus).HasColumnName("repair_status");
                entity.Property(x => x.RequiredParts).HasColumnName("required_parts");
                entity.Property(x => x.PerformedBy).HasColumnName("performed_by");
                entity.Property(x => x.CompletedAt).HasColumnName("completed_at");
                entity.Property(x => x.CreatedBy).HasColumnName("created_by");
            });
            ConfigureMedia<VehicleWorkPhotoRecord>(modelBuilder, "vehicle_work_photos",
                entity => {
                    entity.Property(x => x.WorkId).HasColumnName("work_id");
                    entity.Property(x => x.StoragePath).HasColumnName("storage_path");
                });
            ConfigureMedia<VehicleDefectPhotoRecord>(modelBuilder, "vehicle_defect_photos",
                entity => {
                    entity.Property(x => x.DefectId).HasColumnName("defect_id");
                    entity.Property(x => x.StoragePath).HasColumnName("storage_path");
                });
            ConfigureMedia<VehicleWorkVideoRecord>(modelBuilder, "vehicle_work_videos",
                entity => {
                    entity.Property(x => x.WorkId).HasColumnName("work_id");
                    entity.Property(x => x.StoragePath).HasColumnName("storage_path").IsRequired();
                });
            ConfigureMedia<VehicleDefectVideoRecord>(modelBuilder, "vehicle_defect_videos",
                entity => {
                    entity.Property(x => x.DefectId).HasColumnName("defect_id");
                    entity.Property(x => x.StoragePath).HasColumnName("storage_path").IsRequired();
                });

            ConfigureRequirementRecords(modelBuilder);
            ConfigureEmployeeRecords(modelBuilder);
            ConfigureMaintenanceRecords(modelBuilder);
            ConfigureMaterialGroupRecords(modelBuilder);
            ConfigureMediaStorageSettings(modelBuilder);
            ConfigureStockViews(modelBuilder);
        }

        private static void ConfigureRequirementRecords(ModelBuilder modelBuilder) {
            modelBuilder.Entity<ComponentRequirementRecord>(entity => {
                entity.ToTable("component_requirements");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Id).HasColumnName("id");
                entity.Property(x => x.CreatedAt)
                    .HasColumnName("created_at")
                    .HasDefaultValueSql("NOW()");
                entity.Property(x => x.CreatedBy).HasColumnName("created_by");
                entity.Property(x => x.AuthorName).HasColumnName("author_name");
                entity.Property(x => x.IssuerName).HasColumnName("issuer_name");
                entity.Property(x => x.VehicleNumber).HasColumnName("vehicle_number");
                entity.Property(x => x.SourceTable).HasColumnName("source_table");
                entity.Property(x => x.FormData)
                    .HasColumnName("form_data")
                    .HasColumnType("jsonb");
            });
            modelBuilder.Entity<ComponentRequirementItemRecord>(entity => {
                entity.ToTable("component_requirement_items");
                entity.HasKey(x => new { x.RequirementId, x.Position });
                entity.Property(x => x.RequirementId).HasColumnName("requirement_id");
                entity.Property(x => x.Position).HasColumnName("position");
                entity.Property(x => x.Name).HasColumnName("name");
                entity.Property(x => x.Unit).HasColumnName("unit");
                entity.Property(x => x.Quantity).HasColumnName("quantity");
            });
        }

        private static void ConfigureEmployeeRecords(ModelBuilder modelBuilder) {
            modelBuilder.Entity<EmployeeSignatureRecord>(entity => {
                entity.ToTable("employee_signatures");
                entity.HasKey(x => new {
                    x.LastName,
                    x.FirstName,
                    x.Patronymic
                });
                entity.Property(x => x.LastName).HasColumnName("last_name");
                entity.Property(x => x.FirstName).HasColumnName("first_name");
                entity.Property(x => x.Patronymic).HasColumnName("patronymic");
                entity.Property(x => x.Content).HasColumnName("content");
                entity.Property(x => x.ContentType).HasColumnName("content_type");
                entity.Property(x => x.UpdatedAt)
                    .HasColumnName("updated_at")
                    .HasDefaultValueSql("NOW()");
            });
            modelBuilder.Entity<EmployeeDirectoryRecord>(entity => {
                entity.HasNoKey();
                entity.ToView("employees");
                entity.Property(x => x.FullName).HasColumnName("FullName");
                entity.Property(x => x.Profession).HasColumnName("Profession");
            });
        }

        private static void ConfigureMaintenanceRecords(ModelBuilder modelBuilder) {
            modelBuilder.Entity<MaintenanceEquipmentRecord>(entity => {
                entity.ToTable("maintenance_equipment");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Id).HasColumnName("id");
                entity.Property(x => x.Name).HasColumnName("name");
                entity.Property(x => x.SortOrder).HasColumnName("sort_order");
            });
            modelBuilder.Entity<MaintenanceIntervalRecord>(entity => {
                entity.ToTable("maintenance_intervals");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Id).HasColumnName("id");
                entity.Property(x => x.EquipmentId).HasColumnName("equipment_id");
                entity.Property(x => x.Name).HasColumnName("name");
                entity.Property(x => x.SortOrder).HasColumnName("sort_order");
            });
            modelBuilder.Entity<MaintenanceIntervalItemRecord>(entity => {
                entity.ToTable("maintenance_interval_items");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Id).HasColumnName("id");
                entity.Property(x => x.IntervalId).HasColumnName("interval_id");
                entity.Property(x => x.MaterialName).HasColumnName("material_name");
                entity.Property(x => x.Quantity).HasColumnName("quantity");
                entity.Property(x => x.SortOrder).HasColumnName("sort_order");
            });
        }

        private static void ConfigureMaterialGroupRecords(ModelBuilder modelBuilder) {
            modelBuilder.Entity<MaterialGroupRecord>(entity => {
                entity.ToTable("material_groups");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Id).HasColumnName("id");
                entity.Property(x => x.Name).HasColumnName("name");
            });
            modelBuilder.Entity<MaterialGroupItemRecord>(entity => {
                entity.ToTable("material_group_items");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Id).HasColumnName("id");
                entity.Property(x => x.GroupId).HasColumnName("group_id");
                entity.Property(x => x.SourceTable).HasColumnName("source_table");
                entity.Property(x => x.MaterialName).HasColumnName("material_name");
            });
        }

        private static void ConfigureMediaStorageSettings(ModelBuilder modelBuilder) {
            modelBuilder.Entity<MediaStorageSettingsRecord>(entity => {
                entity.ToTable("media_storage_settings");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Id).HasColumnName("id");
                entity.Property(x => x.RetentionDays).HasColumnName("retention_days");
                entity.Property(x => x.UpdatedAt)
                    .HasColumnName("updated_at")
                    .HasDefaultValueSql("NOW()");
            });
        }

        private static void ConfigureStockViews(ModelBuilder modelBuilder) {
            modelBuilder.Entity<FullStockRecord>(entity => {
                entity.HasNoKey();
                // Source views may expose "Количество" as numeric or text.
                // Normalize it in the SQL query before Npgsql materializes a string.
                // Keep this as a composable query so the repositories still use LINQ.
                entity.ToSqlQuery(
                    """
                    SELECT v."Наименование", v."Ед.изм.",
                           v."Количество"::text AS "Количество"
                    FROM v_full_ost AS v
                    """);
                entity.Property(x => x.Name).HasColumnName("Наименование");
                entity.Property(x => x.Unit).HasColumnName("Ед.изм.");
                entity.Property(x => x.Quantity).HasColumnName("Количество");
            });
            modelBuilder.Entity<MechanicalStockRecord>(entity => {
                entity.HasNoKey();
                entity.ToSqlQuery(
                    """
                    SELECT v."Наименование", v."Ед.изм.",
                           v."Количество"::text AS "Количество"
                    FROM v_meh_ost AS v
                    """);
                entity.Property(x => x.Name).HasColumnName("Наименование");
                entity.Property(x => x.Unit).HasColumnName("Ед.изм.");
                entity.Property(x => x.Quantity).HasColumnName("Количество");
            });
            modelBuilder.Entity<FullStockLegacyRecord>(entity => {
                entity.HasNoKey();
                entity.ToSqlQuery(
                    """
                    SELECT v.name, v.amount::text AS amount
                    FROM full_ost AS v
                    """);
                entity.Property(x => x.Name).HasColumnName("name");
                entity.Property(x => x.Amount).HasColumnName("amount");
            });
            modelBuilder.Entity<MechanicalStockLegacyRecord>(entity => {
                entity.HasNoKey();
                entity.ToSqlQuery(
                    """
                    SELECT v.name, v.amount::text AS amount
                    FROM meh_ost AS v
                    """);
                entity.Property(x => x.Name).HasColumnName("name");
                entity.Property(x => x.Amount).HasColumnName("amount");
            });
        }

        private static EntityTypeBuilder<T> ConfigureCreatedAt<T>(
            EntityTypeBuilder<T> entity,
            string tableName) where T : class {
            entity.ToTable(tableName);
            entity.HasKey("Id");
            entity.Property<Guid>("Id").HasColumnName("id");
            entity.Property<DateTimeOffset>("CreatedAt")
                .HasColumnName("created_at")
                .HasDefaultValueSql("NOW()");
            return entity;
        }

        private static void ConfigureMedia<T>(
            ModelBuilder modelBuilder,
            string tableName,
            Action<EntityTypeBuilder<T>> configureParent)
            where T : class, IVehicleMediaRecord {
            var entity = modelBuilder.Entity<T>();
            ConfigureCreatedAt(entity, tableName);
            entity.Property(x => x.FileName).HasColumnName("file_name").HasMaxLength(255);
            entity.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(100);
            entity.Property(x => x.Content).HasColumnName("content");
            entity.Property(x => x.Size).HasColumnName("size");
            configureParent(entity);
        }
    }
}
