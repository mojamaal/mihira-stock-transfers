# AI session log

A factual running log, kept by the AI (Claude Code) during development, of places where the AI
was wrong, had to correct itself, or where a build/test failure exposed a wrong assumption.
Only real events are recorded here. This log feeds `AI_USAGE.md`.

| # | Phase | What happened | How it was noticed | Fix |
|---|---|---|---|---|
| 1 | 2 – data | The AI wrote the dev connection string as `Server=localhost,14333`. On this Windows/Docker Desktop setup `localhost` resolved to IPv6 `::1`, which Docker Desktop did not forward, so the first startup timed out (SQL error 258). | API crashed on startup; `Test-NetConnection` showed `127.0.0.1:14333` open but `::1:14333` closed. | Use `127.0.0.1` in the connection string. |
| 2 | 4 – commands | The AI reused the pattern variable name `key` in a `catch ... when (idempotencyKey is Guid key ...)` filter while `key` was already declared earlier in the method (CS0136). | Compiler error on build. | Use `idempotencyKey.Value` in the catch filter. |
| 3 | 2 – data | To start the API for a check, the AI ran `dotnet run --no-launch-profile --environment Development`, assuming that would set the ASP.NET Core environment. It did not, so the app started outside Development, found no connection string and exited. The AI's check loop then waited ~2 minutes before the run was stopped. | Startup error `Connection string 'ConnectionStrings:Default' is not configured` in the API's stderr. | Set `ASPNETCORE_ENVIRONMENT=Development` explicitly (the README uses the normal launch profile, which already does this). |
| 4 | 6 – front end | The AI's first version of the transfers list called `setLoading(true)` synchronously inside a `useEffect`. | `oxlint` warning `react(set-state-in-effect)`. | Derive "loading" from a request key instead of storing it. |
| 5 | 6 – front end | In the first UI review the AI saw washed-out selected controls in the dark-mode screenshot and suspected a colour bug. It was the screenshot being taken during the 0.2 s colour transition after switching the colour scheme, not a bug. | Re-taking the screenshot after a 600 ms wait showed correct colours. | No code change; the walkthrough script now waits before screenshots. |
| 6 | 6 – front end | The first UI review also found real issues in the AI's layout: the product picker was squeezed on phone widths, the over-stock warning wrapped awkwardly, and action errors were titled with the generic server title "Not allowed". | Reading the Playwright screenshots (desktop, dark, 390 px phone). | Phone-specific line layout, reworded warning, and contextual titles such as "Couldn't dispatch transfer #1". |
