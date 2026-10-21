# All-type important-event feed coverage

Scope: the public important-events feed (`GET /api/news/important`), its shared selection
pipeline (`src/api/Selection/**`), and the landing page that renders it. Approved event-type
codes come from decision D1 in `docs/decisions/2026-10-21-task-01-acceptance-boundaries.md`.
No provider was probed, no polling was changed, no rule weight was invented, and no family was
given a minimum quota.

## 1. Method and evidence basis

* Live reply inspected on the running instance (`https://localhost:7021/api/news/important`,
  evaluation instant `2026-10-21T04:06:03Z`). The running instance is the **pre-change build**;
  the new diagnostic fields below require a rebuild and restart by the parent.
* The running API caps the rejected preview at 100 rows and reports `omittedRejectedCount`
  (1109 rows hidden), and the cap is filled with news first, so the live API alone cannot show
  why the market and disaster families are empty. The per-reason counts in section 3 were
  therefore produced by a read-only local replication of the production rules over the same
  persisted SQLite file (`file:…?mode=ro`, no writes).
* The replication is **cross-validated against the live API**: at the same evaluation instant
  it reproduces the live `familyDiagnostics` exactly (News 270/134/4, Disaster 903/358/3,
  Market 43/38/0, `missingMarketBaselineCount` 11).
* Fixtures in `src/tests/WorldAlerts.Api.Tests/ImportantNewsTypeCoverageTests.cs` are
  deterministic test fixtures built from provider-shaped metadata. They are never presented as
  live provider data.

## 2. Live feed state before this change

```
items 7   omitted 0   omittedRejected 1109   missingMarketBaselineCount 11
News     evaluated 270  eligible 134  important+eligible 4
Disaster evaluated 903  eligible 358  important+eligible 3
Market   evaluated  43  eligible  38  important+eligible 0
displayed codes: world-news(4)+conflict(4) | disaster.earthquake(3)+disaster.tsunami(2) | market.*(0)
```

Every displayed event carried its real rule id, factors, and a finite 0–1 score
(`news.tier-1-urgency-keyword` 0.75, `disaster.tsunami-flag` 0.90,
`disaster.earthquake-magnitude` 0.725). The feed is public and subscription-independent; the
preview pipeline is the same code path used for notification selection.

## 3. Per-reason counts (evaluation instant 2026-10-21T04:06:03Z)

| Family | Eligibility | Rule | Important | Count |
| --- | --- | --- | --- | --- |
| News | Eligible | `news.tier-1-urgency-keyword` | yes | 4 |
| News | Eligible | `news.no-approved-trigger` | no | 130 |
| News | OutsideWindow | `news.tier-1-urgency-keyword` | yes | 3 |
| News | OutsideWindow | `news.no-approved-trigger` | no | 133 |
| Disaster | Eligible | `disaster.tsunami-flag` | yes | 2 |
| Disaster | Eligible | `disaster.earthquake-magnitude` | yes | 1 |
| Disaster | Eligible | `disaster.no-approved-trigger` | no | 355 |
| Disaster | OutsideWindow | `disaster.gdacs-orange-red` | yes | 5 |
| Disaster | OutsideWindow | `disaster.earthquake-magnitude` | yes | 1 |
| Disaster | OutsideWindow | `disaster.tsunami-flag` | yes | 1 |
| Disaster | OutsideWindow | `disaster.no-approved-trigger` | no | 539 |
| Market | Eligible | `market.missing-baseline` | no | 35 |
| Market | Eligible | `market.invalid-interval` | no | 3 |
| Market | OutsideWindow | `market.missing-baseline` | no | 5 |

Reading of the counts:

* **Eligible but not important** (scored, rule simply did not match): News 130, Disaster 355,
  Market 0.
* **Eligible but unscorable** (rule could not be evaluated from provider data): Market 38
  (35 without a 24-hour comparison, 3 without a fixed interval). News and Disaster have no
  unscorable rule: every event reaches a decision.
* **Important but outside the 24-hour window** (would qualify if the provider time were current):
  7 disaster events, including all 5 GDACS Orange/Red events, plus 3 news stories.
* **Market below threshold: 0.** No market observation has ever been scored and rejected for a
  small move, because none could be scored at all. The distinction is now explicit in the
  response instead of being hidden behind one counter.

## 4. Approved code coverage (23 codes from decision D1)

`Rule` is the approved importance rule that can mark an event of that code important.
`Test` is the named test that proves an eligible, important event of that code renders with the
code on the public feed.

| Approved code | Source / classification path | Approved rule | Test | Remaining gap |
| --- | --- | --- | --- | --- |
| `world-news` | Any news item (always assigned) | `news.tier-1-urgency-keyword`, `news.tier-1-corroboration`, `news.consequential-topic` | `ImportantNewsTypeCoverageTests.Every_approved_event_type_code_renders_an_eligible_important_event` | live: only 4 eligible news items are important; 130 eligible news items match no approved rule |
| `conflict` | Headline subject (troops/armed forces/ceasefire/missile/airstrike/invasion/war) | `news.tier-1-urgency-keyword` or `news.consequential-topic` | same + `Approved_subject_headlines_keep_their_subscription_code` | live: only conflict-coded news is currently important; no data gap |
| `politics` | Headline subject (government/president/parliament/election) | `news.consequential-topic` | same | live: 0 eligible politics story matches subject+action |
| `diplomacy` | Headline subject (treaty/sanctions/summit/diplomatic/ceasefire) | `news.consequential-topic` | same | live: UN press feed carries 10 eligible diplomacy items, none with subject+action |
| `tech.cyber` | Headline subject (ransomware/cyberattack/data breach/hackers) | `news.consequential-topic` | same | live: configured `the-record` feed has 0 stored rows (never collected) |
| `tech.ai` | Headline subject (ai/ai model/ai system/artificial intelligence) | `news.consequential-topic` | same | live: configured `techcrunch-ai` feed has 0 stored rows |
| `tech.acquisitions` | Headline subject (acquisition/acquires/merger) | `news.consequential-topic` | same | live: 0 eligible acquisition story matches subject+action |
| `tech.critical-infrastructure` | Headline subject (power grid/water supply/telecom outage/telecom network/hospital network) | `news.consequential-topic` | same | live: 0 eligible infrastructure story matches subject+action |
| `environment.energy` | Headline subject (climate/renewable energy/oil spill/power plant/emissions/energy grid) | `news.consequential-topic` | same | live: configured `guardian-environment` feed has 0 stored rows |
| `science.transformative` | Headline subject (clinical trial/fusion reactor/scientific breakthrough) | `news.consequential-topic` | same | live: Nature feed carries 75 eligible items, none with subject+action |
| `disaster.earthquake` | Provider hazard label `earthquake` | `disaster.earthquake-magnitude` (Green, magnitude ≥ 6) | same + `Provider_hazard_labels_keep_their_approved_code` | live: 1 eligible important quake; 1 more is outside the window |
| `disaster.flood` | Provider hazard label `flood`, `Pluvial/Flash Flood` | `disaster.gdacs-orange-red` | same | live: 5 Orange/Red GDACS events exist but all are outside the 24-hour window |
| `disaster.cyclone` | Provider hazard label `cyclone`, `storm` + cyclone evidence in title | `disaster.gdacs-orange-red` | same | live: no Orange/Red cyclone inside the window |
| `disaster.wildfire` | Provider hazard label `wildfire`/`fire` | `disaster.gdacs-orange-red` | same | live: no Orange/Red wildfire inside the window |
| `disaster.volcano` | Provider hazard label `volcano`/`volcanoes`/`volcanic eruption`/`volcanic activity` | `disaster.gdacs-orange-red` | same | live: no Orange/Red volcano inside the window |
| `disaster.drought` | Provider hazard label `drought` | `disaster.gdacs-orange-red` | same | live: the 3 stored Orange drought events are from April–July 2026 |
| `disaster.tsunami` | Provider tsunami flag | `disaster.tsunami-flag` | same | live: 2 eligible, 1 outside the window |
| `disaster.landslide` | Provider hazard label `landslide`/`mass movement` | `disaster.gdacs-orange-red` | same | live: no Orange/Red landslide inside the window |
| `disaster.epidemic` | Provider hazard label `epidemic` | `disaster.ifrc-severity` (severity ≥ 2) | same | live: no IFRC event has severity ≥ 2 (max stored severity is 1) |
| `health.humanitarian` | Provider hazard label `humanitarian`/`complex emergency`/`population movement`/`mass removal` | `disaster.ifrc-severity` (severity ≥ 2) | same | live: no IFRC event has severity ≥ 2 |
| `market.crypto` | Market family, configured `binance-ticker` source | `market.move-threshold` (≥ 5 % over 24 h) | same + `Provider_rolling_24h_baseline_scales_a_real_move_despite_timestamp_jitter` | live: 0 important; 11 eligible observations without a 24-hour comparison |
| `market.fx` | Market family, configured `frankfurter` source | `market.move-threshold` (≥ 1 % daily) | same | live: 1 stored observation only, no prior comparable observation |
| `market.indices` | Market family, configured `cboe`/`twse`/`moex` sources | `market.move-threshold` (≥ 2 % daily) | same | live: 1 stored observation per index source; the prior trading day/session is not a true 24-hour comparison |
| `economy.financial` | News headline financial event (subject + consequential action or material scale) or a provider financial topic tag | `news.consequential-financial-event` (tier-1 source, 0.70 + 0.05 usable publication time + 0.05 explicit material amount) | `ImportantNewsTypeCoverageTests` + `NewsImportanceScorerTests.F1`–`F10` + `CoverageGapTests` + `ImportantNewsEndpointTests.A_subscriber_to_economy_financial_matches_an_important_financial_story` | live: 2 eligible financial events qualify; 1 further financial story is a forecast and stays not important; no dedicated financial feed is configured |

### Metadata that was being lost (fixed in this change)

`CanonicalEventTypes` mapped some provider labels and headline subjects to **no** approved code,
so the approved topic metadata disappeared even though the approved rule could promote the event:

* News headline subjects present in the approved consequential-topic rule but missing from the
  classifier: `troops`, `armed forces` (conflict), `ceasefire` (diplomacy), `hackers`
  (tech.cyber), `telecom network`, `hospital network` (tech.critical-infrastructure),
  `emissions`, `energy grid` (environment.energy), `ai system` (tech.ai).
* Disaster provider labels with an approved code but no mapping: `Pluvial/Flash Flood`
  (3 stored IFRC rows), `Volcanic Eruption` and `volcanic-activity` (2 rows),
  `mass-movement` (3 rows), `storm` with cyclone/hurricane/typhoon evidence in the title
  (2 rows), and IFRC `Other` rows whose title names population movement or mass removal
  (2 rows). 12 of 903 stored disaster rows carried a provable approved code and carried none.
* The landing page labelled any event without a type code as `world-news`, which is a news code
  and wrong for a disaster or market event. It now shows `no approved type code`.

No code outside the approved taxonomy was added, and no importance rule was changed. The single
approved addition in the follow-up slice is the `economy.financial` code and its
`news.consequential-financial-event` rule (section 5).

## 5. Market and financial news: the missing path, and the approved addition

The human reported "market related important news" as missing. The two things called "market" are
different objects, and the news one was missing for three independent reasons.

**The Market family is numeric only.** `market.crypto`, `market.fx` and `market.indices` are
subscribable codes for *price observations* produced by the Markets collector (Binance ticker,
Frankfurter/ECB reference rate, Cboe/TWSE/MOEX quotes). They are never produced from a news
article: `CanonicalEventTypes.News` has no `market.*` alias, so a financial article can only
carry `world-news` (plus a topic code when the headline carries an approved subject).

**Exact missing path, link by link:**

1. **Source (present, not a dedicated feed).** The configured tier-1 world feeds do carry
   financial stories. 13 eligible news items in the persisted data contain financial or
   macroeconomic vocabulary, including two unambiguous macroeconomic stories:
   * "Odds of fifth interest rate hike this year shorten as RBA deputy warns inflation outlook
     'worsening'" (The Guardian World, tier-1, published inside the 24-hour window);
   * "Billionaire Mukesh Ambani launches India's biggest-ever IPO" (The Guardian World).
   Trade-policy stories are present as well ("Trump sued over Canadian tariffs in latest legal
   challenge"). No dedicated financial or markets news feed is configured
   (`appsettings.json` lists 10 news feeds: 4 world, Ars Technica, Nature, UN press, The Record,
   TechCrunch AI, Guardian Environment).
2. **Taxonomy (missing before the approved addition).** Decision D1 had no code for a
   macroeconomic or financial *story*. The only `market.*` codes are price-move families, and
   reusing them for articles would reclassify articles as price moves and mix the News and
   Market families. The Guardian provider category tags ("Reserve Bank of Australia",
   "Interest rates", "Inflation", "Australian economy", "Business") resolved to no code at all.
3. **Rule (missing before the approved addition).** No approved importance rule could promote a
   macroeconomic story. The tier-1 urgency keyword list is conflict/disaster vocabulary only, and
   the approved consequential-topic subject+action table has no macroeconomic subject/action
   pair, so the RBA rate-hike story evaluated to `news.no-approved-trigger` and was not
   important. It was not ineligible for display for a data reason; it was rejected by the rule.

**Consequence before the approved addition:** a financial or macroeconomic story could never
appear as a market-news item under the previous approved taxonomy and rules. A baseline fix does
not change this: the Binance baseline work supplies a comparison price for numeric observations
and supplies no article.

**Minimal approved-taxonomy fit — approved by the human on 2026-10-21 and implemented.**

The human approved one separate canonical code for economic and financial **news stories**:
`economy.financial` (display "Economy and financial news", family News), distinct from the
`market.crypto`, `market.fx` and `market.indices` price-observation codes, together with an
additive news rule `news.consequential-financial-event`. The full criteria, the approved
examples and the fixture inventory are in
`docs/decisions/2026-10-21-task-01-acceptance-boundaries.md` (D1 table row and the D2 addition).
The frozen `ctx/goals/plan.md` and the task tracker structure were not changed, and no global
"important > 0.70" cutoff was introduced.

What the approved rule does, in short: a **tier-1** source that reports a headline with an
approved financial event subject **and** a consequential action or a material scale marker, with
no exclusion marker (forecast, odds, routine market commentary, personal finance, promotion,
opinion or analysis), is important. A capital-market subject (IPO, acquisition, merger,
takeover) additionally needs a quantified or superlative event, so an ordinary small fundraising
or an unquantified deal is never promoted. The score is 0.70, plus 0.05 for a usable provider
publication time, plus 0.05 for an explicit material amount; a routine financial story keeps the
existing news score of 0 and the existing `news.no-approved-trigger` rule.

**Live effect on the persisted data (evaluation instant 2026-10-21T04:20:00Z, 139 eligible news
items, verified by replicating the new rule over the same read-only database):**

| Live article | Source | Rule | Score | `economy.financial` |
| --- | --- | --- | --- | --- |
| "Billionaire Mukesh Ambani launches India's biggest-ever IPO" | The Guardian World (tier-1) | `news.consequential-financial-event` | 0.75 | yes (headline event + provider tags "IPOs", "Stock markets") |
| "Elon Musk says SpaceX and Tesla exploring largest merger in history" | Al Jazeera English (tier-1) | `news.consequential-financial-event` | 0.75 | yes (headline event + provider tag "Economy") |
| "Odds of fifth interest rate hike this year shorten as RBA deputy warns inflation outlook 'worsening'" | The Guardian World (tier-1) | `news.no-approved-trigger` (forecast/odds excluded) | 0 | yes (provider tags "Interest rates", "Inflation", "Australian economy") |
| "Musk confirms SpaceX and Tesla exploring merger" | Al Jazeera English (tier-1) | `news.no-approved-trigger` (no action, no scale) | 0 | yes (provider tag "Economy") |
| "Elon Musk says Tesla-SpaceX merger being considered" | BBC World (tier-1) | `news.no-approved-trigger` (no action, no scale) | 0 | no (no financial provider tag) |

The eligible important news count moves from 4 to 7 at that instant (5 tier-1 urgency stories,
2 financial events). No article was fabricated, no provider was probed, and no family quota was
introduced.

**Bounded source proposal (for the provider-probe owner, not probed here).** The tier-1 world
feeds already deliver macroeconomic stories, so the taxonomy is usable without a new feed. If a
dedicated feed is wanted later, candidates for a keyless financial/macro RSS feed include the
Guardian business feed, the ECB press-release feed, the IMF news feed, or a central-bank
statistics release feed. None was probed, none is verified, and the provider-research owner
(who also owns the blacklist) must validate them.

**What remains open.** The Market family still shows numeric price observations only, and a
market move still needs an exact 24-hour comparison; the live Binance move (~0.74 %) stays below
the approved 5 % threshold, so fixing the baseline does not create an important market event.

**What is implemented now without new codes or weights:** the path is reported explicitly. The
public response states that Market-family entries are numeric price observations and that no
approved code covers a financial or macroeconomic article, so such an article can only appear as
world news and only when another approved rule already made it important. Two regression tests
pin that behaviour:
`ImportantNewsEndpointTests.A_financial_news_story_is_never_reclassified_as_a_market_price_move`
(a tier-1 eligible macroeconomic story stays `world-news`, is not important, and never becomes
a `market.*` code) and
`ImportantNewsEndpointTests.A_financial_story_promoted_by_an_approved_rule_keeps_its_news_topic`
(a story the approved rule does promote keeps its own news topic and stays in the News family).

## 6. Data absence versus algorithm gap

| Observation | Classification | Evidence |
| --- | --- | --- |
| 130 eligible news items match no approved rule | Algorithm/taxonomy gap for macro stories, data gap for the rest | per-reason counts, section 3 |
| 5 GDACS Orange/Red events (cyclone, 3× drought) are outside the 24-hour window | Data absence (provider publication time), rule works | section 3, "OutsideWindow" rows |
| No IFRC event with severity ≥ 2 | Data absence | stored IFRC severity values are all < 2 |
| 11 eligible market observations without a 24-hour comparison | Data absence, now separable from "scored and rejected" | `market.missing-baseline` 35 + `market.invalid-interval` 3 eligible |
| `the-record`, `techcrunch-ai`, `guardian-environment` feeds have 0 stored rows | Data absence (never collected on this host) | source ids absent from `NewsItems` |
| 7 important events excluded only by the 24-hour window | Data absence | section 3 |
| No approved code or rule for a macroeconomic story | Algorithm/taxonomy gap | section 5 |

## 7. Files changed in this change

| File | Change |
| --- | --- |
| `src/api/Selection/CanonicalEventTypes.cs` | Additive classification: approved headline subjects the classifier dropped, provider hazard labels with an approved code, deterministic type ordering. No new code, no rule change. |
| `src/api/Selection/AlertSelectionService.cs` | Per-family diagnostics now separate eligible-but-not-important from eligible-but-unscorable and expose per-rule counts; provider rolling 24-hour baseline preferred over the exact stored history with the same validation; limitations list the empty-family reasons and the market/financial distinction. |
| `src/api/Selection/ImportantNewsService.cs` | Unchanged; it already forwards the family diagnostics, which now carry the new fields. |
| `src/web/src/app/landing.component.ts` / `.html` | Event-type coverage badges, correct `no approved type code` label, per-family not-important / unscorable counters. |
| `src/web/src/app/landing.spec.ts` | 3 new landing tests (type coverage, no false `world-news` label, below-threshold vs unscorable split). |
| `src/tests/WorldAlerts.Api.Tests/ImportantNewsTypeCoverageTests.cs` | New: one deterministic fixture per approved code (24), approved-subject classification, provider hazard label mapping. |
| `src/tests/WorldAlerts.Api.Tests/ImportantNewsEndpointTests.cs` | New: provider baseline with timestamp jitter, no-baseline row diagnostics, below-threshold move not counted as missing, invalid provider baselines rejected, financial story not reclassified as a market price move, `economy.financial` subscriber match, market family isolation. |
| `src/api/Scoring/NewsImportanceScorer.cs` | Approved addition: the `news.consequential-financial-event` rule, its subject/action/scale/exclusion vocabulary, the headline analysis helper and the graded strength. |
| `src/api/Scoring/ImportanceRuleIds.cs` | Approved addition: the `news.consequential-financial-event` rule id. |
| `src/web/src/app/subscriptions/subscriptions.component.ts` | Taxonomy list only: the `economy.financial` option for alert setup. |
| `src/web/src/app/subscriptions/subscriptions.component.spec.ts` | Taxonomy count assertion updated to the 24 approved codes, plus the new display name. |
| `src/tests/WorldAlerts.Api.Tests/NewsImportanceScorerTests.cs` | New `F1`–`F10` financial rule fixtures: trusted-source decision, material IPO, material amount, official decision, explicit trust map, untrusted source, forecast exclusion, small fundraising, routine/personal-finance/promotion exclusions, bounded score. |
| `src/tests/WorldAlerts.Api.Tests/CoverageGapTests.cs` | New: the financial code is assigned only for consequential financial events, provider financial topic tags assign it, a generic "Business" tag does not. |
| `docs/decisions/2026-10-21-task-01-acceptance-boundaries.md` | Approved D1 taxonomy row and the D2 financial rule with criteria, approved examples and the fixture inventory. The frozen `ctx/goals/plan.md` and the tracker structure are unchanged. |

## 8. Test status

* Backend build: `dotnet build src/WorldAlerts.sln -m:1 /nodeReuse:false --nologo` → **succeeded,
  0 warnings, 0 errors** (serialized run, no concurrent build).
* Backend tests: `dotnet test src/WorldAlerts.sln --no-build --nologo -m:1 /nodeReuse:false` →
  **539 passed, 0 failed, 0 skipped** (512 before the approved financial addition, +27 new
  fixtures).
* Focused tests for the financial addition (`NewsImportanceScorerTests`, `CoverageGapTests`,
  `ImportantNewsTypeCoverageTests`, `ImportantNewsEndpointTests`): **165 passed, 0 failed**.
* Frontend: `npx ng test --watch=false` in `src/web` → 5 files, **29 tests passed** (including
  the taxonomy list count for the 24 approved codes).
* Full output is recorded in `out/backend-verification.log`.
* The running API still serves the pre-change build; the new `economy.financial` code, the
  `news.consequential-financial-event` rule and the new diagnostic fields
  (`eligibleNotImportantCount`, `unscorableCount`, `ruleCounts`) appear only after the parent
  rebuilds and restarts the backend.
