# Decision: task 01 acceptance boundaries

Date: 2026-10-21. Directive: the human reviewed and approved the agent-proposed defaults below for Deliverable 3 task 01 ("Settle blocking acceptance boundaries"). No implementation is approved by this record.

## Context

Task 01 required testable acceptance for importance scoring per family, the event timestamp used for 24-hour eligibility, catch-up pacing, retries, admin access, and what "new channel without production-code change" accepts, while keeping provider gaps explicit. The defaults were proposed by the agent and approved by the human on 2026-10-21. The testable cases are in `docs/evidence/task-01-example-cases.md`.

## D1 — Event-type taxonomy (subscribable codes)

Subscriptions use these codes only. "Verified source" reflects evidence recorded by tasks 05–11, not a promise of worldwide or 24-hour coverage.

| Code | Display name | Family | Verified source |
| --- | --- | --- | --- |
| `world-news` | World news | News | Yes (multiple tier-1 RSS feeds) |
| `conflict` | Armed conflict | News | Yes (news-topic coverage) |
| `politics` | Politics | News | Yes (news-topic coverage) |
| `diplomacy` | Diplomacy | News | Yes — two verified feeds (U.S. State Department, UN press) |
| `disaster.earthquake` | Earthquake | Disaster | Yes (GDACS) |
| `disaster.flood` | Flood | Disaster | Yes (GDACS) |
| `disaster.cyclone` | Cyclone | Disaster | Yes (GDACS/EONET) |
| `disaster.wildfire` | Wildfire | Disaster | Yes (GDACS/EONET) |
| `disaster.volcano` | Volcano | Disaster | Yes (GDACS/EONET) |
| `disaster.drought` | Drought | Disaster | Yes (GDACS) |
| `disaster.tsunami` | Tsunami | Disaster | Yes (flag on GDACS events; live tsunami-only feed unproven) |
| `disaster.landslide` | Landslide | Disaster | Yes (GDACS events; live landslide-only feed unproven) |
| `disaster.epidemic` | Epidemic | Disaster | Yes (IFRC) |
| `market.crypto` | Crypto markets | Market | Yes (Binance) |
| `market.fx` | FX markets | Market | Yes (Frankfurter/ECB) |
| `market.indices` | Equity indices | Market | Yes (Cboe `^SPX`, TWSE, MOEX — single-venue, delayed/EOD) |
| `health.humanitarian` | Humanitarian emergency | Disaster | Yes (IFRC severity) |
| `tech.cyber` | Cyber incidents | News | News-topic coverage |
| `tech.ai` | AI developments | News | News-topic coverage |
| `tech.acquisitions` | Major acquisitions | News | News-topic coverage |
| `tech.critical-infrastructure` | Critical infrastructure | News | News-topic coverage |
| `environment.energy` | Environment and energy | News | News-topic coverage |
| `science.transformative` | Transformative science | News | Yes (Nature); topic filter unproven |
| `economy.financial` | Economy and financial news | News | Yes (tier-1 world feeds; no dedicated financial feed) |

**Approved addition 2026-10-21 — `economy.financial`.** The human approved a separate canonical
code for economic and financial *news stories*, distinct from the `market.crypto`, `market.fx` and
`market.indices` price-observation codes. The code is assigned from item-specific metadata only:
a consequential financial event in the headline, or a provider topic tag that names a financial
subject (for example "Interest rates", "Inflation", "Australian economy", "Stock markets"). A
generic tag such as "Business" is not accepted, and a feed family alone never assigns the code.
The code marks the topic of a story; whether the story is *important* is decided by the rule below.

## D2 — Deterministic importance

Importance is rule-based, metadata-only, and applied per family. The rules and numeric values below are **approved acceptance examples, not universal weights**; they are not a claim about real-world editorial importance. Scoring tasks 14–16 must use them as test fixtures.

- **News:** important when a tier-1 global source reports a story with a breaking/conflict/disaster keyword, **or** when two independent tier-1 sources carry the same story within 6 hours. Not important: a single specialist item with no urgency keyword.
- **Disaster:** GDACS Orange/Red alert is always important. A Green earthquake at magnitude ≥ 6.0 is important. Any tsunami flag is important. An epidemic/humanitarian event with IFRC severity ≥ 2 is important. Other green/low-severity events are not important.
- **Markets:** important when the absolute move over the observation interval is ≥ 5% for crypto (24h interval), ≥ 1% for FX (daily), or ≥ 2% for indices (daily), **and** the observation is fresher than twice its interval. Otherwise not important.

### Approved addition 2026-10-21 — news financial event rule (`news.consequential-financial-event`)

A tier-1 source that reports a **consequential financial event** is important. The rule is
metadata-only, headline-based and deterministic. All of the following must hold:

1. **Trusted source.** The reporting source resolves to tier-1 in the approved tier map. The
   approved tier map is the only trust map today; an explicit trust list for official institutions
   (IMF, central banks) is not defined, so an official feed qualifies only when it is also a
   tier-1 source. A custom tier map can make another source trusted.
2. **Positive financial event subject**, one of: central bank, Federal Reserve, ECB, Bank of
   England, Bank of Japan, RBA, BOJ, BOE, IMF, World Bank, monetary policy, policy rate,
   interest rate, rate hike, rate cut, inflation, consumer prices, CPI, GDP, recession, jobs
   report, unemployment, payrolls, default, bank failure, bank collapse, insolvency, bankruptcy,
   bailout, trading halt, circuit breaker, suspended trading, IPO, initial public offering,
   acquisition, merger, takeover.
3. **Consequential action or material scale.**
   * *Action path* (subjects in D2 subject list except IPO/acquisition/merger/takeover): a
     decision or material-movement verb — raise(s|d|ing), cut(s|ting), hike(s|d|ing),
     lower(s|d|ing), slash(es|ed), double(s|d), triple(s|d), surge(s|d), soar(s|d),
     plunge(s|d), collapse(s|d), contract(s|ed), shrink(s), fail(s|ed), default(s|ed),
     halt(s|ed), suspend(s|ed), approve(s|d), agree(s|d), complete(s|d), announce(s|d),
     launch(es|d), bail out, rescue(s|d), seize(s|ed).
   * *Scale path* (all subjects, including IPO/acquisition/merger/takeover): a material scale
     marker — billion, trillion, biggest, largest, record(s). A capital-market subject therefore
     needs a quantified or superlative event; an ordinary small fundraising or an unquantified
     deal is never important on its own.
4. **No exclusion marker.** Forecasts, odds and speculation (forecast, predicts, expected to,
   odds, likely to, analysts, warns, outlook, projection, speculation, rumour, could, might, set
   to, poised to, in talks, weighing), routine market commentary (round up, markets today, stock
   market today, market wrap, market update, closing bell, opening bell, pre market, what to
   watch, what to know, watchlist, top stocks, stocks to watch, share price, stock price, live
   updates), personal finance and promotion (your money, personal finance, should you buy,
   investing in, how to save, retirement planning, credit score, best stocks to buy, for
   beginners, sponsored, promoted, advertisement, discount, coupon, giveaway, black friday, promo
   code) and opinion or analysis pieces (opinion, analysis, review, explainer, how to) are
   excluded even when a subject and an action are present.

**Graded strength.** 0.70 when the rule fires, plus 0.05 when the provider publication time is
usable, plus 0.05 when the headline carries an explicit material amount (billion, trillion).
A trusted-source financial decision is therefore graded at least as strong as the approved tier-1
urgency heuristic (0.70), and a routine financial story keeps the existing news score of 0. The
score is a metadata-strength ranking, not a probability; `IsImportant` stays a boolean rule
outcome, and no global score cutoff is introduced.

**Approved examples.**

| Headline | Source | Outcome |
| --- | --- | --- |
| "Reserve Bank of Australia raises interest rates by half a point to 4.1%" | tier-1 | important, 0.75, `economy.financial` |
| "Billionaire Mukesh Ambani launches India's biggest-ever IPO" | tier-1 | important, 0.75, `economy.financial` |
| "Elon Musk says SpaceX and Tesla exploring largest merger in history" | tier-1 | important, 0.75, `economy.financial` |
| "Central bank announces $500 billion emergency package for banks" | tier-1 | important, 0.80, `economy.financial` |
| "IMF approves $10 billion emergency financing programme for Egypt" | tier-1 | important, 0.80, `economy.financial` |
| "Reserve Bank of Australia raises interest rates by half a point to 4.1%" | specialist | not important (source is not trusted) |
| "Odds of fifth interest rate hike this year shorten as RBA deputy warns inflation outlook 'worsening'" | tier-1 | not important (forecast/odds excluded); still coded `economy.financial` by its provider topic tags |
| "Local startup announces $4 million IPO to fund expansion" | tier-1 | not important (no material scale) |
| "Analysis: Fed raises rates as inflation cools" | tier-1 | not important (analysis excluded) |
| "Central bank could cut rates next month, analysts say" | tier-1 | not important (conditional/forecast excluded) |
| "Your money: central bank raises rates, what it means for savers" | tier-1 | not important (personal finance excluded) |
| "Stock market today: central bank raises rates as investors sell" | tier-1 | not important (routine market commentary excluded) |

**Fixture inventory.** `NewsImportanceScorerTests.F1`–`F10` (rule, score, trust, exclusions),
`CoverageGapTests.Financial_news_code_is_assigned_only_for_consequential_financial_events`,
`CoverageGapTests.Provider_financial_topic_tags_assign_the_financial_code`,
`CoverageGapTests.A_generic_business_tag_does_not_assign_the_financial_code`,
`ImportantNewsTypeCoverageTests.Every_approved_event_type_code_renders_an_eligible_important_event`
(24 approved codes) and
`ImportantNewsEndpointTests.A_subscriber_to_economy_financial_matches_an_important_financial_story`.
The shared preview and notification-selection path use the same canonical code and the same score.

## D3 — 24-hour eligibility

Timestamp basis is event time → published time → fetched time, using the basis flag defined by task 04. An item is eligible when its published time falls within the previous 24 hours, even if the event itself is older. Items with no provider timestamps at all never count as new. Future timestamps are flagged and excluded. Items whose event is older than 24 hours but published within the window are delivered normally as late items.

## D4 — Catch-up pacing

On startup, allow one immediate burst of at most 50 notifications. After that burst, pace the remainder and steady-state notifications at no more than 10 notifications per minute. The startup burst is an explicit exception to the steady-state limit. Any remainder is persisted and queued rather than dropped. No quiet hours by default.

## D5 — Retries

Three attempts at 1, 5 and 15 minutes after the first failure, then the send is marked failed and surfaced in the admin view. No infinite retry. Restart must not duplicate an already-sent notification.

## D6 — Admin wall

Local password plus a session cookie. Contents: source health, poll status, delivery queue, and failed sends. Secrets are never displayed. Unauthorized access returns 401 or redirects to the login page.

## D7 — New channel without production code

A config-driven generic webhook: URL, method, headers, JSON body template, and test-send. Adding such a channel requires no code edit. Secret values are stored outside source control.

## Known provider gaps

Regional-only heat/cold coverage, no Africa-only feed, tsunami/landslide not proven live as standalone feeds, and the dropped WHO feed remain. These are documented limitations, not solved coverage, and must stay visible in user-facing documentation.

## Consequences

- Tasks 14–16 adopt these examples as named fixtures; changing a numeric example requires human approval but does not change the rule.
- Tasks 18–22 use D3–D5; the admin task uses D6; channel work uses D7.
- This record does not rewrite the frozen `ctx/goals/plan.md`.
- The approved `economy.financial` addition extends the D1 taxonomy and the news rule in this record only. `ctx/goals/plan.md` and the frozen task inventory are unchanged, and the task tracker structure is unchanged.

## Review log

- Defaults proposed by the agent as a bounded acceptance draft.
- Reviewed and approved by the human on 2026-10-21.
- Numeric example values are acceptance fixtures and may be revised by the human without changing the underlying rule.
- Closure review: the human directed correction of MK5 to important but ineligible at 37 hours, and approved a one-time startup burst of up to 50 as an exception to the subsequent 10-per-minute limit. Task 01 closes the acceptance decisions only; implementation and runtime verification remain with the corresponding implementation tasks.
- 2026-10-21: the human explicitly approved adding one separate macro/financial-news taxonomy category (`economy.financial`, display "Economy and financial news") and an additive news consequential-financial-event rule, with engineering criteria, positive and negative examples, a graded score above 0.70 for the important case, and no new global score cutoff. The human's separate proposal of a global "important > 0.70" threshold was **not** implemented in this slice.
