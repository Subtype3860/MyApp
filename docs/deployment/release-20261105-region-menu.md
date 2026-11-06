# Release 20261105-region-menu

Site: https://boora.ru/ . Date: 2026-11-05 (Asia/Sakhalin).

The complete «Регионы / участки и закрепление техники» panel is now in the separate «Участок» menu, removed from Users and Transport. Administrators, Senior Mechanics and Chief Mechanics can manage the catalog, assignments, transfers, history and regional user grants. Role/profession checks use current database values for every management request. General work/report access remains region-scoped.

The menu receives minimal fleet and user identifiers from a dedicated management endpoint; it does not grant unrestricted work/media access or user-role editing.

Build source is the archived 20261105-fleet-regions release with only this change applied. Only MyApp.API.dll and its symbols replace server binaries; the existing self-contained .NET 10 runtime, application/infrastructure assemblies, configuration and user media are preserved. Frontend is rebuilt from this isolated source.

Verification: 75 backend cases passed through direct invocation of xUnit assertions; 15 browser checks passed, covering all three management categories, ordinary-user exclusion, transfers, regional grants, responsive layout and removal from former sections. Deployment additionally requires startup and role checks against an isolated restored production database.

Release/source archive: `/var/lib/myapp-releases/20261105-region-menu`.
Backup: `/var/backups/myapp/release-20261105-region-menu`.
Previous application: `/opt/myapp/api.previous-20261105-region-menu`.
Previous frontend: `/opt/myapp/www.previous-20261105-region-menu`.
No new schema migration in this release. File rollback must preserve new staged media; database restoration requires separate consideration of subsequent writes.

Publication verified: API and nginx active; public frontend HTTP 200; protected APIs HTTP 401; login validation HTTP 400. Published frontend HTML and application file hashes match the release. Isolated restored-database checks passed for administrators, senior mechanics and chief mechanics, with ordinary mechanics denied.
