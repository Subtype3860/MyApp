# Release 20261105-fleet-regions

Site: https://boora.ru/ . Date: 2026-11-05 (Asia/Sakhalin).

## Changes

- Region assignment, transfer history and current-region access for vehicles and related records.
- A separate «Участки» menu for administrators and the «Главный механик» profession; the user administration panel no longer shows the region catalog.
- Forecast organisation comes from the selected region. Vehicle rows, snapshots, revision checks and browser drafts are separated by region/date/shift. The API rejects another region's vehicles. Legacy global snapshots are retained as filtered fallback views.
- Public signed links to individual report attachments remain usable; issuing new links still checks region access.
- Region form layout accounts for expanded/collapsed navigation. Image export uses the PDF.js compatibility build.

## Verification and deployment

- 74 backend cases passed by invoking xUnit assertions directly (the Windows testhost has parent-process access restrictions).
- Region menu/access/transfer checks passed at mobile and desktop widths. Forecast switching, autosave, history and PDF/Excel/image exports passed. A PDF text-extraction check was corrected to request UTF-8 explicitly and passed.
- A production dump was restored to an isolated PostgreSQL pod without external network access. Candidate startup, schema upgrade, vehicle history, independent regional reports, canonical organisation, foreign-vehicle rejection and revision conflicts passed.
- The initial framework-dependent package failed to locate a shared runtime. Application files were automatically rolled back; no schema migration ran during that failed startup. The final package is self-contained for linux-x64, preserving the existing deployment model. It was rechecked against the isolated dump.
- Production backup: `/var/backups/myapp/release-20261105-fleet-regions`. Release files and source: `/var/lib/myapp-releases/20261105-fleet-regions`.
- The production baseline was the published daily-hours release. Unrelated local media retention/replication changes were excluded from the release source.

## Recovery

Previous application directories are `/opt/myapp/api.previous-20261105-fleet-regions` and `/opt/myapp/www.previous-20261105-fleet-regions`. Backups include application files and database dumps before each cutover. A database rollback must account for writes after publication; old application code does not understand multiple regional forecasts for the same date/shift. Preserve staged user media when restoring files.

Final verification: API and nginx active; public frontend HTTP 200; protected APIs HTTP 401; login validation HTTP 400; deployed file hashes and frontend HTML match. Database has 1 region, 40 vehicles and 40 current assignments. The region column in GTO reports is present.
