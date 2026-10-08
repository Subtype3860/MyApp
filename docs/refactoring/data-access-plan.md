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
