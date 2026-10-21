# AI session log

A factual running log, kept by the AI (Claude Code) during development, of places where the AI
was wrong, had to correct itself, or where a build/test failure exposed a wrong assumption.
Only real events are recorded here. This log feeds `AI_USAGE.md`.

| # | Phase | What happened | How it was noticed | Fix |
|---|---|---|---|---|
| 1 | 2 – data | The AI wrote the dev connection string as `Server=localhost,14333`. On this Windows/Docker Desktop setup `localhost` resolved to IPv6 `::1`, which Docker Desktop did not forward, so the first startup timed out (SQL error 258). | API crashed on startup; `Test-NetConnection` showed `127.0.0.1:14333` open but `::1:14333` closed. | Use `127.0.0.1` in the connection string. |
| 2 | 4 – commands | The AI reused the pattern variable name `key` in a `catch ... when (idempotencyKey is Guid key ...)` filter while `key` was already declared earlier in the method (CS0136). | Compiler error on build. | Use `idempotencyKey.Value` in the catch filter. |
