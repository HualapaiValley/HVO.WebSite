# Task: Repo-wide review 05 — Data access, SQL, transactions, and database safety

**GitHub issue:** #193
**Type:** Review only — do not make code changes, do not open a PR
**Output:** Post report as a comment on issue #193 if possible; otherwise write to `.codex/reports/193-data-access.md`

## Instructions

Perform a focused repository-wide review of data access and database interaction patterns.

## Focus areas

- SQL injection — raw SQL, string-concatenated queries, unsafe parameterization
- EF Core async compliance — all queries must use `ToListAsync`, `FirstOrDefaultAsync`, etc.
- Transaction boundaries — `SaveChangesAsync` inside loops, missing transactions
- Connection lifetime management via DI
- ORM misuse — `AsEnumerable()` before a `Where` (client-side evaluation)
- N+1 query patterns
- Unbounded queries and missing pagination
- Excessive data loading — full entity graphs returned to UI layers
- Null handling in database results
- Date/time and timezone consistency
- Isolation and concurrency assumptions
- Retry behavior around database operations
- Silent data truncation or conversion risks
- Error handling around database calls
- EF Core migrations — every schema change should have a corresponding migration

## Key context for this repo

- `HVO.DataModels` project owns the EF Core `DbContext` and models
- `HVO.WebSite.v9` is the only project consuming `HVO.DataModels` for SQL Server
- Gateway projects use SQLite for local outbox storage — check those separately
- All EF operations must be async — `.Result`/`.Wait()` on EF tasks is a P1 finding per AGENTS.md

## Output format

1. Executive summary
2. Top data risks
3. Query and transaction observations
4. Database-related test gaps
5. Suggested remediation order

## Key files to check

- `src/HVO.DataModels/` — DbContext, models, migrations
- `src/HVO.WebSite.v9/` — all data service classes, repository pattern if used
- Gateway outbox database code (`HVO.Edge.Outbox`)
- `tests/HVO.WebSite.UnitTests/` — data layer test coverage
