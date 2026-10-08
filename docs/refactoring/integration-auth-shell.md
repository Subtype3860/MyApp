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
