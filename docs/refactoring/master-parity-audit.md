# Master-only commit compatibility audit

The integration branch is based on `subtype3860-auth-navigation-shell`, not
`master`. The histories diverged after common ancestor
`6bdc0b45d454ff6d0f84ace847d0661c4b0509b9`.

## Relevant master-only history

1. `a627b8f`: earlier merge of navigation shell, not a new feature to
   cherry-pick wholesale.
2. `c8c3855`: edit/delete part-request functionality. The current branch
   already includes Controller endpoints, service validation, EF Core
   repository edit/delete operations, and Vue controls. Integration tests
   now verify repository behavior and database uniqueness, but API/UI
   authorization flows still require end-to-end verification.
3. `bcb197d`: October 7 update to vehicle journal and frontend. It touches
   files also rewritten for newer repair history, Controllers and the
   responsive interface. Do **not** replace the current versions with older
   `VehicleEndpoints` or legacy Vue screens. Carry forward missing behavior
   only after a manual feature/contract comparison.

## Compatibility policy

- Do not merge/rebase from `master` simply because it has extra commits.
- Preserve `VehicleController`, new repair journal loading, streamed media,
  staged-file transfers, WebP and responsive Vue navigation.
- Test each missing behavior on the current EF Core model.
- Keep the source branch, master and old clean-architecture branch unchanged
  until this draft PR has passed production-like acceptance checks.

## Evidence

The current repository implements parts-request POST, PUT and DELETE in
`src/MyApp.API/Controllers/VehicleController.cs`. It implements authorization
and validation in `src/MyApp.Application/Services/VehicleService.cs`, DAL in
`src/MyApp.Infrastructure/Repositories/VehicleRepository.cs`, and edit/delete
controls in `frontend/src/components/VehicleJournalView.vue`.

Tests: `src/MyApp.Infrastructure.Tests/VehiclePartsCurrentModelPostgreSqlTests.cs`.
CI: https://github.com/Subtype3860/MyApp/actions/runs/37827943611

The master-only October 7 UI/API changes have **not** been individually
certified as completely replicated.
