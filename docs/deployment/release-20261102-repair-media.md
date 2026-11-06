# Production release 20261102-repair-media

Site: https://boora.ru/
Deployment date: 2026-11-02 (Asia/Sakhalin).
Source: base commit `d92ac86` plus the repair media patch in the working checkout.
The exact patch and its SHA-256 are archived in the server release directory as `source.patch` and `release.json`.

## Changes

- Authors can upload photos/videos to their own repair stages regardless of profession name.
- Media permission denial returns HTTP 403 instead of expiring the browser session through HTTP 401.
- The repair form waits for image preparation and retries only remaining attachments using the saved stage ID.

## Verification

- Local frontend production build and 13 browser tests passed.
- 47 backend test cases passed through direct invocation of xUnit assertions; the standard Windows test host failed to monitor its parent process with access denied.
- Database backup restored into a disposable PostgreSQL pod with no external network. Current foreign-table contents were exported read-only from the database for the isolated CSV snapshot because the old CIFS paths were unavailable.
- Candidate initialization, authenticated reads, GTO, repair sync/idempotency, local advice and game authorization checks passed.
- An isolated synthetic employee with profession `Электрослесарь по ремонту оборудования` created a stage, uploaded photo/video, read the photo, and received both attachment records in the repair journal. Another user received HTTP 403 for photo/video upload. No production test records or grants were created.
- A fresh database dump was taken with the API stopped before cutover. Existing application configuration, media staging, downloads and old hashed assets were retained; nginx configuration was unchanged.
- After cutover API/nginx were active, public frontend HTTP 200, protected API HTTP 401, invalid login body HTTP 400. All release files passed SHA-256 verification. Public HTML and main JavaScript matched the local build byte-for-byte.
- No schema changes are introduced by this patch. The existing initializer was tested on the restored database before activation.

## Recovery

- Release: `/var/lib/myapp-releases/20261102-repair-media`.
- Backup: `/var/backups/myapp/release-20261102-repair-media` (application archive, database dump, pre-cutover dump and verification logs).
- Previous API: `/opt/myapp/api.previous-20261102-repair-media`.
- Previous frontend: `/opt/myapp/www.previous-20261102-repair-media`.
- Cutover script includes automatic rollback on failed checks. A later manual rollback must preserve any new media-staging files. Database restoration is separate and must account for later writes.

API archive SHA-256: `4088c19461ad1260b227960a5688bdc90c9e48d0c4588d420eb3512d941c61b3`.
Frontend archive SHA-256: `10504e14c72970dcbab42b7156557425e2dede0b2347477a36e95d16ba2209aa`.
