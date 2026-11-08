# Release 20261107-repair-numbers

Published to https://boora.ru/ on 2026-11-07 (Asia/Sakhalin).

The repair request form now displays separate read-only garage and state registration numbers from the selected vehicle. Missing values display «Не указан»; garage number zero is preserved. Search continues to accept either number.

Compared frontend sources with the archived 20261107-maintenance-cards release: only VehicleJournalView.vue changed. Production build passed. Frontend HTML and referenced asset hashes verified after activation; myapp/nginx remain active and protected APIs return 401. API binaries and database were unchanged.

Release package, deployment script and changed source: `/var/lib/myapp-releases/20261107-repair-numbers`. Previous frontend retained at `/opt/myapp/www.previous-20261107-repair-numbers`; existing downloads and hashed assets were preserved. Deployment automatically restores the previous frontend if activation checks fail.
