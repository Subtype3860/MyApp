# Latest production deployment

Site: https://boora.ru/
Release: `20261013-011851`
Application source: `9df62b36a7a9287fe77e4d3a66f672616b42e8b8`, branch `refactor/integration-auth-shell`.

The deployment uses a self-contained .NET 10 linux-x64 API and Vite static assets.
Live paths remain `/opt/myapp/api` and `/opt/myapp/www`.
The existing `appsettings*.json`, `/etc/myapp/myapp.env`, template path and media paths were preserved.
Local development configuration and local media-staging files were excluded from the release.
Production staging files were copied after stopping the old service.

## Changes

CSV / XLSX imports accept arbitrary filenames and an explicit source; XLSX supports sheet selection. Preview validates the complete file before confirmation. Stock files replace the selected source's balances, zeroing absent positions atomically. Workers are added or updated without removing absent workers. Stock positions and workers have UUID primary keys; worker full names are computed from name components. Source metadata is stored in `app_import_sources`, import statistics in `app_import_history`, and completed migrations in `app_schema_migrations`.

JWT validation now checks the user's session stamp. Sessions issued by the previous release require a new login. Password changes and explicit logout revoke sessions on the server.

Existing documents and material groups still store names; migrating these references to UUID foreign keys is separate work.

## Recovery artifacts on the server

- Release candidate: `/var/lib/myapp-releases/20261013-011851`.
- Per-file checksums: `API-FILES.sha256`, `WWW-FILES.sha256`, `METADATA.sha256`.
- Complete archives: `api.tar.gz`, `www.tar.gz`, `SHA256SUMS`, `release.json`.
- Backup directory: `/var/backups/myapp/release-20261013-011851` (root access only).
- Database snapshot used for rehearsal: `job.dump`.
- Database snapshot taken with the service stopped, immediately before switching: `pre-cutover.dump`.
- Previous API: `/opt/myapp/api.previous-20261013-011851`.
- Previous frontend: `/opt/myapp/www.previous-20261013-011851`.
- Application/configuration archive: `application.tar.gz`.
- Isolated verification log: `isolation.log` in the backup directory.

Release files were transferred into separate directories using rsync with checksum comparison and copy-dest. Every deployed file matched the local build manifest. Complete release archives were then created and checksummed on the server.

The cutover script automatically restored prior binaries if activation checks failed.
For any later rollback, first assess database compatibility and new writes: restoring a pre-cutover dump would discard subsequent changes. Do not restore the dump automatically.

## Verification

- 103 unit tests and 38 PostgreSQL integration tests passed locally against an isolated disposable test database.
- 89 browser scenarios verified; two outdated scenarios were adjusted for the fleet action menu and required work location, then their 13-test groups passed.
- GitHub Actions for the application commit completed successfully.
- Release file checksums and PostgreSQL dump catalogs verified.
- Fresh database snapshot restored into an isolated PostgreSQL container with no external network.
- New API initialization passed against the restored database.
- Authenticated isolated vehicle list, repair journal, stock/worker table APIs and stock/worker exports returned 200.
- Production service is active; public HTTPS frontend returned 200 and its script matches the build.
- Public login page rendered in Chromium with zero JavaScript errors.
- Production login route returned 400 for an empty request (expected validation).
- Protected production repair route returned 401 without credentials (expected).
- Production user credentials were not used for the smoke checks.
