# Release 20261105-fleet-site-filter

Published to https://boora.ru/ on 2026-11-05 (Asia/Sakhalin).

Fleet dashboard now filters vehicles by their current site assignment. The filter combines with search, resets pagination, and uses the existing scoped region and vehicle APIs. Administrators also have an unassigned option.

The release was built from the previous published frontend source with only HomeView.vue changed. Seven targeted Playwright cases passed, including mobile layout, site/search filtering, pagination and assignment lookup errors. Production build passed.

Deployment verified the previous index checksum before switching. Live HTML and referenced assets match the release. Backend DLL checksum stayed unchanged, myapp and nginx are active, and anonymous requests to vehicles and both region endpoints return 401. No database changes or backend restart were needed.

Release artifacts: `/var/lib/myapp-releases/20261105-fleet-site-filter`.
Previous frontend retained at `/opt/myapp/www.previous-20261105-fleet-site-filter`.
The deployment script automatically restores this directory if activation verification fails. For a later manual rollback, retain the current frontend under a new unused path and restore the previous directory to `/opt/myapp/www`, then verify HTML and assets over HTTPS. Backend remains release `20261105-region-menu`.
