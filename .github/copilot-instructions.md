# Copilot Instructions

## Project Overview

**HVO.WebSite** is a .NET 10 observatory dashboard and monitoring web application built with ASP.NET Core and Blazor Server (SSR). It provides real-time observatory status, weather monitoring, imaging session tracking, and equipment control interfaces.

- **Runtime**: .NET 10 / ASP.NET Core + Blazor Server (SSR)
- **ORM**: Entity Framework Core
- **Dependencies**: HVO.Core, HVO.Core.SourceGenerators (NuGet from HVO.SDK)

## Key Components

| Project | Description |
|---------|-------------|
| **HVO.WebSite.v9** | Main observatory dashboard — Blazor SSR pages + ASP.NET Core API endpoints |
| **HVO.DataModels** | Entity Framework Core models and DbContext for observatory data |
| **HVO.WebSite.Themes** | Shared CSS themes, fonts, and static assets (Razor Class Library) |

## Coding Standards

- **Blazor Components**: Follow the file structure convention — `.razor` (markup), `.razor.cs` (code-behind), `.razor.css` (scoped styles)
- **Theme**: Use the HVO Dark theme variables and classes from HVO.WebSite.Themes
- **Logging**: Use structured logging with `ILogger<T>`
- **EF Core**: Use async data access patterns (`ToListAsync`, `FirstOrDefaultAsync`, etc.)

## Issue & PR Workflow

Follow this process for every issue. **Never auto-start the next issue unless explicitly instructed.**

### 1. Start Work

- Create a new branch from `main` for the issue (e.g., `feature/<issue#>-<short-description>` or `fix/<issue#>-<short-description>`).

### 2. Implement

- Work the issue on the branch.
- Write appropriate tests for **all new and modified code**.
- Ensure the project builds with **zero warnings and zero errors**.
- Ensure all tests pass with **zero failures and zero warnings**.

### 3. Submit

- Commit, push, and create a PR.
- **Stop and wait** — do not proceed until instructed.

### 4. Code Review

- After the PR is code-reviewed, address **all** review comments.
- Rebuild the project — **zero warnings and zero errors**.
- Commit and push the fixes.

> **Hard rule:** Never create a PR or merge that has warnings or errors unless specifically instructed otherwise. Never create a PR or merge with failing tests or test warnings.

### 5. Merge & Clean Up

- Merge the PR.
- Switch back to `main` and pull latest.
- Delete the merged branch (local and remote).

### 6. Wait

- **Do not** start the next issue automatically. Wait for explicit instructions.

## Conventions

- **Branch naming**: `feature/<issue#>-<short-desc>`, `fix/<issue#>-<short-desc>`
- **Commit messages**: Conventional commits (`feat:`, `fix:`, `chore:`, `refactor:`, `test:`, `docs:`)
- **Merge strategy**: Squash merge into `main`

## Dev Container Tool Policy

The dev container does **not** include Node.js or Python. Do **not** attempt to use these tools or suggest installing them.

- **Scripting & automation**: Use `bash`/`zsh` shell scripts, `gh` CLI, `dotnet` CLI, or `az` CLI
- **JSON processing**: Use `jq` (installed) or .NET `System.Text.Json`
- **Issue/PR management**: Use `gh issue create`, `gh pr create`, etc.
- **Azure resources**: Use `az` CLI (installed) for Entra ID, App Service, Container Apps, subscriptions, etc.
- **Search**: Use `rg` (ripgrep, installed) for text search
- **Never** suggest `npm`, `npx`, `pip`, `python`, or `dotnet-script` commands

### Heredoc / Multi-Line String Warning

**Do NOT use `cat << 'EOF'` or any heredoc syntax in terminal commands.** Heredocs are unreliable in this environment — content frequently gets corrupted, garbled, or truncated. Instead:

1. Write multi-line content to a file using the file-creation tool (e.g., `create_file`).
2. Reference that file in the terminal command (e.g., `gh issue create --body-file /tmp/issue-body.md`).

This applies to **all** cases where you need to pass multi-line text to a CLI command.
