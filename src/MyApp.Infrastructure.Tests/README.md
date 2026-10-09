# Integration test instructions

The tests in this branch target the **auth-navigation-shell** EF Core model,
rather than the old refactor repository implementation.

## Unit tests

```bash
dotnet test src/MyApp.API.sln --filter "Category!=Integration"
```

## PostgreSQL integration tests

Do **not** use the deployed database. The connection must point to a
disposable PostgreSQL database whose name ends in `_test`, e.g. `myapp_test`.
Tests create random schemas and drop them afterward.

```bash
docker run --rm --name myapp-integration-pg \
  -e POSTGRES_USER=postgres -e POSTGRES_PASSWORD=postgres \
  -e POSTGRES_DB=myapp_test -p 5432:5432 postgres:17
```

In another terminal:

```bash
export MYAPP_TEST_POSTGRES="Host=localhost;Port=5432;Database=myapp_test;Username=postgres;Password=postgres"
dotnet test src/MyApp.Infrastructure.Tests/MyApp.Infrastructure.Tests.csproj --filter "Category=Integration"
```

GitHub Actions runs .NET 10 build, xUnit tests, PostgreSQL tests and
`npm ci && npm run build` for the frontend.

**Limit:** `RequirementPostgreSqlDatabase` uses a database-backed
`edit_csv_tab` test stub, not a physical CSV file writer.
See `docs/refactoring/stock-file-consistency.md` before deploying changes
that touch production stock files.

## Regression groups

- `NavigationPermissionsTests`: legacy auth/navigation permissions.
- `MaintenanceCurrentModelPostgreSqlTests`: template hierarchy and safe
  parallel sort allocation.
- `RequirementCurrentModelPostgreSqlTests`: authoritative stock, duplicate
  lines, transaction rollback and concurrent issue/restore. The stock-writer
  function is a **PostgreSQL test double**, not real filesystem I/O.
- `VehicleHoursCurrentModelPostgreSqlTests`: empty-cell carry forward,
  repeated rows, transaction rollback, concurrent import and **one group
  SELECT for many blank rows**.
- `VehiclePartsCurrentModelPostgreSqlTests`: create/edit/delete request
  functionality on the active EF Core model, including awaiting_parts and
  uniqueness rules.

`Category=Integration` tests run only against random schemas in a
disposable `*_test` database. They are not migration/production-data tests.
