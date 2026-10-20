# Goals

**Feature status: FROZEN — human-approved scope.** Implementation details and open feasibility questions are addressed task by task; changes to these goals require explicit human direction.

## Original requirement (verbatim)

> "We want users to be able to set up alerts so they get notified when something important happens in the world — like breaking news, market movements, natural disasters, that kind of thing. Should work for both email and Slack. Make it flexible enough that we can add more channels later. We need an admin view too."

## Agreed human direction

- Each person runs their own local copy; GitHub hosting is withdrawn. This is an in-house, noncommercial project, not designed for large-scale service.
- Use C# for the application, SQLite for local persistent data, and a frontend for user alert setup and the admin view.
- Support email through SMTP and Slack through incoming webhooks; event-data sources require no account or API key, but delivery credentials/webhook URLs still need configuration.
- Place Linux and Windows launch scripts in the repository's top-level directory. They build, test, then run the containerized application locally and idempotently, with GitHub Actions turned off. The directory location does not imply running the scripts as root or administrator; Docker access is a separate prerequisite. Do not implement these during planning.

## Further human-specified goals

- Users subscribe to an event type from the approved event-type coverage matrix and configure notification channels; when an important event of that type occurs, they are notified on those channels. No per-source or per-instrument custom conditions were requested.
- Breaking-news importance, market-movement importance, and natural-disaster importance are to be inferred algorithmically and deterministically using multiple factors and available metadata, including importance scoring. The human cannot provide further algorithm details; no particular weights, thresholds, or per-event scoring formula have been approved.
- When the application starts, look up important events from the previous 24 hours across **all defined event categories**, including breaking news, market movements, natural disasters, and other approved types, so a user can catch up after the computer was off. Send important items reported late as normal notifications. Do not overwhelm the user with many notifications at once; allow reasonable, non-excessive retries. The human now allows some timing flexibility for late reports and catch-up, superseding the earlier skip-offline instruction and unconditional under-five-minutes-from-occurrence demand for these cases. The precise timing target, 24-hour timestamp interpretation, de-duplication and pacing of catch-up notifications still require review; source support for retrieving 24 hours of history across all categories is not yet verified.
- Support adding notification channels for platforms not pre-coded in the application, out of the box and without changing production code. No mechanism for that capability has yet been approved.
- Provide an admin view covering what needs to be behind an admin wall; its exact permissions and functions remain to be reviewed.
- The frontend may use React or Angular; neither framework has been selected.
- Plan unit tests for non-trivial methods and integration tests for major functions, together with a feature inventory/matrix identifying what unit and integration tests actually cover.
- Document each implemented user-facing feature in a user guide as work progresses. Every implementation task contributing to the original request must provide evidence along the way (logs and/or screenshots appropriate to the task).

The approved event-coverage matrix guides implementation. A single probe does not prove exhaustive worldwide operational coverage; remaining matching details, feasibility questions and acceptance decisions are addressed with the human during the corresponding tasks, without silently weakening these goals.
