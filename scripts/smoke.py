#!/usr/bin/env python3
"""Read-only World Alerts smoke test. Wait for container readiness; never sends notifications."""
import json
import os
import sys
import time
from urllib import request, error

BASE = (sys.argv[1] if len(sys.argv) > 1 else 'http://127.0.0.1:8080').rstrip('/')
FAIL = False


def get(path):
    try:
        with request.urlopen(BASE + path, timeout=5) as response:
            return response.status, response.headers.get_content_type(), response.read().decode('utf-8')
    except error.HTTPError as exc:
        return exc.code, exc.headers.get_content_type(), exc.read().decode('utf-8', errors='replace')
    except Exception as exc:
        return None, '', f'{type(exc).__name__}: {exc}'


def is_healthy(status, body):
    if status != 200:
        return False
    try:
        return json.loads(body).get('status') == 'ok'
    except (ValueError, AttributeError):
        return False


def await_health():
    # The launcher starts Docker in detached mode, BEFORE ASP.NET necessarily binds the port.
    # Always wait for an actual healthy response rather than treating docker-run success as readiness.
    try:
        wait_seconds = max(0.0, min(180.0, float(os.environ.get('WORLDALERTS_SMOKE_WAIT_SECONDS', '45'))))
    except ValueError:
        wait_seconds = 45.0
    deadline = time.monotonic() + wait_seconds
    attempts = 0
    while True:
        attempts += 1
        status, mime, body = get('/api/health')
        if is_healthy(status, body):
            if attempts > 1:
                print(f'API ready after {attempts} probes.')
            return status, mime, body
        # A different app or a wrong URL is not a transient startup condition.
        if status not in (None, 503):
            return status, mime, body
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            return status, mime, body
        time.sleep(min(1.0, remaining))


def check(label, good, message):
    global FAIL
    print(f"{'OK ' if good else 'FAIL'} {label}: {message}")
    FAIL |= not good


status, _, body = await_health()
good = is_healthy(status, body)
check('API/SQLite', good, f'HTTP {status}')
if not good:
    print(f'  Detail: {body[:600]}')
    print('  Container diagnostics (do not post secrets):')
    print('    docker ps -a --filter name=worldalerts-local')
    print('    docker logs --tail 100 worldalerts-local')
    print(f'    curl -v {BASE}/api/health')
    print('  The other HTTP checks are skipped while the API is unavailable.')
    sys.exit(1)

for path in ('/', '/subscriptions', '/admin'):
    status, mime, body = get(path)
    check(f'Angular route {path}', status == 200 and mime == 'text/html' and '<app-root' in body,
          f'HTTP {status}; content-type={mime}' + (f'; detail={body[:160]}' if status is None else ''))

status, mime, body = get('/api/news/important')
try:
    feed = json.loads(body)
    good = status == 200 and isinstance(feed.get('items'), list) and isinstance(feed.get('familyDiagnostics'), list)
except (ValueError, AttributeError):
    feed = {}
    good = False
check('Important News API', good, f'HTTP {status}; real-time results vary')
if good:
    items = feed['items']
    for family in ('News', 'Disaster', 'Market'):
        shown = sum(1 for e in items if e.get('family') == family)
        diagnosis = next((x for x in feed['familyDiagnostics'] if x.get('family') == family), {})
        print(f"  {family}: shown={shown}; important+eligible={diagnosis.get('importantEligibleCount', '?')}; "
              f"evaluated={diagnosis.get('evaluatedCount', '?')}")
    print(f"  Missing baseline={feed.get('missingMarketBaselineCount', '?')}; "
          f"rejected omitted={feed.get('omittedRejectedCount', '?')}")

status, mime, body = get('/api/alert-settings/types')
try:
    prefs = json.loads(body)
    good = status == 200 and isinstance(prefs.get('selectedTypes'), list)
except (ValueError, AttributeError):
    good = False
check('Event type preferences', good, f'HTTP {status}')

status, _, _ = get('/api/admin/status')
check('Admin access protection', status == 401, f'unauthenticated HTTP {status} (expected 401)')

print('\nThis smoke test does NOT send email/Slack, verify provider completeness, or exercise authentication.')
sys.exit(1 if FAIL else 0)
