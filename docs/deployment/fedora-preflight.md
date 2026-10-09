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
- `myapp-api-linux-x64-selfcontained.tar.gz`: autonomous .NET 10
  `linux-x64` executable (`MyApp.API`) with the runtime included.
  It does not require a system `dotnet` command. The archive excludes
  `appsettings*.json` so deployment must preserve the *existing* live
  configuration. This is a multi-file self-contained deployment: extract
  the **complete archive**, not only the executable.
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

## Observations from read-only Fedora diagnostics (2026-10-09)

- Fedora Linux 41 x86_64, SELinux Enforcing, 30 GB available.
- `myapp.service` runs as `myapp:myapp`, working directory
  `/opt/myapp/api`; `/proc/<MainPID>/exe` resolves to
  `/opt/myapp/api/MyApp.API`.
- No `dotnet` binary was discovered in root's PATH. This is why the
  candidate package was changed to **self-contained `linux-x64`**;
  confirm the active binary's format before switching.
- nginx listens publicly on 80/443; backend listens on localhost:5296.
- The live frontend is in `/opt/myapp/www`, and multiple
  `api.backup-*` and `www.backup-*` directories exist.
- The app's process SELinux context is `unconfined_service_t`;
  the mounted network share is CIFS with `cifs_t`. The presented
  AVC events concerned `sshd-session` reading `localtime`, **not**
  MyApp opening the PDF.
- `/mnt/dietpi/data/t.pdf` is a regular PDF readable by Unix
  permissions for user `myapp`; a successful direct HTTP endpoint
  response and systemd runtime visibility are **not yet verified**.
- The source installation directory `/opt/myapp` is mode 777.
  Review ownership/permissions before deployment; do not alter it
  until exact service and asset locations are confirmed.

### Next read-only commands

These commands do not access credential files and do not restart services:

```bash
file /opt/myapp/api/MyApp.API
ls -ld /opt/myapp/api /opt/myapp/www
python3 - <<'PY'
import json
from pathlib import Path
p = Path('/opt/myapp/api/MyApp.API.runtimeconfig.json')
if p.exists():
    x = json.loads(p.read_text()).get('runtimeOptions', {})
    print('TFM:', x.get('tfm'))
    print('Framework:', x.get('framework'))
    print('Frameworks:', x.get('frameworks'))
    print('Included frameworks:', x.get('includedFrameworks'))
PY
systemctl show myapp.service -p ExecStart -p WorkingDirectory -p User
```

**Review the last line before sharing:** `ExecStart` may contain private
command-line arguments and must be redacted if it does.

Do not expose full systemd units or nginx configuration without reviewing
them for secrets. Do not treat repeated `sshd_t` AVCs as evidence of
`myapp.service` file access denial.

Before activating the new backend, identify whether the production
`DatabaseInitializer` will modify schemas, take a verified database backup
and decide on a maintenance/rollback window. The user must approve the
actual production switch separately.
