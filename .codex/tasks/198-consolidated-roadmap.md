# Task: Repo-wide review 10 — Consolidated findings and remediation roadmap

**GitHub issue:** #198
**Type:** Review only — do not make code changes, do not open a PR
**Output:** Post report as a comment on issue #198 if possible; otherwise write to `.codex/reports/198-roadmap.md`

## Instructions

Consolidate the results of the previous repo-wide review issues into a single prioritized remediation roadmap.

Use the findings from review issues #187 (inventory), #189 (architecture), #190 (security), #191 (logging), #192 (async/perf), #193 (data access), #194 (tests), #195 (CI/CD), #196 (deps), #197 (front-end) if they are available as issue comments. If you cannot access those results, inspect the repository directly and produce the best consolidated roadmap from first principles.

## Goals

- Identify the highest-risk issues across the entire repository
- Deduplicate overlapping findings from different review passes
- Separate immediate defects (P0/P1) from modernization opportunities (P2/P3)
- Build a practical, phased remediation roadmap
- Suggest concrete work packages that can become GitHub issues or sprint backlog items

## Output format

1. Executive summary
2. Overall repository health assessment (one of: Stable / Needs Attention / High Risk / Critical)
3. Top 10 highest-risk findings across all reviews
4. Consolidated findings grouped by domain:
   - Critical production/security risk (P0)
   - Reliability/diagnostics risk (P1)
   - Maintainability/refactoring risk (P1/P2)
   - Test coverage risk (P1/P2)
   - CI/CD/configuration risk (P1/P2)
   - Modernization/dependency risk (P2/P3)
5. Recommended Phase 1: quick wins — 1 to 2 sprints
6. Recommended Phase 2: high-risk fixes — 2 to 4 sprints
7. Recommended Phase 3: larger refactors and modernization
8. Suggested backlog issues with titles and acceptance criteria
9. Manual verification and testing recommendations
10. Open questions for the engineering team

Do not fix anything yet.

## Key files to read

- `AGENTS.md` — severity definitions and repo context
- Prior review reports in `.codex/reports/` if available
- `docs/CSS_GOVERNANCE.md` — CSS policy already implemented
- `docs/UNIFIED_THEME_PLAN.md` — active migration epic context
