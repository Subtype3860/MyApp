# Release 20261108-card-time

Published to https://boora.ru/ on 2026-11-08 (Asia/Sakhalin).

Fixed maintenance-card datetime entry: native input events now update valid timestamps, and autosave waits until the user leaves the datetime field. Incomplete native datetime input does not overwrite the saved timestamp; invalid input blocks saving with an explicit message. This applies to equipment stop, work start, work finish and return to operation. Card errors are displayed beside the save/submit controls.

Diagnosis on the existing card was read-only: all operations and actual material quantities were filled, while FinishedAt remained null. No timestamps or maintenance results were changed directly in the database.

Production build and three browser cases passed, including editing all four timestamps across an autosave delay on mobile/desktop and subsequent submission/acceptance. Only MaintenanceCards.vue changed relative to the previous published frontend sources. Deployed HTML/assets match package hashes, API hash is unchanged, services are active and protected API requests return 401.

Release: `/var/lib/myapp-releases/20261108-card-time`. Previous frontend: `/opt/myapp/www.previous-20261108-card-time`. Downloads and existing hashed assets retained; automatic file rollback configured for failed activation checks. Backend and database unchanged.
