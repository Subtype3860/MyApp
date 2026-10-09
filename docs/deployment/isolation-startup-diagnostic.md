# Diagnosing new API startup on a restored PostgreSQL backup

**Only for the 2026-10-09 staging release.** The actual live API must remain
untouched while we investigate migration compatibility.

The previous one-off isolated test returned an empty HTTP response, then
printed only the *end* of the SQL batch executed by
`DatabaseInitializer.InitializeAsync` around `component_requirements`.
The SQL body alone does not identify the actual PostgreSQL error code.

Run on Fedora as an administrator after reviewing the script:

```bash
curl -fsSLo /tmp/diagnose-isolated-api.sh \
  https://raw.githubusercontent.com/Subtype3860/MyApp/refactor/integration-auth-shell/scripts/deploy/diagnose-isolated-api.sh
less /tmp/diagnose-isolated-api.sh
bash /tmp/diagnose-isolated-api.sh
```

Expected prerequisites:
- /var/backups/myapp/postgres-20261009-050613/job.dump
- /var/lib/myapp-releases/d9cb41b796930e9d885b7ac2e094f63554d60781/api/MyApp.API
- `localhost/myapp-postgres17-restore` image (PostgreSQL 17, plpython3u,
  ru_RU.UTF-8), already built for the restore test
- `mcr.microsoft.com/dotnet/aspnet:10.0` image, previously pulled

The diagnostic creates a **Podman pod with `--network none`**, a temporary
PostgreSQL 17, restores `job.dump`, and starts a copied release
binary against the disposable database using a random temporary JWT key.
It does not mount production media, config, CSV or the remote share. It
never points at the live PostgreSQL host. It removes all disposable resources
after the run and prints the **first exception summary** if startup fails.
Do not bypass the failed initialization or deploy a newer API to production
until the migration is fixed and retested on an isolated restored copy.

The response `HTTP/1.1 401 Unauthorized` to the unauthenticated PDF URL
indicates the middleware is listening, but does **not** prove every feature
or long-running background job works. Additional functional tests are needed.
