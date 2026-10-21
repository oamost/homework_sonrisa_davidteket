#!/usr/bin/env python3
"""Secure, interactive admin-password creation/rotation for a Docker env file.

Existing SMTP, Slack, and unrelated settings are preserved byte-for-byte except
for the one admin hash line. No plaintext passwords are persisted or printed.
"""
import base64
import getpass
import hashlib
import os
from pathlib import Path
import stat
import sys
import tempfile

KEY = 'WorldAlerts__Admin__PasswordHash='


def main():
    target = Path(sys.argv[1]).expanduser().absolute()
    rotate = len(sys.argv) > 2 and sys.argv[2] == '--reset'
    if target.is_symlink():
        sys.exit('Refusing a symlink as a private env file.')
    if target.exists() and not rotate:
        sys.exit(f'Private configuration already exists: {target}; use admin-password to rotate only the admin hash.')
    if not sys.stdin.isatty():
        sys.exit('An interactive terminal is required; never enter a password in a command argument.')
    first = getpass.getpass('Choose an admin password (12-1024 characters): ')
    confirm = getpass.getpass('Repeat admin password: ')
    if not 12 <= len(first) <= 1024 or first != confirm or '\n' in first or '\r' in first:
        sys.exit('Passwords did not match or did not meet length requirements; configuration unchanged.')
    salt = os.urandom(32)
    derived = hashlib.pbkdf2_hmac('sha256', first.encode('utf-8'), salt, 310_000, dklen=32)
    password_hash = f'310000:{base64.b64encode(salt).decode()}:{base64.b64encode(derived).decode()}'
    if target.exists():
        if not target.is_file():
            sys.exit('Private configuration must be a regular file.')
        old = target.read_text(encoding='utf-8')
        lines = [line for line in old.splitlines(keepends=True) if not line.startswith(KEY)]
        contents = ''.join(lines)
        if contents and not contents.endswith(('\n', '\r')):
            contents += '\n'
        contents += KEY + password_hash + '\n'
    else:
        contents = '# Admin password HASH (PBKDF2), never plaintext. Do not commit.\n' + KEY + password_hash + '\n'
    target.parent.mkdir(parents=True, exist_ok=True)
    fd, tmp = tempfile.mkstemp(prefix='.worldalerts-', dir=str(target.parent), text=True)
    try:
        os.fchmod(fd, stat.S_IRUSR | stat.S_IWUSR)
        with os.fdopen(fd, 'w', encoding='utf-8') as dest:
            dest.write(contents)
            dest.flush()
            os.fsync(dest.fileno())
        os.replace(tmp, target)
    finally:
        if os.path.exists(tmp):
            os.unlink(tmp)
    print('Admin password hash saved safely. Run restart/up to reload the env file.')


if __name__ == '__main__':
    main()
