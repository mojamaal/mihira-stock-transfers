# How AI was used

## Tools

**Claude Code** (Anthropic, model Claude Opus 5.5), in VS Code. It was used for this work:

- **Analysing the requirements and refining the prompt.** I gave Claude the requirements and from the pdf and also gave it the prompt and asked it to refine and enhance the prompt according to the reuqirements outlined in the document and give it to me. It proposed the
  design: atomic conditional `UPDATE`s for the status and stock changes, CHECK constraints, idempotent repeats,
  an `Idempotency-Key` for create, and tests against a real database instead of the EF in-memory provider.
- **Writing a build prompt.** Claude wrote a detailed build specification, which I edited (for example, I added
  the requirement that the UI follow Apple's design principles). The final version is committed as
  [`docs/BUILD_PROMPT.md`](docs/BUILD_PROMPT.md).
- **Building the system.** Following that prompt, Claude Code wrote the API, schema and migrations, seeding,
  tests, React UI and these docs. It also ran builds, tests and a scripted browser walkthrough (Playwright with Edge)
  to check the UI, including double-click behaviour, light and dark mode, and phone width.
- **Reviewing its own assumptions.** After the first build I asked Claude why it had made certain assumptions
  and trade-offs, and whether they were wrong. It agreed several were weak (an idempotency key not bound to the
  request, silent truncation of the list at 200, no retry on transient SQL errors, an implicit branch filter), and
  then fixed them. It also added a stock movement ledger, stock visibility for open requests and incoming
  transfers, required shortfall reasons, and live updates via server-sent events.
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
- **A race that didn't exist.** In its review, Claude said two concurrent receives into a missing stock row "could
  collide" and return a 500, then wrote a fix and a test. The test passed even with the fix disabled, which showed
  it proved nothing. A forced interleaving deadlocked, and a lock dump showed SQL Server already serialises that
  case. Claude removed the misleading test, kept the fix as a documented safety net, and corrected the README.
- **Garbled characters.** Claude edited two React files with a PowerShell text replacement that read UTF-8 as
  Windows-1252, so characters like "…" and "→" were committed garbled. It noticed while reading a file later,
  searched the repo for similar damage, and repaired both files.

The one I caught myself was in the first README. Claude listed a few "trade-offs" that read fine at a glance,
but when I went through them properly some looked more like shortcuts than decisions. The list was cut off at
200 rows with no warning. An idempotency key could be reused for a completely different request. Nothing
retried if SQL dropped a connection. The branch filter quietly matched both directions. So I asked it straight
out why it had chosen these and whether they were wrong. It agreed that most of them were weak, and we fixed
them in the second round. The lesson for me: the AI is good at making a gap sound like a deliberate choice, so
the trade-offs section needs a critical read, not just a skim.


## What I wrote or decided myself

I didn't hand-write most of the code. My part was deciding what to build, steering it, and checking the result.

- I set up the starting project myself: an ASP.NET Core Web API on .NET 10 with Docker support. That's why the
  solution is on .NET 10 and SQL Server rather than PostgreSQL.
- I had Claude  understand the requirements and how the system should function and what are the expected outputs and what are the constraint and also i gave it a prompt and asked it to refine and enhance the prompt which i used to ask the claude code to develop the system and before using that prompt I read the prompt and changed
  it before running it. I wanted the UI to follow Apple's design principles rather than "basic CSS is fine", and I
  loosened the test wording so it wasn't just aiming for the minimum of three.
- After the first build, I questioned the assumptions and trade-offs and pushed for the weak ones to be fixed
  rather than just written down. Making the branch filter explicit (in, out or both) came from that.
- I asked for live updates instead of refreshing after each action. Several branches work on the same transfers,
  so it makes sense for a dispatch in Colombo to show up in Kandy straight away.
- I agreed with the main design calls: check stock at dispatch rather than reserving it at request time (the
  brief says stock leaves at dispatch), treat a repeated dispatch or receive as a harmless no-op that's clearly
  flagged rather than an error, and do the optional partial receive with a required reason.
- When time was getting long, I chose to skip the final browser walkthrough and fresh-clone check of the second
  round, and to do the user acceptance testing myself instead.
