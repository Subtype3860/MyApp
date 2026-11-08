# Release 20261107-maintenance-cards

Published to https://boora.ru/ on 2026-11-07 (Asia/Sakhalin).

The release includes electronic maintenance cards and corrected standard cycles, materials from Technical Maintenance, compact template editing, profession-based library access, maintenance with performer names in shift reports, administrator profession visibility settings, and an explicit choice to generate a requirement PDF without a responsible employee.

Built from working checkout based on `50bfe3b`, including the current tracked and untracked application changes. The exact source snapshot, package hashes and scripts are archived at `/var/lib/myapp-releases/20261107-maintenance-cards`. Full self-contained linux-x64 API and Vite frontend were deployed. Production configuration, staged media, downloads and old hashed frontend assets were preserved. Automatic media retention remains disabled by default, matching the prior production behavior; enabling deletion was not part of this release.

Validation: 63 selected backend cases passed by direct invocation of xUnit assertions, and 20 browser tests passed. The production database backup was restored inside a disposable network-isolated pod. Candidate startup/migration, region permissions and reports, template access and revision conflicts, maintenance report reads, PDF preview without an issuer, profession-policy updates/conflicts and stale-session rejection passed. All synthetic records and policy changes were confined to this disposable database.

The first isolated startup exposed restrictive permissions on extracted nested resource directories; directory permissions were corrected and the complete isolated check rerun successfully before cutover. No production restart occurred during that failed check.

Backups: `/var/backups/myapp/release-20261107-maintenance-cards`, including `job.dump`, the application/configuration archive and `pre-cutover.dump` taken with the API stopped. Previous application and frontend directories are `/opt/myapp/api.previous-20261107-maintenance-cards` and `/opt/myapp/www.previous-20261107-maintenance-cards`. File rollback must preserve subsequently written media; database restoration requires separate review of subsequent writes.

Activation checks passed: myapp and nginx active; frontend HTTP 200 and matching release HTML; protected API endpoints HTTP 401; login validation HTTP 400; deployed file hashes match; Android download and public games remain available, including the existing tanks WebSocket/auth checks.
