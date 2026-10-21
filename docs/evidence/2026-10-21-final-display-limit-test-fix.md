# Final regression correction — 2026-10-21

## Observed failure
`ImportantNewsEndpointTests.Response_reports_important_stories_omitted_after_display_limit` expected 100 displayed and 5 omitted, but its fixture generated 105 items with the exact same headline (`Breaking conflict`). The recent important-feed behavior intentionally deduplicates by normalized headline and event family, so the original fixture no longer measures the 100-distinct-story display limit.

## Correction
- Generate 105 individually distinct, important headlines (`Breaking conflict report {i}`), keeping the expected 100 displayed / 5 omitted. All are inside the existing 24-hour test window.
- Add an independent end-to-end endpoint regression verifying two distinct source IDs with one repeated headline become one displayed story and zero omitted distinct stories.
- Replace xUnit `Assert.Single(items.Where(...))` with its predicate overload, removing xUnit2031 without weakening the assertion.

## Validation
Static comparison completed. Full .NET / Docker test execution must run in the recipient's environment: `./run.sh up` and `python3 scripts/smoke.py`. No test has been disabled, and production source has not been altered by this last correction.
