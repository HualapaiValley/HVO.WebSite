# Contributing

Thank you for your interest in contributing to HVO.WebSite! This document outlines the workflow, standards, and expectations for contributions.

## Table of Contents

- [Getting Started](#getting-started)
- [Branch Naming](#branch-naming)
- [Development Workflow](#development-workflow)
- [Coding Standards](#coding-standards)
- [Pull Request Process](#pull-request-process)
- [PR Checklist](#pr-checklist)
- [Issue & PR Labels](#issue--pr-labels)

---

## Getting Started

1. Clone the repository and open in the dev container (recommended).
2. Verify the build: `dotnet build` — expect **zero warnings, zero errors**.

---

## Branch Naming

| Pattern | Use For |
|---------|---------|
| `feature/<issue#>-<short-desc>` | New features and enhancements (e.g., `feature/34-weather-dashboard`) |
| `fix/<issue#>-<short-desc>` | Bug fixes and corrective changes (e.g., `fix/42-null-ref-on-sensor-data`) |

Always branch from `main`. Use the `feature/` or `fix/` pattern for all work, including documentation-only and refactor-only changes.

---

## Development Workflow

1. **Create a feature branch** from `main`:
   ```bash
   git checkout main && git pull origin main
   git checkout -b feature/{issue-number}-{short-description}
   ```

2. **Make incremental commits** with [Conventional Commits](https://www.conventionalcommits.org/):
   ```
   feat: add weather station dashboard (#34)
   fix: handle null sensor reading (#42)
   docs: update configuration reference (#50)
   test: add data model validation tests (#38)
   refactor: extract theme variables (#45)
   ```

3. **Run build** before pushing:
   ```bash
   dotnet build
   ```

4. **Push and create a PR** targeting `main`.

---

## Coding Standards

- **Language**: C# / .NET 10
- **Style**: Follow existing conventions in the codebase
- **Warnings**: Build must produce **zero warnings and zero errors**
- **Blazor Components**: Follow file structure conventions (`.razor`, `.razor.cs`, `.razor.css`)
- **Documentation**: Update docs if the change adds or modifies features, configuration, or components
- **Container Publishing**: If a change updates image version metadata or publish workflow, update `.env`, sync the private `.env` gist, and document the published version in `CHANGELOG.md`

---

## Pull Request Process

1. **Verify** the build passes with zero warnings.
2. **Create the PR** with a clear title and description:
   - Summary of what the PR does
   - Which issue it resolves (`Resolves #N`)
   - Key implementation details
   - Files changed
3. **Address all review comments** — fix code, respond, or discuss.
4. **Re-run build** after any review-driven changes.
5. PRs are **squash-merged** into `main`.

---

## PR Checklist

Before requesting review, verify:

- [ ] Feature branch created from `main` with correct naming
- [ ] `dotnet build` — 0 errors, 0 warnings
- [ ] No new warnings introduced
- [ ] Documentation updated (if applicable)
- [ ] Issue linked in PR description (`Resolves #N`)

---

## Issue & PR Labels

| Label | Description |
|-------|-------------|
| `bug` | Something isn't working |
| `enhancement` | New feature or improvement |
| `documentation` | Documentation changes only |
| `refactor` | Code refactoring with no behavior change |
| `in-progress` | Work is actively being done |
| `help wanted` | Looking for contributors |
| `good first issue` | Good for newcomers |
