# Task: Repo-wide review 00 — Inventory, architecture map, and review plan

**GitHub issue:** #187
**Type:** Review only — do not make code changes, do not open a PR
**Output:** Post a structured report as a comment on GitHub issue #187 if possible; otherwise write the report to `.codex/reports/187-inventory.md`

---

## Instructions

Perform a repository-wide inventory and review planning pass.

Do not make code changes. Do not open a PR. Produce a structured report only.

## Goals

- Identify the major projects, applications, services, libraries, test projects, scripts, and deployment/configuration areas in this repository.
- Infer the primary technology stack from the source.
- Identify build/test entry points.
- Identify likely business-critical or high-risk areas.
- Identify which areas should be reviewed in later focused passes.
- Identify any missing documentation that would make the repo hard for a new developer or reviewer to understand.

## Scope

Review:

- Solution/project structure
- Application entry points
- API/service boundaries
- UI/front-end areas
- Data access layers
- Shared libraries
- Configuration files
- Build and CI/CD files
- Test projects
- Documentation

## Output format

Produce all seven sections. If you cannot post to GitHub, write the report to `.codex/reports/187-inventory.md`.

1. **Executive summary**
2. **Repository map** — list every project, its type, and its role
3. **Primary technology stack** — infer from `.csproj`, `packages`, `appsettings`, `Dockerfile`, etc.
4. **Build/test discovery** — how to build, how to run tests, CI entry points
5. **High-risk areas to review first** — business-critical code, security boundaries, complex integrations
6. **Recommended focused review sequence** — ordered list of follow-up review passes with scope for each
7. **Open questions or assumptions** — anything that could not be confirmed from local files

## Key files to read first

- `AGENTS.md` — operating rules, severity levels, repo-specific review guidelines
- `docs/CSS_GOVERNANCE.md` — CSS authoring policy
- `docs/UNIFIED_THEME_PLAN.md` — active migration epic
- `.github/copilot-instructions.md` — coding standards
- `HVO.WebSite.sln` — solution structure
- `src/` — all project source
- `deploy/pi-gateways/` — gateway deployment configs
- `tests/` — test projects
- `.github/workflows/` — CI pipelines
