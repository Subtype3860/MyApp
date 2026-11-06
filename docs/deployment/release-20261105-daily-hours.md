# Production release 20261105-daily-hours

Site: https://boora.ru/
Deployment date: 2026-11-05 (Asia/Sakhalin).

Fetched origin before publication. `refactor/integration-auth-shell` remains at `d92ac867c5682c0d9c6700345ee8c1b9ae373967`. Production also includes the later repair-media, shift-report and public-report-media changes. Release source reconstructs those published changes from their archived sources, then applies the daily-hours change. Unrelated working-checkout changes are excluded.

## Changes

- Daily Excel import synchronizes two engine counters using their larger increment over the same interval, retaining separate cumulative baselines.
- Above 24 hours per elapsed day, both increments use an estimated 22 hours per elapsed day. Exactly 24 remains 24.
- Preview and saved notes describe corrected inputs and estimates. Existing conflicting records are not overwritten.
- Only `MyApp.Application.dll` and its symbols are replaced in the live API baseline; the current API, infrastructure, configuration and media features are preserved. Frontend is rebuilt with the updated import explanation and the latest deployed report sources.

## Verification

- 40 backend test cases passed via direct invocation of xUnit assertions; Windows testhost cannot monitor its parent process (access denied).
- 11 browser tests passed for daily import, engine hours and shift reports/PDFs. One optional supplied-workbook test was skipped because no sample file was provided.
- Production database backup restored in a disposable pod without external network access. The candidate passed startup, authenticated reads, GTO conflict checks, repair synchronization, local advice, games and public report media checks.
- On that isolated database, truck 911 with baseline `20351/14896` and input `20374/4520` produced and committed `20374/14919`. A subsequent excessive increment produced `20396/14941` using 22 hours. Reimport was unchanged.
- Database and application backups were taken; another database dump was taken with the service stopped before cutover. No production test records or historical reading corrections were made.
- After activation, API/nginx active, frontend HTTP 200, protected APIs HTTP 401, invalid login body HTTP 400. Release file checksums and public frontend HTML matched. Public game authorization/WebSocket checks passed.

## Recovery

- Release and exact source archive: `/var/lib/myapp-releases/20261105-daily-hours` (`source.tar.gz`, `release.json`, `SOURCE.sha256`, `RELEASE.sha256`).
- Backup: `/var/backups/myapp/release-20261105-daily-hours`.
- Previous API: `/opt/myapp/api.previous-20261105-daily-hours`.
- Previous frontend: `/opt/myapp/www.previous-20261105-daily-hours`.
- Cutover script rolls back application files if checks fail. Later rollback must preserve newly staged media. Database restoration is separate and must account for subsequent writes.

Published Application DLL SHA-256: `20e1e0787bc028edc76fee5c6ec93443c712b9721ae4404ada983865fb7e5555`.
