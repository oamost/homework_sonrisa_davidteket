# Eight-request alert setup — current implementation evidence

The approved design is `docs/superpowers/specs/2026-10-21-seamless-alert-setup-design.md`; implementation and human verification are **still in progress**. No real Slack or SMTP credentials were entered and no live notification was sent in these checks.

## Implemented and tested locally

- Feed: automatic 60-second visible-tab refresh; Debug checkbox hides counters, family diagnostics, coverage gaps, baselines and score details until requested; Debug-only TEST event button. `landing.spec.ts`: 9 passed.
- Alert setup: all 24 approved types selected for a new local profile; compact toggle tags and explicit save including empty selection; readable From/recipient email addresses and Slack label; masked SMTP password/Slack webhook; legacy subscriptions available under Advanced; parallel email/Slack/generic webhook setup. Focused Angular suites: landing 9, setup 11, Slack 2, generic webhook 1 passed at last focused runs.
- Backend: protected email recipient and Slack label, Slack route and worker dispatch, generic configurable protected HTTPS webhook with public-IP connection pinning and redirect refusal, saved type preference, and synthetic TEST event with CSRF/origin checks plus SQLite cooldown. Fresh full .NET suite: **565 passed, 0 failed**. Subsequent focused EmailDelivery and pipeline checks also passed; rerun once after remaining admin work before sign-off.
- Local dev service after backend relaunch: `/api/health` 200, unauthenticated `/api/admin/slack` 401, unauthenticated `/api/admin/webhooks` 401, `/subscriptions` 200; proxied preference GET returned 24 defaults and public test-token GET returned a token. Latest backend was relaunched again after the endpoint fixes and returned health 200, setup 200, token 200. These statuses are **not** live-send evidence.
- Frontend `npm test -- --watch=false` after testing the Debug-only public send interaction: **34 passed, 0 failed**; `npm run build` succeeded with an initial bundle budget warning (31.88 kB over the 500 kB configured warning limit).

## Pending before user testing

- Admin operations automatic refresh is assigned to a bounded worker; no completed edit has yet been reported. Verify visible-tab refresh and stale/logout states after integration.
- End-to-end browser checks need the final backend build relaunched and a visual review. Screenshots not captured.
- Human must privately enter SMTP account/app password, authorized Slack incoming webhook and selected destinations, then consent to real test sends and check inbox/Slack. Being signed in to providers is not configuration.
- Generic webhooks are configurable but adapters are still required for protocols other than the supported public HTTPS POST/PUT JSON form. Never use a private-network URL. Do not equate a mock success with arbitrary real-platform acceptance.
- Native Windows host and backup/restore round-trip remain unverified; earlier task 25 DONE status reflects the human's explicit instruction and does not erase those limits.

See `docs/alert-setup.md` for private setup steps and `docs/commit-message-draft.md` for an uncommitted, human-approval-dependent proposed message.
