# Latest production deployment

Site: https://boora.ru/
Release: `20261021-f24fb8e`
Deployment date: 2026-10-21 (Asia/Sakhalin).
Application source: `f24fb8e977970269f6f9234fb17eca762997d2ba`, branch `refactor/integration-auth-shell`.
Previous release: [20261013-011851](release-20261013-011851.md).

The release uses a self-contained .NET 10 linux-x64 API and Vite static assets.
Live paths remain `/opt/myapp/api` and `/opt/myapp/www`; the systemd service is `myapp`.
Existing `appsettings*.json`, `/etc/myapp/myapp.env`, media paths and template configuration were preserved.
Local development configuration and secrets were excluded from the release archive.

## Changes

- Merge Domain models into Application, leaving three application projects.
- Add Russian documentation comments to backend declarations and frontend named functions.
- Add daily Excel engine-hours preview and commit, with per-engine chronology checks and warning confirmation.
- Show model and garage number in repair history; reduce vertical spacing in cards, requests and work entries.
- Start the Reports submenu collapsed and update browser navigation scenarios.
- Preserve fixed-width engine-hours output in the GTO report.

Database initialization adds the nullable `reading_time` column to `vehicle_hour_readings`.
Existing and manually entered readings may retain an unknown time.

## Verification

- GitHub Actions for the application commit completed successfully, including backend build, unit tests, PostgreSQL integration tests and frontend build.
- 114 backend unit tests passed locally.
- 44 browser scenarios passed locally across imports, reports, repair history, participation and auxiliary work (including the corrected Windows fixture path).
- Two PDF text-extraction scenarios could not complete locally because `pdftotext` was unavailable; one workbook-fixture scenario was skipped because its optional source file was absent.
- Release archive and every deployed API/frontend file matched SHA-256 checksums.
- Database dump catalog verified; dump restored into an isolated PostgreSQL container without external networking.
- Candidate API initialized successfully against that copy; authenticated profile, vehicle list and repair-journal requests returned HTTP 200.
- The isolated database contains the expected `reading_time` column.
- After activation, systemd service is active; public frontend returns 200, protected repair API returns 401 without credentials, and empty login request returns 400.
- Public HTML matches the release's index file.

## Recovery artifacts on the server

- Candidate and manifests: `/var/lib/myapp-releases/20261021-f24fb8e`.
- Uploaded archive: `/var/tmp/myapp-release-f24fb8e.tar.gz`.
- Archive SHA-256: `376a095021923e9f222099ffffcedbfb2d1b97294e61b92955b3348d11627649`.
- Protected backups: `/var/backups/myapp/release-20261021-f24fb8e`.
- Database snapshots: `job.dump` for rehearsal and `pre-cutover.dump` taken with the API stopped.
- Application/configuration backup: `application.tar.gz`; service configuration: `myapp.service.txt`.
- Isolated verification log: `isolation.log` in the backup directory.
- Previous API: `/opt/myapp/api.previous-20261021-f24fb8e`.
- Previous frontend: `/opt/myapp/www.previous-20261021-f24fb8e`.

The activation script automatically restores previous binaries and assets if its checks fail.
It does not restore the database automatically. Assess new writes and compatibility before any database rollback;
restoring a pre-cutover dump would discard subsequent changes.
Temporary backup credentials were removed after successful activation.
