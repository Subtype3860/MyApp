#!/usr/bin/env bash
# Read-only Fedora deployment reconnaissance. Never prints environment secrets.
set -uo pipefail
workdir="${1:-/opt/myapp}"
template="${2:-/mnt/dietpi/data/t.pdf}"

section() { printf '\n===== %s =====\n' "$1"; }
section "System"
if [[ -f /etc/os-release ]]; then
  grep -E '^(NAME|VERSION_ID|PRETTY_NAME)=' /etc/os-release || true
fi
uname -m
id -un
if command -v getenforce >/dev/null 2>&1; then
  printf 'SELinux: '; getenforce || true
fi

section "Runtime versions"
for bin in git dotnet node npm nginx httpd caddy; do
  if command -v "$bin" >/dev/null 2>&1; then
    case "$bin" in
      git) git --version ;;
      dotnet) dotnet --version; dotnet --list-runtimes ;;
      node|npm) "$bin" --version ;;
      *) printf '%s: installed\n' "$bin" ;;
    esac
  fi
done

section "Working copy (no remote URLs or credentials)"
if [[ -d "$workdir" ]]; then
  ls -ld "$workdir"
  if git -C "$workdir" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
    git -C "$workdir" status --short
    git -C "$workdir" branch --show-current
    git -C "$workdir" rev-parse --short HEAD
  else
    printf 'Directory exists; not a git worktree\n'
  fi
  for candidate in "$workdir/frontend" "$workdir/src/MyApp.API" "$workdir/publish" "$workdir/dist"; do
    [[ ! -e "$candidate" ]] || ls -ld "$candidate"
  done
else
  printf 'Working copy not found: %s\n' "$workdir"
fi

section "System services (unit names only)"
if command -v systemctl >/dev/null 2>&1; then
  systemctl list-units --type=service --all --no-pager --no-legend 2>/dev/null |
    grep -Ei 'myapp|nginx|httpd|apache|caddy|dotnet|kestrel' || true
fi
section "Listening web ports"
if command -v ss >/dev/null 2>&1; then
  ss -ltn 2>/dev/null | grep -E '(:80|:443|:5296|:5150)[[:space:]]' || true
fi

section "Network mounts (source, target, filesystem only)"
if command -v findmnt >/dev/null 2>&1; then
  for candidate in "$workdir" /mnt/dietpi /mnt/dietpi/data /mnt/dietpi/img /mnt/dietpi/video; do
    if [[ -e "$candidate" ]]; then
      printf '%s: ' "$candidate"
      findmnt --noheadings -T "$candidate" -o SOURCE,TARGET,FSTYPE 2>/dev/null || true
    else
      printf 'Missing: %s\n' "$candidate"
    fi
  done
fi

section "PDF template metadata (no file contents)"
if [[ -f "$template" ]]; then
  ls -l "$template"
  if [[ -r "$template" ]]; then printf 'Readable by current user: yes\n'
  else printf 'Readable by current user: no\n'; fi
  if command -v file >/dev/null 2>&1; then file --brief --mime-type "$template" || true; fi
else
  printf 'Missing or not regular file: %s\n' "$template"
fi

section "Free disk space"
if command -v df >/dev/null 2>&1; then
  df -h "$workdir" 2>/dev/null || true
fi
printf '\nRead-only preflight complete. Redact any private paths/hosts before sharing.\n'
