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
- Parallel creation of maintenance template records using
  `MAX(sort_order)+1` can allocate duplicate sort order.
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

- [ ] Review `master`-only changes, especially October 1 edits/deletes of
  parts requests and the October 7 vehicle-journal update. Map user-facing
  functionality onto the current controller and DTO model.
- [ ] Reassess vehicle-hours batch import performance and concurrency within
  the current EF-based `VehicleRepository`; old Npgsql classes cannot be
  pasted in without losing repair-history/media improvements.
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
