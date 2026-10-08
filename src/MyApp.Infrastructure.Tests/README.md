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
