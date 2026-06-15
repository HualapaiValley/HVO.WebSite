# Task: Repo-wide review 06 — Tests, coverage gaps, and QA strategy

**GitHub issue:** #194
**Type:** Review only — do not make code changes, do not open a PR
**Output:** Post report as a comment on issue #194 if possible; otherwise write to `.codex/reports/194-tests.md`

## Instructions

Perform a focused repository-wide review of test coverage, test quality, and test strategy.

## Focus areas

- Existing test projects and frameworks (xUnit, FluentAssertions, Playwright)
- Unit test coverage — business logic, view models, data transformations
- Integration test coverage — EF Core, API endpoints
- Playwright E2E tests — circuit alive, charts rendered, theme compliance
- Missing tests for critical business logic (ingest processing, outbox forwarding, power snapshot)
- Missing tests for validation and error paths
- Missing tests for data access behavior
- Missing tests for concurrency, timeout, or retry behavior in gateway workers
- Brittle tests — `Thread.Sleep`, wall-clock time dependency, hard-coded data
- Tests that verify implementation details instead of behavior
- Live tests — must be tagged `TestCategory=Live` to be excluded from standard CI run
- CI integration — are tests run in GitHub Actions?
- Areas where tests must be added before any refactoring is safe

## Key context for this repo

- Unit tests: `tests/HVO.WebSite.UnitTests/` — 169 tests currently passing
- Playwright tests: `tests/HVO.WebSite.PlaywrightTests/` — live E2E tests for gateway UIs
- Test filter for CI: `dotnet test --filter "TestCategory!=Live"` — live tests require running gateways
- `HvoChartDataset` must have: null data entries accepted, `Tension` parameter stored, `Data_AllowsNullEntries_RepresentingGaps` test
- Test standard per AGENTS.md: tests for all new/modified business logic; no coverage = no merge

## Output format

1. Executive summary
2. Current test landscape (what exists, what framework, what coverage)
3. Top test gaps (ordered by risk)
4. Recommended minimum test standard for this repo
5. Suggested first 10 tests to add
6. Phased test strategy

## Key files to check

- `tests/HVO.WebSite.UnitTests/` — all unit test files
- `tests/HVO.WebSite.PlaywrightTests/` — all Playwright test files
- `.github/workflows/` — CI test execution
- `src/HVO.WebSite.v9/` — which services/logic has no test coverage
- Gateway worker classes — `Workers/`, `Services/` in each gateway project
