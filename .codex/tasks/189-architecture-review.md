# Task: Repo-wide review 01 — Architecture, layering, and maintainability

**GitHub issue:** #189
**Type:** Review only — do not make code changes, do not open a PR
**Output:** Post a structured report as a comment on GitHub issue #189 if possible; otherwise write the report to `.codex/reports/189-architecture.md`

---

## Instructions

Perform a focused repository-wide architecture and maintainability review.

Do not make code changes. Do not open a PR. Produce a structured report only.

## Focus areas

- Overall architecture
- Project boundaries
- Layering and separation of concerns
- Business logic placement
- Dependency direction
- Shared library design
- Coupling between UI, services, business logic, data access, and infrastructure
- Duplicated business logic
- Large classes or methods
- Hard-to-test design
- Static/global state
- Service locator patterns
- Legacy areas that should be isolated
- Areas where incremental refactoring would reduce risk

## For each finding include

- Severity: P0, P1, P2, or P3 (see AGENTS.md for definitions)
- File/path or area
- Evidence from the codebase
- Why it matters
- Recommended fix
- Estimated effort: Small, Medium, or Large
- Suggested owner type

## Output format

1. Executive summary
2. Architecture map
3. Top 10 maintainability risks
4. Refactoring candidates
5. Suggested phased remediation plan
6. Areas that should not be refactored without additional tests

## Key files to read first

- `AGENTS.md` — operating rules, severity levels, repo-specific review guidelines
- `HVO.WebSite.sln` — solution structure and project references
- `src/HVO.WebSite.v9/` — main site (Blazor SSR + ASP.NET Core API)
- `src/HVO.WebSite.Themes/` — shared RCL (layouts, CSS, components, formatting)
- `src/HVO.DataModels/` — EF Core models and DbContext
- `src/HVO.Hardware.*/` and `src/HVO.Gateway.*/` — Pi gateway projects
- `tests/` — unit and Playwright test projects
- `Directory.Build.props`, `Directory.Packages.props` — solution-wide build config
