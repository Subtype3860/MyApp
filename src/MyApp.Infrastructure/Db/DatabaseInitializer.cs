using Microsoft.EntityFrameworkCore;
using MyApp.Application.Abstractions;
using MyApp.Application.Security;
using MyApp.Domain.Entities;
using MyApp.Infrastructure.Db.Entities;

namespace MyApp.Infrastructure.Db;

public sealed class DatabaseInitializer(AppDbContext db) : IDatabaseInitializer
{
    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS professions (
                id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                profession varchar(40) NOT NULL
            )
            """,
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS material_groups (
                id uuid PRIMARY KEY,
                name varchar(100) NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_material_groups_name
                ON material_groups (LOWER(BTRIM(name)));

            CREATE TABLE IF NOT EXISTS material_group_items (
                id uuid PRIMARY KEY,
                group_id uuid NOT NULL,
                source_table varchar(20) NOT NULL,
                material_name text NOT NULL,
                CONSTRAINT fk_material_group_items_group
                    FOREIGN KEY (group_id)
                    REFERENCES material_groups(id)
                    ON DELETE CASCADE,
                CONSTRAINT ck_material_group_items_source
                    CHECK (source_table IN ('v_full_ost', 'v_meh_ost'))
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_material_group_items_material
                ON material_group_items (source_table, BTRIM(material_name));
            """,
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS maintenance_equipment (
                id uuid PRIMARY KEY,
                name varchar(100) NOT NULL,
                sort_order integer NOT NULL DEFAULT 0
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_maintenance_equipment_name
                ON maintenance_equipment (LOWER(BTRIM(name)));

            CREATE TABLE IF NOT EXISTS maintenance_intervals (
                id uuid PRIMARY KEY,
                equipment_id uuid NOT NULL,
                name varchar(100) NOT NULL,
                sort_order integer NOT NULL DEFAULT 0,
                CONSTRAINT fk_maintenance_intervals_equipment
                    FOREIGN KEY (equipment_id)
                    REFERENCES maintenance_equipment(id)
                    ON DELETE CASCADE
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_maintenance_intervals_name
                ON maintenance_intervals (
                    equipment_id, LOWER(BTRIM(name)));

            CREATE TABLE IF NOT EXISTS maintenance_interval_items (
                id uuid PRIMARY KEY,
                interval_id uuid NOT NULL,
                material_name text NOT NULL,
                quantity numeric NOT NULL,
                sort_order integer NOT NULL DEFAULT 0,
                CONSTRAINT fk_maintenance_items_interval
                    FOREIGN KEY (interval_id)
                    REFERENCES maintenance_intervals(id)
                    ON DELETE CASCADE,
                CONSTRAINT ck_maintenance_items_quantity
                    CHECK (quantity > 0)
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_maintenance_items_material
                ON maintenance_interval_items (
                    interval_id, BTRIM(material_name));
            """,
            cancellationToken);
        await EnsureMaintenanceDefaultsAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS app_users (
                id uuid PRIMARY KEY,
                first_name varchar(100) NOT NULL,
                middle_name varchar(100) NOT NULL,
                last_name varchar(100) NOT NULL,
                user_name varchar(50) NOT NULL UNIQUE,
                email varchar(100) NOT NULL UNIQUE,
                password_hash text NOT NULL,
                position uuid NOT NULL,
                role varchar(30) NOT NULL,
                permissions text NOT NULL DEFAULT 'menu.tables,menu.vehicles,menu.requirements,menu.maintenance',
                created_at timestamptz NOT NULL DEFAULT NOW(),
                CONSTRAINT fk_app_users_professions_position
                    FOREIGN KEY (position) REFERENCES professions(id)
                    ON DELETE RESTRICT
            )
            """,
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE app_users
            ADD COLUMN IF NOT EXISTS permissions text
                NOT NULL DEFAULT 'menu.tables,menu.vehicles,menu.requirements,menu.maintenance'
            """,
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """
            DO $$
            DECLARE
                position_data_type text;
            BEGIN
                SELECT data_type
                INTO position_data_type
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'app_users'
                  AND column_name = 'position';

                IF position_data_type IN ('character varying', 'text') THEN
                    INSERT INTO professions (profession)
                    SELECT DISTINCT LEFT(BTRIM(users.position), 40)
                    FROM app_users AS users
                    WHERE BTRIM(users.position) <> ''
                      AND NOT EXISTS (
                          SELECT 1
                          FROM professions AS professions
                          WHERE LOWER(professions.profession) =
                                LOWER(LEFT(BTRIM(users.position), 40))
                      );

                    INSERT INTO professions (profession)
                    SELECT 'Не указана'
                    WHERE EXISTS (
                        SELECT 1 FROM app_users WHERE BTRIM(position) = ''
                    )
                      AND NOT EXISTS (
                        SELECT 1 FROM professions
                        WHERE LOWER(profession) = LOWER('Не указана')
                    );

                    ALTER TABLE app_users ADD COLUMN position_id uuid;

                    UPDATE app_users AS users
                    SET position_id = professions.id
                    FROM professions AS professions
                    WHERE LOWER(professions.profession) =
                          LOWER(LEFT(BTRIM(users.position), 40));

                    UPDATE app_users AS users
                    SET position_id = professions.id
                    FROM professions AS professions
                    WHERE users.position_id IS NULL
                      AND LOWER(professions.profession) =
                          LOWER('Не указана');

                    ALTER TABLE app_users
                        ALTER COLUMN position_id SET NOT NULL;
                    ALTER TABLE app_users DROP COLUMN position;
                    ALTER TABLE app_users
                        RENAME COLUMN position_id TO position;
                END IF;
            END
            $$;
            """,
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE app_users
                ADD COLUMN IF NOT EXISTS avatar_content bytea,
                ADD COLUMN IF NOT EXISTS avatar_content_type varchar(100);
            """,
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """
            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1
                    FROM pg_constraint
                    WHERE conname = 'fk_app_users_professions_position'
                ) THEN
                    ALTER TABLE app_users
                    ADD CONSTRAINT fk_app_users_professions_position
                    FOREIGN KEY (position)
                    REFERENCES professions(id)
                    ON DELETE RESTRICT;
                END IF;
            END
            $$;
            """,
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS component_requirements (
                id uuid PRIMARY KEY,
                created_at timestamptz NOT NULL DEFAULT NOW(),
                created_by uuid NOT NULL,
                author_name varchar(300) NOT NULL,
                issuer_name varchar(300) NOT NULL,
                vehicle_number varchar(100) NOT NULL,
                source_table varchar(20) NOT NULL,
                form_data jsonb NOT NULL,
                CONSTRAINT fk_component_requirements_created_by
                    FOREIGN KEY (created_by) REFERENCES app_users(id)
                    ON DELETE RESTRICT
            );

            CREATE TABLE IF NOT EXISTS component_requirement_items (
                requirement_id uuid NOT NULL,
                position integer NOT NULL,
                name text NOT NULL,
                unit varchar(100) NOT NULL,
                quantity numeric NOT NULL,
                PRIMARY KEY (requirement_id, position),
                CONSTRAINT fk_component_requirement_items_requirement
                    FOREIGN KEY (requirement_id)
                    REFERENCES component_requirements(id)
                    ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS ix_component_requirements_created_at
                ON component_requirements (created_at DESC);

            ALTER TABLE component_requirements
                ADD COLUMN IF NOT EXISTS issuer_name varchar(300) NOT NULL DEFAULT '',
                ADD COLUMN IF NOT EXISTS source_table varchar(20) NOT NULL DEFAULT '',
                ADD COLUMN IF NOT EXISTS form_data jsonb;

            UPDATE component_requirements AS requirements
            SET source_table = CASE
                WHEN LOWER(employees."Profession") = LOWER('Кладовщик')
                    THEN 'v_full_ost'
                WHEN LOWER(employees."Profession") = LOWER('Старший механик')
                    THEN 'v_meh_ost'
                ELSE requirements.source_table
            END
            FROM employees
            WHERE requirements.source_table = ''
              AND REGEXP_REPLACE(
                    BTRIM(employees."FullName"),
                    '\s+',
                    ' ',
                    'g') = REGEXP_REPLACE(
                        BTRIM(requirements.issuer_name),
                        '\s+',
                        ' ',
                        'g');

            UPDATE component_requirements AS requirements
            SET form_data = jsonb_build_object(
                'date', TO_CHAR(requirements.created_at, 'YYYY-MM-DD'),
                'vehicleNumber', requirements.vehicle_number,
                'sourceTable', requirements.source_table,
                'responsibleEmployee', jsonb_build_object(
                    'firstName', '',
                    'patronymic', '',
                    'lastName', ''),
                'items', COALESCE((
                    SELECT jsonb_agg(
                        jsonb_build_object(
                            'name', items.name,
                            'unit', items.unit,
                            'quantity', items.quantity,
                            'availableQuantity', items.quantity)
                        ORDER BY items.position)
                    FROM component_requirement_items AS items
                    WHERE items.requirement_id = requirements.id
                ), '[]'::jsonb),
                'vehicleNumbers', jsonb_build_array(
                    requirements.vehicle_number),
                'pages', '[]'::jsonb,
                'jobName', 'Аварийная',
                'authorPosition', '',
                'authorName', requirements.author_name,
                'issuerPosition', '',
                'issuerName', requirements.issuer_name)
            WHERE requirements.form_data IS NULL;

            ALTER TABLE component_requirements
                ALTER COLUMN form_data SET NOT NULL,
                DROP COLUMN IF EXISTS pdf_file_name,
                DROP COLUMN IF EXISTS pdf_content;
            """,
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS employee_signatures (
                last_name text NOT NULL,
                first_name text NOT NULL,
                patronymic text NOT NULL DEFAULT '',
                content bytea NOT NULL,
                content_type varchar(100) NOT NULL,
                updated_at timestamptz NOT NULL DEFAULT NOW(),
                PRIMARY KEY (last_name, first_name, patronymic)
            );
            """,
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS vehicle_purchase_requests (
                id uuid PRIMARY KEY,
                vehicle_id uuid NOT NULL REFERENCES number_car(id) ON DELETE CASCADE,
                request_date date NOT NULL,
                request_number varchar(100) NOT NULL DEFAULT '',
                item_name varchar(500) NOT NULL,
                quantity numeric NOT NULL CHECK (quantity > 0),
                status varchar(100) NOT NULL DEFAULT '',
                note text NOT NULL DEFAULT '',
                created_by uuid NOT NULL REFERENCES app_users(id) ON DELETE RESTRICT,
                created_at timestamptz NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS vehicle_defects (
                id uuid PRIMARY KEY,
                vehicle_id uuid NOT NULL REFERENCES number_car(id) ON DELETE CASCADE,
                node_name text NOT NULL DEFAULT '',
                failure_reason text NOT NULL DEFAULT '',
                error_code varchar(100) NOT NULL DEFAULT '',
                symptoms text NOT NULL DEFAULT '',
                downtime_started_at timestamptz NOT NULL DEFAULT NOW(),
                assigned_to uuid REFERENCES app_users(id) ON DELETE RESTRICT,
                repair_started_at timestamptz,
                created_by uuid NOT NULL REFERENCES app_users(id) ON DELETE RESTRICT,
                created_at timestamptz NOT NULL DEFAULT NOW()
            );

            ALTER TABLE vehicle_defects
                ADD COLUMN IF NOT EXISTS node_name text NOT NULL DEFAULT '',
                ADD COLUMN IF NOT EXISTS failure_reason text NOT NULL DEFAULT '',
                ADD COLUMN IF NOT EXISTS error_code varchar(100) NOT NULL DEFAULT '',
                ADD COLUMN IF NOT EXISTS symptoms text NOT NULL DEFAULT '',
                ADD COLUMN IF NOT EXISTS downtime_started_at timestamptz
                    NOT NULL DEFAULT NOW(),
                ADD COLUMN IF NOT EXISTS assigned_to uuid
                    REFERENCES app_users(id) ON DELETE RESTRICT,
                ADD COLUMN IF NOT EXISTS repair_started_at timestamptz;

            UPDATE vehicle_defects
            SET symptoms = failure_reason
            WHERE symptoms = '';

            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = current_schema()
                      AND table_name = 'vehicle_defects'
                      AND column_name = 'description'
                ) THEN
                    EXECUTE '
                        UPDATE vehicle_defects
                        SET node_name = description
                        WHERE node_name = '''' AND description <> ''''
                    ';
                END IF;
            END
            $$;

            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = current_schema()
                      AND table_name = 'vehicle_defects'
                      AND column_name = 'part_article'
                ) THEN
                    EXECUTE '
                        UPDATE vehicle_defects
                        SET node_name = part_article
                        WHERE node_name = '''' AND part_article <> ''''
                    ';
                END IF;
            END
            $$;

            CREATE TABLE IF NOT EXISTS vehicle_hour_readings (
                id uuid PRIMARY KEY,
                vehicle_id uuid NOT NULL REFERENCES number_car(id) ON DELETE CASCADE,
                reading_date date NOT NULL,
                engine_hours numeric NOT NULL CHECK (engine_hours >= 0),
                note text NOT NULL DEFAULT '',
                created_by uuid NOT NULL REFERENCES app_users(id) ON DELETE RESTRICT,
                created_at timestamptz NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS vehicle_works (
                id uuid PRIMARY KEY,
                vehicle_id uuid NOT NULL REFERENCES number_car(id) ON DELETE CASCADE,
                work_date date NOT NULL,
                description text NOT NULL,
                engine_hours numeric CHECK (engine_hours >= 0),
                performer varchar(300) NOT NULL DEFAULT '',
                note text NOT NULL DEFAULT '',
                defect_id uuid REFERENCES vehicle_defects(id) ON DELETE CASCADE,
                failure_cause text NOT NULL DEFAULT '',
                repair_status varchar(30) NOT NULL DEFAULT 'repaired',
                required_parts text NOT NULL DEFAULT '',
                performed_by uuid REFERENCES app_users(id) ON DELETE RESTRICT,
                completed_at timestamptz,
                created_by uuid NOT NULL REFERENCES app_users(id) ON DELETE RESTRICT,
                created_at timestamptz NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS vehicle_parts_requests (
                id uuid PRIMARY KEY,
                defect_id uuid NOT NULL REFERENCES vehicle_defects(id) ON DELETE CASCADE,
                request_date date NOT NULL,
                request_number varchar(100) NOT NULL DEFAULT '',
                description text NOT NULL DEFAULT '',
                required_parts text NOT NULL DEFAULT '',
                created_by uuid NOT NULL REFERENCES app_users(id) ON DELETE RESTRICT,
                created_at timestamptz NOT NULL DEFAULT NOW()
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_vehicle_parts_requests_legacy
                ON vehicle_parts_requests (defect_id, request_number, request_date, description);
            CREATE INDEX IF NOT EXISTS ix_vehicle_parts_requests_defect_created
                ON vehicle_parts_requests (defect_id, created_at);

            ALTER TABLE vehicle_works
                DROP COLUMN IF EXISTS purchase_request_number,
                DROP COLUMN IF EXISTS purchase_request_date,
                DROP COLUMN IF EXISTS purchase_request_file_name,
                DROP COLUMN IF EXISTS purchase_request_content_type,
                DROP COLUMN IF EXISTS purchase_request_content,
                DROP COLUMN IF EXISTS purchase_request_description;

            ALTER TABLE vehicle_works
                ADD COLUMN IF NOT EXISTS defect_id uuid
                    REFERENCES vehicle_defects(id) ON DELETE CASCADE,
                ADD COLUMN IF NOT EXISTS failure_cause text NOT NULL DEFAULT '',
                ADD COLUMN IF NOT EXISTS repair_status varchar(30)
                    NOT NULL DEFAULT 'repaired',
                ADD COLUMN IF NOT EXISTS required_parts text NOT NULL DEFAULT '',
                ADD COLUMN IF NOT EXISTS performed_by uuid
                    REFERENCES app_users(id) ON DELETE RESTRICT,
                ADD COLUMN IF NOT EXISTS completed_at timestamptz;

            UPDATE vehicle_works
            SET failure_cause = description
            WHERE failure_cause = '';

            UPDATE vehicle_works
            SET performed_by = created_by,
                completed_at = created_at
            WHERE performed_by IS NULL;

            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'ck_vehicle_works_repair_status'
                ) THEN
                    ALTER TABLE vehicle_works
                    ADD CONSTRAINT ck_vehicle_works_repair_status
                    CHECK (repair_status IN (
                        'repaired', 'faulty', 'awaiting_parts'));
                END IF;
            END
            $$;

            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = current_schema()
                      AND table_name = 'vehicle_defects'
                      AND column_name = 'corrective_work'
                ) THEN
                    EXECUTE '
                        INSERT INTO vehicle_works (
                            id, vehicle_id, work_date, description,
                            engine_hours, performer, note, defect_id,
                            created_by, created_at)
                        SELECT
                            md5(defects.id::text || '':legacy-repair'')::uuid,
                            defects.vehicle_id,
                            defects.created_at::date,
                            defects.corrective_work,
                            NULL,
                            '''',
                            '''',
                            defects.id,
                            defects.created_by,
                            defects.created_at
                        FROM vehicle_defects AS defects
                        WHERE defects.corrective_work <> ''''
                          AND NOT EXISTS (
                              SELECT 1
                              FROM vehicle_works AS works
                              WHERE works.defect_id = defects.id
                                AND works.description = defects.corrective_work)
                    ';
                END IF;
            END
            $$;

            UPDATE vehicle_works
            SET failure_cause = description,
                performed_by = created_by,
                completed_at = created_at
            WHERE performed_by IS NULL;

            CREATE TABLE IF NOT EXISTS vehicle_work_photos (
                id uuid PRIMARY KEY,
                work_id uuid NOT NULL
                    REFERENCES vehicle_works(id) ON DELETE CASCADE,
                file_name varchar(255) NOT NULL,
                content_type varchar(100) NOT NULL,
                content bytea,
                storage_path text,
                size bigint NOT NULL DEFAULT 0,
                created_at timestamptz NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS vehicle_defect_photos (
                id uuid PRIMARY KEY,
                defect_id uuid NOT NULL
                    REFERENCES vehicle_defects(id) ON DELETE CASCADE,
                file_name varchar(255) NOT NULL,
                content_type varchar(100) NOT NULL,
                content bytea,
                storage_path text,
                size bigint NOT NULL DEFAULT 0,
                created_at timestamptz NOT NULL DEFAULT NOW()
            );

            ALTER TABLE vehicle_work_photos
                ALTER COLUMN content DROP NOT NULL,
                ADD COLUMN IF NOT EXISTS storage_path text,
                ADD COLUMN IF NOT EXISTS size bigint NOT NULL DEFAULT 0;
            ALTER TABLE vehicle_defect_photos
                ALTER COLUMN content DROP NOT NULL,
                ADD COLUMN IF NOT EXISTS storage_path text,
                ADD COLUMN IF NOT EXISTS size bigint NOT NULL DEFAULT 0;

            UPDATE vehicle_work_photos
            SET size = OCTET_LENGTH(content)
            WHERE size = 0 AND content IS NOT NULL;
            UPDATE vehicle_defect_photos
            SET size = OCTET_LENGTH(content)
            WHERE size = 0 AND content IS NOT NULL;

            CREATE TABLE IF NOT EXISTS vehicle_defect_videos (
                id uuid PRIMARY KEY,
                defect_id uuid NOT NULL
                    REFERENCES vehicle_defects(id) ON DELETE CASCADE,
                file_name varchar(255) NOT NULL,
                content_type varchar(100) NOT NULL,
                content bytea,
                storage_path text NOT NULL,
                size bigint NOT NULL,
                created_at timestamptz NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS vehicle_work_videos (
                id uuid PRIMARY KEY,
                work_id uuid NOT NULL
                    REFERENCES vehicle_works(id) ON DELETE CASCADE,
                file_name varchar(255) NOT NULL,
                content_type varchar(100) NOT NULL,
                content bytea,
                storage_path text NOT NULL,
                size bigint NOT NULL,
                created_at timestamptz NOT NULL DEFAULT NOW()
            );

            CREATE INDEX IF NOT EXISTS ix_vehicle_purchases_vehicle_date
                ON vehicle_purchase_requests (vehicle_id, request_date DESC);
            CREATE INDEX IF NOT EXISTS ix_vehicle_defects_vehicle_created
                ON vehicle_defects (vehicle_id, created_at DESC);
            CREATE INDEX IF NOT EXISTS ix_vehicle_hours_vehicle_date
                ON vehicle_hour_readings (vehicle_id, reading_date DESC);
            CREATE INDEX IF NOT EXISTS ix_vehicle_works_vehicle_date
                ON vehicle_works (vehicle_id, work_date DESC);
            CREATE INDEX IF NOT EXISTS ix_vehicle_works_defect
                ON vehicle_works (defect_id, created_at DESC);
            CREATE INDEX IF NOT EXISTS ix_vehicle_work_photos_work
                ON vehicle_work_photos (work_id, created_at);
            CREATE INDEX IF NOT EXISTS ix_vehicle_defect_photos_defect
                ON vehicle_defect_photos (defect_id, created_at);
            CREATE INDEX IF NOT EXISTS ix_vehicle_defect_videos_defect
                ON vehicle_defect_videos (defect_id, created_at);
            CREATE INDEX IF NOT EXISTS ix_vehicle_work_videos_work
                ON vehicle_work_videos (work_id, created_at);
            """,
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS media_storage_settings (
                id smallint PRIMARY KEY CHECK (id = 1),
                retention_days integer NOT NULL
                    CHECK (retention_days BETWEEN 1 AND 180),
                updated_at timestamptz NOT NULL DEFAULT NOW()
            );
            """,
            cancellationToken);
        if (!await db.MediaStorageSettings.AnyAsync(
                settings => settings.Id == 1,
                cancellationToken))
        {
            db.MediaStorageSettings.Add(new MediaStorageSettingsRecord
            {
                Id = 1,
                RetentionDays = 1,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        var administratorProfession = await db.Professions.FirstOrDefaultAsync(
            profession => profession.Name == "Администратор",
            cancellationToken);
        if (administratorProfession is null)
        {
            administratorProfession = new Profession
            {
                Id = Guid.NewGuid(),
                Name = "Администратор"
            };
            db.Professions.Add(administratorProfession);
            await db.SaveChangesAsync(cancellationToken);
        }

        if (await db.Users.AnyAsync(
            user => user.UserName == "boora",
            cancellationToken))
        {
            return;
        }

        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            FirstName = "Администратор",
            MiddleName = string.Empty,
            LastName = "Системы",
            UserName = "boora",
            Email = "boora@local",
            PasswordHash =
                "pbkdf2-sha256$100000$fwsW/+p0gAm3idMJZNn4Jw==$" +
                "RywEjkyVe79il+wrlDQ2SKatTxMl4GLyzef57VQDf54=",
            PositionId = administratorProfession.Id,
            Role = "administrator",
            Permissions = string.Join(',', Permissions.All),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureMaintenanceDefaultsAsync(
        CancellationToken cancellationToken)
    {
        var equipmentDefaults = new[]
        {
            (Name: "Экскаваторы", SortOrder: 0),
            (Name: "Самосвалы", SortOrder: 1)
        };
        var equipment = await db.MaintenanceEquipment
            .ToListAsync(cancellationToken);
        foreach (var (name, sortOrder) in equipmentDefaults)
        {
            if (equipment.Any(existing =>
                    string.Equals(
                        existing.Name.Trim(),
                        name.Trim(),
                        StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var created = new MaintenanceEquipmentRecord
            {
                Id = Guid.NewGuid(),
                Name = name,
                SortOrder = sortOrder
            };
            equipment.Add(created);
            db.MaintenanceEquipment.Add(created);
        }
        await db.SaveChangesAsync(cancellationToken);

        var intervalDefaults = new[]
        {
            (Equipment: "Экскаваторы", Name: "ТО-100", SortOrder: 0),
            (Equipment: "Экскаваторы", Name: "ТО-250", SortOrder: 1),
            (Equipment: "Экскаваторы", Name: "ТО-500", SortOrder: 2),
            (Equipment: "Экскаваторы", Name: "ТО-1000", SortOrder: 3),
            (Equipment: "Экскаваторы", Name: "ТО-2000", SortOrder: 4),
            (Equipment: "Экскаваторы", Name: "ТО-4000", SortOrder: 5),
            (Equipment: "Самосвалы", Name: "ТО-100", SortOrder: 0),
            (Equipment: "Самосвалы", Name: "ТО-350", SortOrder: 1),
            (Equipment: "Самосвалы", Name: "ТО-500", SortOrder: 2),
            (Equipment: "Самосвалы", Name: "ТО-700", SortOrder: 3),
            (Equipment: "Самосвалы", Name: "ТО-1000", SortOrder: 4),
            (Equipment: "Самосвалы", Name: "ТО-2000", SortOrder: 5),
            (Equipment: "Самосвалы", Name: "ТО-4000", SortOrder: 6)
        };
        var existingIntervals = await db.MaintenanceIntervals
            .ToListAsync(cancellationToken);
        foreach (var (equipmentName, name, sortOrder) in intervalDefaults)
        {
            var equipmentRow = equipment.FirstOrDefault(existing =>
                string.Equals(
                    existing.Name.Trim(),
                    equipmentName,
                    StringComparison.OrdinalIgnoreCase));
            if (equipmentRow is null ||
                existingIntervals.Any(existing =>
                    existing.EquipmentId == equipmentRow.Id &&
                    string.Equals(
                        existing.Name.Trim(),
                        name,
                        StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var created = new MaintenanceIntervalRecord
            {
                Id = Guid.NewGuid(),
                EquipmentId = equipmentRow.Id,
                Name = name,
                SortOrder = sortOrder
            };
            existingIntervals.Add(created);
            db.MaintenanceIntervals.Add(created);
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
