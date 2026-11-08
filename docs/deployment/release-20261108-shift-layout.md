# Release 20261108-shift-layout

Published to https://boora.ru/ on 2026-11-08 (Asia/Sakhalin).

Shift report and PDF now order repair locations as КАРЬЕР, РММ, followed by additional works. Vehicle types are ordered excavators, bulldozers, dump trucks, then other/support vehicles; garage numbers retain numeric ordering. Maintenance without a stored work location remains a separate block before additional works.

Each work displays its description first in larger bold text, then all available photographs, then its performers in italics. PDF uses embedded bold/italic fonts and paginated image rows. Existing visibility preferences and media links remain supported.

Production build and 10 targeted report tests passed, covering ordering in preview/PDF, more than two photographs, mobile/desktop layout, maintenance and profession visibility. Deployed HTML and referenced asset hashes verified; API hash unchanged; services active and protected APIs return 401. Backend and database unchanged.

Release and changed source archive: `/var/lib/myapp-releases/20261108-shift-layout`. Previous frontend: `/opt/myapp/www.previous-20261108-shift-layout`. Existing downloads and hashed assets preserved; deployment includes automatic rollback on failed activation checks.
