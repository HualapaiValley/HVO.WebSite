# Task: Repo-wide review 08 — Dependencies, framework versions, and modernization risks

**GitHub issue:** #196
**Type:** Review only — do not make code changes, do not open a PR
**Output:** Post report as a comment on issue #196 if possible; otherwise write to `.codex/reports/196-deps.md`

## Instructions

Perform a focused repository-wide review of dependencies, framework versions, package management, and modernization risks.

## Focus areas

- .NET target framework versions across all projects (expect .NET 10)
- NuGet package versions — `Directory.Packages.props` centralized versioning
- `PackageReference` vs `packages.config` usage consistency
- Known deprecated packages or APIs in use
- Version drift across projects in the solution
- Transitive dependency risks
- MudBlazor version (8.x expected) — any breaking change exposure
- Entity Framework Core version
- Chart.js version (4.4.0 bundled locally at `_content/HVO.WebSite.Themes/js/chart.min.js`)
- Playwright version in test projects
- JavaScript/npm packages in `.opencode/` or any front-end tooling
- Lock files present and up to date
- Build tooling age — MSBuild, SDK version in `global.json`

**Group findings as:**
- Must fix now (security vulnerability, build blocker, runtime failure risk)
- Should plan soon (unsupported version, deprecated API, known breaking changes incoming)
- Modernization opportunity (newer API would simplify code, minor version updates)

## Key context for this repo

- All projects target .NET 10
- Packages are centrally versioned in `Directory.Packages.props`
- MudBlazor is the UI component library for all gateway and main site projects
- Chart.js is bundled locally — CDN references in gateway App.razor files are a P0 violation
- `global.json` pins the SDK version

## Output format

1. Executive summary
2. Dependency inventory summary (table: package, version, used by, status)
3. Framework version summary
4. Top modernization blockers
5. Suggested modernization roadmap

## Key files to check

- `Directory.Packages.props` — central package versions
- `Directory.Build.props` — shared build properties
- `global.json` — SDK version pin
- Each `*.csproj` — `TargetFramework`, any version overrides
- `src/HVO.WebSite.Themes/wwwroot/js/chart.min.js` — Chart.js version comment
- `.opencode/package.json` — JS dependencies if present
