# Integration of `subtype3860-auth-navigation-shell` and DAL refactoring

## Source of truth

This integration branch was created directly from commit
`d85595d8ee2545f9b6f8195aa36fd1659d5203f2` of
`subtype3860-auth-navigation-shell`. Neither that branch nor `master`
nor the previous `refactor/clean-architecture` branch is modified.

The two histories are divergent. This branch already contains:
- Controller-based HTTP API (instead of the earlier minimal API endpoints)
- EF Core mappings for vehicle records and LINQ-based vehicle queries
- Repair history / batched repair journal loading
- Media storage service and staged file transfer, WebP conversion
- Updated authentication/navigation and frontend UI

**Do not cherry-pick all old refactor commits.** They were written against
another version of DTOs, interfaces, media storage and database mappings.
Overwriting the feature branch's `VehicleRepository`,
`AppDbContext`, `VehicleService` or `Program.cs` with the old
versions would remove new functionality.

## Transfer strategy

1. Establish independent .NET 10 and frontend CI against this baseline.
2. Port isolated unit and PostgreSQL integration checks adapted to *current*
   DTOs/entities, not copied wholesale.
3. Adapt correctness fixes (maintenance `sort_order` allocation,
   requirements/CSV issue, hours concurrency) to the current EF-based
   repositories, one feature at a time with regression tests.
4. Keep Controller endpoints, repair history and media storage intact.
5. Review the three `master`-only commits separately; decide which
   behaviors remain relevant and adapt rather than blindly merging.
6. Validate production-schema compatibility, real file writes, and
   API/frontend scenarios before the final merge.

## Risk ledger

- `RequirementJournalRepository` invokes `edit_csv_tab`, which may
  have external CSV side effects outside database rollback.
- The app currently has startup schema initialization; do not apply an
  unverified EF migration to a deployed database.
- Maintenance template sort-order allocation is now serialized in this
  repository through PostgreSQL advisory locks. External/direct SQL writers
  bypassing those locks can still create duplicate ordering.
- The latest repair/media API and DTO contracts differ substantially
  from the old refactor branch. They require integration regression tests.

## Evidence

GitHub Actions workflow: `.github/workflows/integration-ci.yml`.
The older refactor remains available for cherry-pick/adaptation selectively:
`refactor/clean-architecture` (draft PR #6 against `master`).

## Implemented in integration branch (2026-10-08)

- [x] .NET 10 and Vue builds run independently in GitHub Actions.
- [x] Ported xUnit test project with 5 tests of the **current** auth/navigation
  permissions, including legacy permission mapping.
- [x] Added isolated PostgreSQL 17 fixtures and maintenance template tests
  against the current EF Core entity model (not the old repository models).
- [x] Fixed concurrent `MAX(sort_order)+1` allocation in
  `MaintenanceTemplateRepository`: scoped PostgreSQL advisory lock protects
  SELECT and INSERT in one transaction, with parallel regression tests.
- [x] Adapted the old stock issue/restore safeguards to this branch's
  `RequirementJournalRepository` using its current `AppDbContext`.
  Stock is read from server-side views, duplicate materials are aggregated,
  all changes are preflighted, and issue/delete operations are locked.
- [x] Fixed the discovered EF Core parent/child insertion-order error:
  requirement header is saved before its items in the same transaction.
- [x] Tests check stale client balances, duplicate stock lines, invalid
  parent FK, insufficient/missing material and concurrent issue/restore.
- [x] Latest tested code (commit `8221ab8`): 5 unit + 11 PostgreSQL tests;
  backend and frontend builds passed in GitHub Actions.

## Not yet transferred / do not blindly merge

- [x] Verify October 1 master-only part-request edit/delete behavior in
  current Controllers, services, Vue UI and EF Core DAL. Added PostgreSQL
  regression tests for create/edit/delete, awaiting_parts and duplicates.
- [ ] Compare the October 7 master-only vehicle-journal/frontend update
  against the newer Controller API, repair history, responsive UI and media
  streaming before declaring full feature parity.
- [x] Optimize vehicle-hours CSV import in the current EF Core repository:
  one grouped latest-reading SELECT for all blank rows, in-transaction cache
  for repeated vehicle rows, date-scoped advisory lock; behavior and query
  count verified by PostgreSQL tests.
- [ ] Decide whether to enforce global `(vehicle_id, reading_date)` uniqueness
  after auditing historical rows. Direct AddHoursAsync/external writers do
  not participate in the import lock.
- [ ] Verify `edit_csv_tab` and `lo_export` on actual staging CSV files;
  an EF transaction does not roll back the filesystem.
- [ ] Audit deployed schema against `DatabaseInitializer` and EF metadata
  before baseline migrations; run tests on a restored, isolated data copy.
- [ ] Check authorization, media streaming, WebP, staged media transfer and
  repair history end-to-end after integration.
- [ ] Benchmark journal loading and uploads against representative data.

The source `subtype3860-auth-navigation-shell` and previous
`refactor/clean-architecture` remain unchanged. This integration PR
targets the former, not `master`.

## New regression coverage and import safety (2026-10-09)

### EF Core vehicle hours

- [x] Added a disposable PostgreSQL schema fixture:
  `VehicleHoursPostgreSqlDatabase`.
- [x] Preserved carry-forward semantics for blank CSV cells (as-of date),
  repeated lines in one import, and rollback on a PostgreSQL check failure.
- [x] Replaced per-blank-row lookup with a grouped latest-reading LINQ query:
  `GroupBy(VehicleId)`, ordered by `ReadingDate` and `CreatedAt`.
- [x] Added in-memory carry-forward updates within the same transaction:
  rows for a vehicle later in the CSV see the updated reading.
- [x] Added a PostgreSQL transaction advisory lock by reading date; concurrent
  calls through this `ImportHoursAsync` implementation cannot both insert
  the same vehicle/date into an initially empty day.
- [x] Regression verifies **32 blank vehicle readings use exactly one SELECT**
  (EF Core command interceptor), rather than one SELECT per blank row.
- [x] Verified rollback semantics and concurrent imports. See CI run
  [#28](https://github.com/Subtype3860/MyApp/actions/runs/37827630371):
  5 unit + 16 PostgreSQL integration tests, Vue/frontend build success.

Limitations: the importer still executes UPDATE/INSERT per row, and the lock
covers only callers of `ImportHoursAsync`; `AddHoursAsync`, direct SQL and
external loaders may create duplicates. A unique constraint requires
investigating historical duplicate data and production-safe migrations.

### Master-only parts requests

- [x] Confirmed the current `VehicleController` includes POST/PUT/DELETE
  routes for part requests; `VehicleService` retains profession/validation
  checks, and `VehicleJournalView.vue` contains edit/delete controls.
- [x] Added current EF Core PostgreSQL regression tests covering creation,
  updates, deletion, forbidden add unless `awaiting_parts`, and legacy
  normalized uniqueness constraint.
- [x] CI [#30](https://github.com/Subtype3860/MyApp/actions/runs/37827943611):
  **5 unit + 19 PostgreSQL integration tests**, backend and Vue build passed.
- [ ] API authentication/authorization and frontend edit/delete interaction
  must still be exercised end-to-end. PostgreSQL DAL tests alone do not
  guarantee frontend behavioral parity with the master-only changes.

### Production release gates

- [ ] Obtain the real deployed `edit_csv_tab` function definition, audit all
  CSV file writes and simulate failures between updates and SQL commit.
- [ ] Verify the actual `DatabaseInitializer` schema against a restored
  production backup, validate EF model assumptions and historical duplicates.
- [ ] Regression-test Controllers, permissions, repair history, media streams,
  staged-file transfers and WebP conversions with real API integration tests.
- [ ] Review master-only journal changes and compare UI against the new
  responsive frontend.
- [ ] Benchmark large CSV imports and repair journal workloads.
- [ ] Keep this pull request **draft**, and do not merge into the source
  branch before the gates above have been reviewed.
