# Safe initial deployment of MyApp on Fedora

Target: the existing installation under `/opt/myapp/` on the Fedora server.
Source of the **candidate** release:
`refactor/integration-auth-shell` (draft PR #7 against
`subtype3860-auth-navigation-shell`).

**This document is a staging plan, not authorization to overwrite the
production installation.** There is no direct SSH execution in this chat.
Avoid posting passwords, private SSH keys, JWT secrets or DB credentials.

## Phase A — inspect the live system, read-only

Use a terminal on the Fedora server to obtain the repo file and review it
before running:

```bash
curl -fsSLo /tmp/myapp-preflight-fedora.sh \
  https://raw.githubusercontent.com/Subtype3860/MyApp/refactor/integration-auth-shell/scripts/deploy/preflight-fedora.sh
sed -n '1,220p' /tmp/myapp-preflight-fedora.sh
bash /tmp/myapp-preflight-fedora.sh /opt/myapp /mnt/dietpi/data/t.pdf
```

The script **does not** display service environment variables or `git
remote` URLs because these can expose authentication secrets. Please redact
any identifying host or private paths before sharing results.

Determine:
- systemd service name, its working directory, user and runtime (not secret
  environment values);
- nginx/httpd/Caddy site root and reverse-proxy routing for `/api`;
- installed `dotnet` 10 runtime and available disk space;
- current Git branch/commit, uncommitted files in `/opt/myapp`;
- actual mount point, protocol and availability of the remote file share;
- configured PDF-template path and whether the runtime user can read it;
- whether SELinux enforces additional restrictions.

**Do not run `git reset --hard`, `git clean -fd`, `dotnet ef database
update`, unreviewed `systemctl restart` or copy/delete operations on the
mounted share.**

## Phase B — assemble immutable release candidates in CI

On each push to the integration branch,
`.github/workflows/integration-ci.yml` now builds:
- `myapp-api-net10.tar.gz`: framework-dependent .NET 10 published DLLs;
  excludes `appsettings*.json` so production settings are NOT replaced.
  Requires ASP.NET Core runtime 10.x on the server.
- `myapp-frontend.tar.gz`: static assets from Vite `dist/`.
- `.sha256` checksum files for the two archives.

Find the archives in the successful GitHub Actions run's **Artifacts**.
They are retained for 14 days and include the commit SHA in their artifact
names. The archives are *not* deployed by the CI workflow.

## Phase C — deploy only after confirming the server layout

1. Obtain and verify backups **before** starting the new API, including
   a verified PostgreSQL dump and a safe snapshot of the current application
   code, runtime configuration and web assets.
2. Back up files on the network share only through its supported backup
   procedure; verify mounts without changing them.
3. Prepare a **new versioned release directory** outside the running
   directory. Verify artifact SHA-256 checksums. Preserve service/secret
   configurations outside the release.
4. Inspect `DatabaseInitializer` before changing the live runtime: the
   ASP.NET Core application calls `InitializeDatabaseAsync()` **on every
   startup**, which can update the database schema. Test first on an
   isolated copy of the real DB.
5. Confirm the backend service and nginx/html root. Plan a switch back to
   the prior binaries and frontend, with explicit rollback steps.
6. After user authorization, activate binaries/assets in a maintenance
   window and verify TLS, login, `/api`, permissions, media streaming,
   repair, PDF, and stock journaling.

Never assume `/opt/myapp` is safe to replace or that `master` is the
deployment branch. Never build or deploy code by pasting passwords into
shell commands.

## PDF issue found during diagnosis

The frontend calls `GET /api/documents/components/template`. The
Controller resolves `DocumentTemplates:ComponentIssuePath`; the source
configuration currently specifies `/mnt/dietpi/data/t.pdf`. No PDF
template is stored in GitHub. If that file is missing, not mounted, invalid
or inaccessible, the template endpoint cannot return a PDF. After
deployment, confirm the path on Fedora is correct for the remote share.
The endpoint has been enhanced to identify missing, unreadable and invalid
files; this code change has not yet been deployed to the Fedora host.
