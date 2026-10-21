#!/usr/bin/env python3
"""Stop only a verified Angular dev server listening on :4200 for THIS repo.

Linux: /proc socket owner + cwd/commandline checks; never kill by port alone.
Windows: run.ps1 provides the equivalent using Get-NetTCPConnection/WMI.
"""
import os
from pathlib import Path
import signal
import sys
import time

PORT = 4200
STATE_LISTEN = "0A"


def listener_inodes(port=PORT, proc=Path("/proc")):
    found = set()
    for name in ("net/tcp", "net/tcp6"):
        try:
            lines = (proc / name).read_text().splitlines()[1:]
        except OSError:
            continue
        for line in lines:
            fields = line.split()
            if len(fields) > 9 and fields[3] == STATE_LISTEN:
                try:
                    if int(fields[1].rsplit(":", 1)[-1], 16) == port:
                        found.add(fields[9])
                except ValueError:
                    pass
    return found


def is_project_dev_server(pid, project, proc=Path("/proc")):
    """Require project path AND the Angular/Vite command (never kill other services)."""
    base = proc / str(pid)
    try:
        cwd = (base / "cwd").resolve(strict=True)
        command = (base / "cmdline").read_bytes().replace(b"\x00", b" ").decode(errors="replace")
    except (OSError, ValueError):
        return False
    project = project.resolve()
    try:
        cwd.relative_to(project)
    except ValueError:
        return False
    # Angular CLI uses a node process, sometimes wrapped in npm or npx.
    lower = command.lower()
    return any(token in lower for token in ("@angular/cli", "ng serve", "ng.js", "vite"))


def owned_listeners(project, port=PORT, proc=Path("/proc")):
    inodes = listener_inodes(port, proc)
    if not inodes:
        return [], False
    owned = []
    foreign = False
    for entry in proc.iterdir():
        if not entry.name.isdecimal():
            continue
        try:
            handles = entry / "fd"
            if not any(os.readlink(fd) in {f"socket:[{inode}]" for inode in inodes}
                       for fd in handles.iterdir()):
                continue
        except (PermissionError, FileNotFoundError, ProcessLookupError, NotADirectoryError):
            continue
        pid = int(entry.name)
        if is_project_dev_server(pid, project, proc):
            owned.append(pid)
        else:
            foreign = True
    return owned, foreign


def main():
    project = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else Path(__file__).resolve().parents[1]
    if not Path("/proc/net/tcp").exists():
        print("Local dev cleanup unavailable on this system; check port 4200 manually.")
        return 0
    owned, foreign = owned_listeners(project)
    if foreign:
        print("WARNING: port 4200 has a listener not identified as this project; leaving it running.", file=sys.stderr)
    for pid in owned:
        print(f"Stopping this project's old Angular dev server (PID {pid}, port {PORT}).")
        try:
            os.kill(pid, signal.SIGTERM)
        except ProcessLookupError:
            continue
        except PermissionError:
            print(f"No permission to stop PID {pid}; please stop it manually.", file=sys.stderr)
            return 1
    if owned:
        for _ in range(20):
            current, _ = owned_listeners(project)
            if not current:
                print("Old Angular :4200 server stopped.")
                break
            time.sleep(0.1)
        else:
            print("Old Angular server did not exit after SIGTERM; refusing unsafe force kill.", file=sys.stderr)
            return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
