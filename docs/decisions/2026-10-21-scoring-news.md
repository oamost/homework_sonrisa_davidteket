# Decision: deterministic news importance scoring

Date: 2026-10-21. Scope: Deliverable 3 task 14, news family only. The shared
contract files `ImportanceDecision.cs`, `ImportanceRuleIds.cs` and
`ScoringText.cs` are unchanged; this note records the news rule, the available
metadata and the boundary behaviour that the news scorer implements.

## Approved rule mapping

The rule is the human-approved D2 news example in
`docs/decisions/2026-10-21-task-01-acceptance-boundaries.md`. Numeric values and
the default tier map are acceptance fixtures, not universal weights.

| Priority | Rule id | Condition | Result |
| --- | --- | --- | --- |
| 1 | `news.tier-1-urgency-keyword` | The primary source resolves to tier-1 and its headline contains an approved urgency keyword. | Important, score 1.0 |
| 2 | `news.tier-1-corroboration` | At least two independent tier-1 sources have usable publication times, and at least one pair is no more than six hours apart. | Important, score 1.0 |
| 3 | `news.no-approved-trigger` | Neither rule matched. | Not important, score 0.0 |

Rule 1 is checked first, so a keyword match does not require corroboration. Rule
2 is checked only when rule 1 did not match. `ImportanceDecision` carries the
stable rule id, human-readable factors and a binary score; there are no graded
editorial weights.

The default urgency keywords are the fixture list in
`NewsImportanceScorer.DefaultUrgencyKeywords`: breaking, conflict, war,
invasion, airstrike/air strike, missile, hostage, coup, disaster, earthquake,
tsunami, flood, cyclone, hurricane, typhoon, wildfire, volcano, drought,
landslide, epidemic and pandemic. Matching is whole-token, case-insensitive and
whitespace/punctuation-normalized. This prevents `war` matching inside
`software` while still matching `war`, `WAR` and `war.`.

The default tier map in `NewsSourceTiers.Default` treats BBC, NYT, Al Jazeera
and The Guardian as tier-1, and Ars Technica and Nature as specialist. A caller
can supply a different map. These defaults are fixtures, not a claim about
real-world editorial quality.

## Input and output shape

`NewsImportanceScorer.Score` accepts:

- `NewsItem item`: the normalized item persisted by the news collector.
- `DateTimeOffset nowUtc`: caller-supplied evaluation time. No clock is read
  inside the scorer, so tests and replay are deterministic.
- `IReadOnlyList<NewsCorroboratingSource>? corroboratingSources`: other sources
  that the caller has already associated with the same story. Each entry has
  `SourceId`, `SourceName` and `PublishedUtc`.

It returns `ImportanceDecision` with `IsImportant`, `RuleId`, `Factors` and
`Score`. The corroboration input deliberately does not include a story id: the
caller owns story clustering and passes only sources it believes carry the same
story. The scorer only checks source independence, tier and publication-time
distance.

## Metadata availability

| Field | Available to the news scorer? | Used by the rule? |
| --- | --- | --- |
| Headline / title | Yes, `NewsItem.Title` | Yes, urgency keyword match |
| Source id | Yes, `NewsItem.SourceId` and `NewsCorroboratingSource.SourceId` | Yes, tier lookup and independence |
| Source display name | Yes | Yes, tier lookup and factors |
| Provider publication time | Yes, nullable | Yes, corroboration window; future times excluded |
| First-seen / fetch time | Yes, `NewsItem.FirstSeenUtc` | No; eligibility is a later concern |
| Family and categories | Yes | No; the approved news rule is headline-based |
| Provenance / link / native id | Yes | No |
| Event time | No, `NewsItem` has no event-time field | No |
| Summary / description | Yes, `NewsItem.Summary` (provider-supplied, nullable, may be truncated) | No; the approved fixtures match the headline only |
| Story/cluster id | No | No; caller supplies corroboration |
| Source organization/alias id | No, only configured source id/name | No |
| Geography, language, revision, editorial weight | No | No |

Missing metadata produces explicit factors rather than an invented decision:

- A missing headline reports that no keyword can be matched.
- An unknown or unclassified source reports `not tier-1 (unclassified)`.
- A missing publication time reports that the source cannot establish
  corroboration.
- A future publication time is reported and excluded.
- A non-tier-1 corroborating source is reported and ignored.
- A duplicate source id is reported and counted once.

Because `NewsItem` has no event time, the D3 event-time → published-time
precedence cannot be evaluated by this scorer. Eligibility (24-hour window,
future exclusion, missing-timestamp exclusion) remains a separate layer. In
particular, the T3 fixture is excluded from delivery by D3 because its
publication time is in the future. Its test exercises only the pure importance
function to record that the function has no eligibility clock; a caller must
apply D3 before treating any decision as deliverable.

## Corroboration details

- Independence is keyed by the normalized source id. `"nyt-world"` and
  `" NYT-World "` are the same source and count once. Blank ids collapse to one
  unknown key, so they cannot manufacture independence.
- The primary source counts only when it is tier-1 and has a usable publication
  time.
- A specialist primary can still be corroborated by two tier-1 sources; the
  specialist itself does not count toward the two.
- Non-tier-1 corroborating sources never count.
- Every pair among all usable tier-1 publications is tested. Exactly six hours
  is inside the window; anything greater is outside. A third source can create a
  new within-window pair, but if no pair is within the window, extra sources do
  not help.
- A future primary or corroborating publication is excluded. If two other valid
  tier-1 sources remain, the corroboration rule can still match.
- The scorer does not cluster stories and does not compare titles. The caller
  must not pass unrelated stories as corroboration.

## Edge cases and tests

`NewsImportanceScorerTests` reproduces the task-01 news fixtures N1–N6 and the
news eligibility cases T1–T5. For T3 the test records only that the pure
importance function has no eligibility clock; D3 still excludes the item from
delivery. The tests also cover the hardening cases: word-boundary keyword
matching, case and whitespace normalization, missing headline, unknown source,
duplicate corroborating ids, the same id in different case, missing and future
publication times on the primary and corroborating sources, non-tier-1
corroboration, a specialist primary with two tier-1 corroborators, three
independent tier-1 sources, any-pair matching, exactly six hours, just over six
hours and a non-positive configured window. The focused run is 34 tests.

## Proposed shared-contract changes (not applied)

These would improve the boundary between scoring and the rest of the pipeline,
but they are outside this task's frozen shared files and are reported only:

1. Add `EventTimeUtc` to `NewsItem`, or introduce a dedicated scoring input, so
   D3 timestamp precedence can be evaluated without changing the news model
   later. `Summary` already exists but is deliberately not used by the approved
   headline rule.
2. Add a `StoryId`/`ClusterId` to the corroboration input and a
   `SourceOrganizationId` so "same story" and "independent organization" are
   explicit rather than inferred from source ids by the caller.
3. Add an eligibility result type (or a combined importance+eligibility result
   with the D3 basis flag) so T2/T3 semantics are represented in one place
   instead of requiring callers to combine two separate decisions.
4. Load source tiers from configuration rather than only from the fixture
   default map, so production tier choices are not hard-coded in the scorer.

## Limitations

- Keyword matching is literal whole-token matching, not stemming: `flooding`
  does not match `flood`, and no synonyms are invented.
- Source independence is id-based. `bbc` and `bbc-world` are treated as
  different ids even if they represent the same organization; an alias map is a
  proposed contract change, not current behaviour.
- The default tier map and keyword list are acceptance fixtures. Changing them
  does not change the underlying rule.
- The scorer is a pure library and is not yet wired into subscription matching
  or delivery.
