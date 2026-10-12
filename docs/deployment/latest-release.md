# Latest production deployment

Site: https://boora.ru/
Release: `20261011-181824`
Source: current workspace, including uncommitted application changes.

The deployment uses a self-contained .NET 10 linux-x64 API and Vite static assets.
Live paths remain `/opt/myapp/api` and `/opt/myapp/www`.
The existing `appsettings*.json`, `/etc/myapp/myapp.env`, template path and media paths were preserved.
Local development configuration and `media-staging` files were excluded from the release archive.

## Recovery artifacts on the server

- Release candidate: `/var/lib/myapp-releases/20261011-181824`.
- Backup directory: `/var/backups/myapp/release-20261011-181824` (root access only).
- Database snapshot used for rehearsal: `job.dump`.
- Database snapshot taken with the service stopped, immediately before switching: `pre-cutover.dump`.
- Previous API: `/opt/myapp/api.previous-20261011-181824`.
- Previous frontend: `/opt/myapp/www.previous-20261011-181824`.
- Application/configuration archive: `application.tar.gz`.

The cutover script automatically restored prior binaries if activation checks failed.
For any later rollback, first assess database compatibility and new writes: restoring a pre-cutover dump would discard subsequent changes. Do not restore the dump automatically.

## Verification

- Archive checksums and PostgreSQL dump catalogs verified.
- Fresh database snapshot restored into an isolated PostgreSQL container.
- New API initialization/migrations passed against the restored database.
- Authenticated isolated `/api/vehicles` and `/api/vehicles/repair-journal` returned 200.
- Production service is active; public HTTPS frontend returned 200.
- Public login page rendered in Chromium with zero JavaScript errors.
- Production login route returned 400 for an empty request (expected validation).
- Protected production repair route returned 401 without credentials (expected).
- Production user credentials were not used for the smoke checks.
