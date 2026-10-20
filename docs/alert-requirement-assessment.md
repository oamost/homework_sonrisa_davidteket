# Assessment: setting up alerts for important world events

**Discussion snapshot; not a frozen specification.** This covers the first clause of the human's request: how a user defines an alert and how the product detects a matching world event. Delivery through email/Slack and the admin view are intentionally out of scope for this assessment; see `docs/product-concept.md` for the broader discussion.

## What is settled by the human

- Users must be able to set up alerts for important events **worldwide**, including breaking news, market movements, and major natural disasters across disaster types and regions; additional kinds of “important” may later be added.
- There is no existing system or required technology stack.
- Event-data sources must not require account registration or API keys. This restriction does not apply to email/Slack delivery.
- The initial named event categories must all be shipped; a demonstration of only one category is insufficient.

## What we have found, without claiming full coverage

| Category | Candidate discovery | Still not evidenced |
| --- | --- | --- |
| Disasters | Global multi-hazard candidates [GDACS](https://www.gdacs.org/feed_reference.aspx) and [NASA EONET](https://eonet.gsfc.nasa.gov/docs/v3), plus supplementary [USGS earthquake data](https://earthquake.usgs.gov/earthquakes/feed/v1.0/geojson.php). | A verified hazard-type × region coverage map, timeliness, quality, use/redistribution rights, and gap-filling keyless sources. |
| Breaking news | [BBC World RSS](https://feeds.bbci.co.uk/news/world/rss.xml), [NYT World RSS](https://rss.nytimes.com/services/xml/rss/nyt/World.xml), [Al Jazeera English](https://www.aljazeera.com/xml/rss/all.xml) and [Guardian World](https://www.theguardian.com/world/rss) returned keyless editorial lists. Al Jazeera had conflict stories and Guardian had politics/elections; GDELT is blacklisted. | A testable definition of “breaking” across war/conflict, politics and other major news, geographic/language breadth, duplicate handling, and rights to use article-derived text. |
| Market movements | Keyless crypto and daily FX feeds returned records; an undocumented Yahoo chart endpoint returned one index sample, not a validated broad-market source. Stooq is blacklisted after HTTP 404. | Dependable keyless data across intended instruments and global exchanges, freshness, and usable price-based alert signals. |

The links above show **candidates**, not vetted production integrations. In particular, examples for crypto and daily FX must not be misrepresented as full market coverage.

## What has not yet been defined

1. **Alert authoring:** the supported predicates per event category, free text versus structured rules, geographic matching, threshold windows, frequency, pause/edit/delete behavior, and whether an alert is personal or shared.
2. **“Important”:** how severity, topical relevance, market significance, and future event kinds become explicit, testable conditions instead of an opaque universal promise.
3. **Detection semantics:** source event identity, revisions and cancellations, story clustering, duplicate suppression, historical versus live matching, alert-once versus reminder behavior, and missing/stale-data behavior.
4. **Coverage evidence:** a category × hazard type × geography/language/instrument matrix tied to verified keyless sources and allowable use, with realistic latency expectations.
5. **Success criteria:** end-to-end scenarios showing that an ordinary user can create each required kind of alert and receive correct matches, with negative examples and source failure cases.

**Assessment:** the objective and constraints are much clearer, and viable source *leads* exist. The event coverage, user-facing alert definition, legal access, and detection rules are **not sufficiently established to freeze the plan or claim the first requirement is solved**. The next useful step is to compare concrete alert examples and a coverage matrix against source capabilities, then bring only true product tradeoffs to the human.
