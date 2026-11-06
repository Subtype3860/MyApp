# Frontend release 20261026-brand-76d7e4a

Published to https://boora.ru/ on 2026-10-26 (Asia/Sakhalin).
Commit: `76d7e4ae42de8b89eb8651098b1dc623c9350a2e`.

Removed the duplicate Atlas text beside the shared logo. The expanded sidebar centers the logo at 70% of its available width; the collapsed sidebar uses the available compact width. The mobile header remains compact.

Local production build passed with the existing large-chunk warning. All 40 deployed files passed SHA-256 verification; public HTML and logo matched the candidate. API remained active, protected repair API returned HTTP 401, and APK download returned HTTP 200. Backend and database were unchanged.

Release: `/var/lib/myapp-releases/20261026-brand-76d7e4a`.
Backup: `/var/backups/myapp/release-20261026-brand-76d7e4a/frontend.tar.gz`.
Previous index: `/var/backups/myapp/release-20261026-brand-76d7e4a/index.html`.
To roll back, atomically restore the previous index; its hashed assets were retained.
