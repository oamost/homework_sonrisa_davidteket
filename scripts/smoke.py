#!/usr/bin/env python3
"""Read-only local smoke test. No notifications or test events are sent."""
import json
import sys
from urllib import request, error

BASE = (sys.argv[1] if len(sys.argv) > 1 else 'http://127.0.0.1:8080').rstrip('/')
FAIL = False


def get(path):
    try:
        with request.urlopen(BASE + path, timeout=15) as response:
            return response.status, response.headers.get_content_type(), response.read().decode('utf-8')
    except error.HTTPError as exc:
        return exc.code, exc.headers.get_content_type(), exc.read().decode('utf-8', errors='replace')
    except Exception as exc:
        return None, '', str(exc)


def check(label, good, message):
    global FAIL
    print(f"{'OK ' if good else 'FAIL'} {label}: {message}")
    FAIL |= not good


status, _, body = get('/api/health')
try:
    good = status == 200 and json.loads(body).get('status') == 'ok'
except (ValueError, AttributeError):
    good = False
check('API/SQLite', good, f'HTTP {status}')

for path in ('/', '/subscriptions', '/admin'):
    status, mime, body = get(path)
    check(f'Angular route {path}', status == 200 and mime == 'text/html' and '<app-root' in body,
          f'HTTP {status}; content-type={mime}')

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
