# Running the MyApp backend tests

## Unit tests (no database required)

From the repository root:

```sh
dotnet test src/MyApp.API.sln --filter "Category!=Integration"
```

## PostgreSQL integration tests

Use a **disposable PostgreSQL 17 database**. The integration tests create random
schemas, populate temporary fixture tables and drop those schemas afterwards.
They **must not** be run against the deployed production database.

Example local development container:

```sh
docker run --rm --name myapp-postgres-tests \
  -e POSTGRES_USER=postgres \
  -e POSTGRES_PASSWORD=postgres \
  -e POSTGRES_DB=myapp_test \
  -p 5432:5432 postgres:17
```

In a second terminal:

```sh
export MYAPP_TEST_POSTGRES="Host=localhost;Port=5432;Database=myapp_test;Username=postgres;Password=postgres"
dotnet test src/MyApp.Infrastructure.Tests/MyApp.Infrastructure.Tests.csproj --filter "Category=Integration"
```

GitHub Actions already provisions the PostgreSQL container and runs both test
groups after building the .NET 10 solution.

The stock tests use a PostgreSQL-backed `edit_csv_tab` substitute. They do
**not** exercise the real server-side CSV file writer or prove rollback safety
of filesystem side effects. See
`docs/refactoring/stock-file-consistency.md` before deployment.
