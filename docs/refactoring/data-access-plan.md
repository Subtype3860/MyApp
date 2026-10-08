# Data access refactoring plan

This document tracks the incremental refactor of MyApp. All implementation changes are made on `refactor/clean-architecture` and must preserve the existing HTTP API and database data.

## Rules

1. Do not alter `master` directly. Submit changes through pull requests.
2. Preserve routes, authorization rules, request and response contracts.
3. Use EF Core for ordinary entity CRUD and LINQ-translatable queries.
4. Keep parameterized Npgsql SQL where PostgreSQL-specific operations are necessary (large objects, dynamic allowlisted views, `edit_csv_tab`, bulk operations). Record each exception.
5. Do not replace the current database initializer until a baseline migration and upgrade path have been tested against a copy of existing data.
6. Maintain transaction boundaries and atomic business invariants when moving operations between repositories.
7. Validate each slice with build, unit tests and PostgreSQL integration tests before merging.

## Implementation slices

- [ ] Baseline build and integration tests for critical vehicle and stock workflows.
- [ ] EF Core entity mapping and baseline migrations with data-preserving upgrade path.
- [ ] Split `IVehicleRepository` and `VehicleRepository` by vehicle, defect, work, hours, purchase and media responsibilities.
- [ ] Move business workflows out of `VehicleService` into focused application services.
- [ ] Extract stock and requirement workflows from `RequirementJournalRepository`.
- [ ] Convert ordinary CRUD SQL to EF Core/LINQ.
- [ ] Profile journal and bulk hours import; optimize based on measurements.
- [ ] Document retained PostgreSQL-specific queries and test their inputs.

## Known risks to cover with tests

- `VehicleRepository.CompleteDefectAsync`: assignment and completion must remain atomic.
- `VehicleRepository.ImportHoursAsync`: repeated imports and missing readings must retain existing behavior.
- `RequirementJournalRepository.SaveAsync/DeleteAsync`: stock changes must remain consistent with journal records.
- Media file writes and deletes: ensure failures do not leave inconsistent metadata.
- `MaintenanceTemplateRepository`: concurrent sort-order assignment.
- Existing `DatabaseInitializer` behavior on deployed databases.

## Definition of done

No API contract regressions; no destructive migration without an explicit upgrade procedure; all new data access paths covered by tests; CI build green; SQL exceptions documented.


## Verified implementation progress (2026-10-08)

Completed without changing production database schema:

- [x] Added .NET 10 CI workflow and a unit-test project. EF Core model mappings and DI composition are covered by isolated tests.
- [x] Split the original vehicle repository into feature-oriented partial files, preserving SQL and transaction method bodies during the move.
- [x] Split the original vehicle application service into feature-oriented partial files, preserving existing request validation and parsing implementations during the move.
- [x] Extracted the existing EF Core mapping for `User` and `Profession` into individual `IEntityTypeConfiguration<T>` classes.
- [x] Segregated vehicle repository contracts into eight narrowly scoped interfaces, retaining `IVehicleRepository` as a compatibility facade.
- [x] Replaced direct parts request handling within the facade with `VehiclePartsRepository`, registered via a scoped `IVehiclePartsRepository` port.
- [x] Replaced direct purchase handling within the facade with `VehiclePurchaseRepository`, registered via a scoped `IVehiclePurchaseRepository` port.

Outstanding high-priority work:

- [ ] PostgreSQL integration tests for journal, defect completion, imports and file-backed stock changes.
- [ ] Introduce EF Core entity mappings for remaining standard tables and plan a verified baseline migration (without running schema changes on startup).
- [ ] Move remaining feature-oriented partial implementations into independent repositories and focused application services.
- [ ] Migrate ordinary CRUD to EF Core/LINQ while preserving specialized PostgreSQL SQL where justified.
- [ ] Benchmark query plans and import roundtrips before claiming performance improvement.
- [ ] Review media and CSV file operations for rollback/compensation behavior.

The current extraction is an *incremental refactor*, not a finished clean architecture. Passing unit tests does not demonstrate database/data migration compatibility.


## PostgreSQL integration and first LINQ conversion (2026-10-08)

- [x] CI provisions a disposable PostgreSQL 17 database and runs integration tests in isolated, generated schemas.
- [x] Purchase integration checks: roundtrip, vehicle/date filtering, and database quantity constraint.
- [x] Parts integration checks: attachment roundtrip and cleanup, state gating, typed NULL parameters.
- [x] Defect integration checks: assignee authorization, duplicate completion, concurrent completion serialized by `FOR UPDATE`.
- [x] Hours import integration checks: carry-forward, repeated-date update, and transaction rollback on invalid vehicle.
- [x] Corrected `VehiclePartsRepository.DeletePartsRequestAsync`: `purchase_request_number` is `NOT NULL` in the deployed schema, so clearing to SQL `NULL` caused PostgreSQL error 23502; now clears to the permitted empty string.
- [x] Converted `VehiclePurchaseRepository` from direct Npgsql SQL to EF Core CRUD and LINQ read projections with `AsNoTracking`.
- [x] Added `VehiclePurchaseEntity`, `VehiclePurchaseConfiguration`, and regression checks for model metadata.
- [x] CI confirms build, unit tests and PostgreSQL integration tests on the LINQ conversion (commit `1f5432e`).

**Next milestones:** PostgreSQL integration tests for requirement/CSV workflows and media side effects; independent defect/hours/work repository implementations; LINQ mapping and migration strategy for other standard tables. The integration fixture mirrors relevant table columns and constraints; it does **not** validate the existing startup initializer against a real production snapshot. Avoid applying automatic migrations to deployed databases before baseline comparison and backups.


## Material groups EF Core conversion (2026-10-08)

- [x] Added `MaterialGroupEntity` and `MaterialGroupItemEntity` with their EF Core configurations and cascade relationship.
- [x] `MaterialGroupRepository` uses LINQ for groups and items queries, normalized-name checks, create, and `ExecuteDeleteAsync` for deletes.
- [x] Retained raw Npgsql for `MaterialExistsAsync` because it queries a strictly allowlisted PostgreSQL view identifier.
- [x] Added three PostgreSQL integration scenarios for CRUD, date-independent material mappings, uniqueness, cascade deletes, and view allowlisting.
- [x] GitHub Actions confirms .NET 10 build, unit tests, PostgreSQL tests for commit `bb30848`.

**Important:** Existing SQL expression indexes and constraints remain database-managed; do not assume that EF Core model configurations are an exact schema migration baseline. Before generating/applying migrations, compare the actual database schema against the EF snapshot.


## Requirement journal / CSV stock hardening (2026-10-08)

- [x] Extracted `RequirementStockGateway` from `RequirementJournalRepository` and registered it with DI.
- [x] Validate all issued quantities against freshly queried PostgreSQL stock views rather than trusting client-supplied `AvailableQuantity`.
- [x] Aggregate duplicate stock names, reject overdrafts and validate all materials before invoking `edit_csv_tab`.
- [x] Run relational inserts/deletes before CSV side effects to avoid touching CSV when a DB constraint fails first.
- [x] Apply a per-source PostgreSQL transaction advisory lock to this application's issue/restore operations; `SELECT ... FOR UPDATE` prevents concurrent deletion of the same requirement.
- [x] Added eight PostgreSQL integration scenarios with a transactional **DB stub** for `edit_csv_tab`: stale balances, duplicates, insufficient stock, FK failure, simultaneous issue, simultaneous restore, missing materials and simulated DB-function failure. These tests **do not** verify real external CSV rollback.
- [x] Release build, unit tests and PostgreSQL integration tests passed for commit `6f91eab`.
- [x] Documented remaining filesystem inconsistency risks and a DB-ledger/outbox migration proposal in [stock-file-consistency.md](stock-file-consistency.md).

**Not done:** source inspection of deployed `edit_csv_tab`, real physical CSV failure-injection tests, reconciliation/backups and zero-data-loss baseline migration. A PostgreSQL transaction is not an atomic transaction with external CSV files.


## Maintenance template EF Core refactor and concurrency hardening (2026-10-08)

- [x] Added PostgreSQL fixture schema and four baseline integration tests for the existing maintenance template behavior.
- [x] Added `MaintenanceEquipmentEntity`, `MaintenanceIntervalEntity`, `MaintenanceItemEntity` and `IEntityTypeConfiguration<T>` mappings.
- [x] Converted the straightforward maintenance existence queries, normalized name/duplicate checks, quantity changes, renames and deletes to EF Core/LINQ. Retained SQL for stock-view `LEFT JOIN LATERAL` and insertion sort-order logic.
- [x] Wrapped each `MAX(sort_order) + 1` INSERT in a PostgreSQL transaction with a scoped `pg_advisory_xact_lock`, ensuring concurrent inserts through this repository cannot allocate the same order for the same equipment/interval.
- [x] Added a PostgreSQL integration test with parallel equipment, interval and material insert operations, checking consecutive unique `sort_order` values.
- [x] Verified **16 unit and 24 PostgreSQL integration tests** on commit `82c18f5` via [GitHub Actions](https://github.com/Subtype3860/MyApp/actions/runs/37747844151).

### Remaining caveats

- The advisory lock is respected only by writers using this repository; external SQL or startup seed code can still affect ordering without following the same lock policy.
- The EF Core model is **not** a schema migration baseline. Expression indexes such as `LOWER(BTRIM(name))` and `BTRIM(material_name)`, database check constraints and deployed schema variations require separate audit.
- Maintenance `GetAllAsync` retains its PostgreSQL `LEFT JOIN LATERAL` stock lookup. Its query plan must be measured on production-like data before optimizing.
- Real CSV rollback/compensation remains unverified (see [stock-file-consistency.md](stock-file-consistency.md)).


## Independent vehicle hours repository and import optimization (2026-10-08)

- [x] Extracted `VehicleHoursRepository` implementing `IVehicleHoursRepository`. `VehicleRepository` forwards hour creation, import and journal reads; existing HTTP contracts remain unchanged.
- [x] Added `GetHoursAsync` to the narrow hours repository port and registered the independent repository in DI.
- [x] Added isolated DI tests and PostgreSQL tests covering filtering, blank-hour carry-forward, later dates and duplicate import lines.
- [x] Optimized CSV import carry-forward: one parameterized `DISTINCT ON (vehicle_id)` query for all blank-hour vehicle IDs, replacing one SELECT per blank import line. The mutable cache preserves semantics for repeated lines in a single transaction.
- [x] Added a transaction-level advisory lock keyed by reading date to serialize simultaneous CSV imports for that day. This prevents duplicate insertion via this import path without changing the existing database schema.
- [x] Verified .NET 10 build, unit and PostgreSQL tests after the optimization (commit `40d9921`).
- [ ] Measure performance on production-like CSV sizes and validate query plans/connection behavior.

Caveats:

- The import still performs individual UPDATE/INSERT statements per row; only blank-hour lookup N+1 has been removed.
- The day-level advisory lock serializes **all** application CSV imports for the same date, including different vehicles. A future improvement could lock ordered vehicle IDs or add a validated uniqueness constraint and use `ON CONFLICT`.
- `AddHoursAsync`, external SQL and other writers do not use this lock. Correctness of globally unique `(vehicle_id, reading_date)` records must eventually be enforced through a reviewed schema migration, after assessing historical duplicates.
- Preserve transaction rollback for mixed valid and invalid imports.


## Independent vehicle work and defect repositories (2026-10-08)

- [x] Added `VehicleWorkRepository` with its own Npgsql access for work creation, ownership checks and work journal data including photos/videos. The compatible `VehicleRepository` forwards to the narrow work port.
- [x] Added `VehicleDefectRepository` with creation, existence checks, claim and atomic completion. The `SELECT ... FOR UPDATE` transaction and duplicate-completion guard were preserved during extraction.
- [x] Extended `IVehicleWorkRepository` and `IVehicleDefectRepository` with journal-query methods. Both narrow ports are now independently registered in DI.
- [x] Added PostgreSQL fixtures for defect/work photo and video metadata; added tests for work journal projections, defect status transitions (`new → in_progress → repaired`), media, and cross-vehicle filtering.
- [x] Updated legacy facade construction and unit-test service registration to resolve the independent repositories. Existing API method signatures are unchanged.
- [x] Removed unused legacy SQL `InsertAsync`, `CreateRangeCommand` and obsolete media list reader after all callers moved, reducing duplicated code in the compatibility facade.
- [x] Verified .NET 10 build and **18 unit / 30 PostgreSQL integration tests** for the independent defect repository on commit `01decc9` ([GitHub Actions](https://github.com/Subtype3860/MyApp/actions/runs/37760449459)).

### Remaining limitations

- The combined `VehicleRepository` still owns vehicle listing, journal composition, entry deletion and media/file side effects. The media and entry ports still resolve to the legacy facade; splitting them safely needs filesystem-failure tests.
- Work/defect journal queries retain specialized Npgsql joins and several database roundtrips. Benchmark query plans on production-like data before optimizing.
- Real disk media cleanup can fail after a database deletion; durable cleanup/compensation remains open.
- The tests use an isolated PostgreSQL fixture, not the deployed database schema, and do not verify all API and frontend scenarios.
