# How AI was used

## Tools

**Claude Code** (Anthropic, model Claude Opus 5.5), in VS Code. It was used for almost all of this work:

- **Analysing the brief.** I gave Claude the PDF and asked what we should build and how. It proposed the
  design: atomic conditional `UPDATE`s for the status and stock changes, CHECK constraints, idempotent repeats,
  an `Idempotency-Key` for create, and tests against a real database instead of the EF in-memory provider.
- **Writing a build prompt.** Claude wrote a detailed build specification, which I edited (for example, I added
  the requirement that the UI follow Apple's design principles). The final version is committed as
  [`docs/BUILD_PROMPT.md`](docs/BUILD_PROMPT.md).
- **Building the system.** Following that prompt, Claude Code wrote the API, schema and migrations, seeding,
  tests, React UI and these docs. It also ran builds, tests and a scripted browser walkthrough (Playwright with Edge)
  to check the UI, including double-click behaviour, light and dark mode, and phone width.
- **Commits.** Claude Code made one commit per phase. Each commit carries a `Co-Authored-By: Claude` trailer.

## Where the AI was wrong or unhelpful

Claude kept a factual log of its own mistakes while it worked: [`docs/AI_SESSION_LOG.md`](docs/AI_SESSION_LOG.md).
Examples from that log:

- **Wrong connection string host.** Claude wrote the dev connection string as `localhost,14333`. On this Windows +
  Docker Desktop machine `localhost` resolved to IPv6 `::1`, which Docker did not forward, so the API's
  first start timed out. This was noticed because the API crashed. `Test-NetConnection` showed `127.0.0.1` open
  and `::1` closed. The fix was to use `127.0.0.1`.
- **A React anti-pattern caught by the linter.** Its first transfer list set React state synchronously inside an
  effect, and `oxlint` flagged it. It was rewritten to derive the loading state instead.
- **A false alarm.** Reviewing its own screenshots, Claude thought dark mode had a colour bug. The screenshot had
  been taken mid-transition. A delayed re-take showed it was fine.

TODO (human): pick the example you noticed yourself, or add your own, and say in your own words how you noticed it.

## What I wrote or decided myself

TODO (human): write this section in your own words. Facts from the session record that may help:

- You chose to use Claude Code for analysis, specification and implementation.
- You reviewed and edited the build prompt before running it. You added the Apple UI/UX requirement and changed
  the test requirement to "perform meaningful automated tests".
- You asked for the full build to proceed in one go.
- Decisions to confirm or change in your own words: SQL Server over PostgreSQL; returning 200 (not 409) for a
  repeated action; checking stock at dispatch rather than reserving at request; doing the optional partial receive.
