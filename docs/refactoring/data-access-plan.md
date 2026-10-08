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
