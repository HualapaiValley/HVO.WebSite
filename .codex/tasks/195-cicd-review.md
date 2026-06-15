# Task: Repo-wide review 07 — Build, CI/CD, configuration, and deployment safety

**GitHub issue:** #195
**Type:** Review only — do not make code changes, do not open a PR
**Output:** Post report as a comment on issue #195 if possible; otherwise write to `.codex/reports/195-cicd.md`

## Instructions

Perform a focused repository-wide review of build, CI/CD, configuration, and deployment safety.

## Focus areas

- Build entry points — `HVO.WebSite.sln`, `dotnet build` from repo root
- Solution/project build consistency — zero warnings, zero errors is the hard gate
- Package restore reliability — `NuGet.config`, lock files, `Directory.Packages.props`
- GitHub Actions workflows — `.github/workflows/` — what runs, what's missing
- Environment-specific configuration — `appsettings.json`, `appsettings.Production.json`
- Secret management — must be in `.env` only, not in source
- Deployment scripts — `scripts/deploy-pi-gateway.sh`, `scripts/check-deployments.sh`
- Docker Compose files — `restart: unless-stopped`, health checks, volume mounts
- Docker context SSH (`devpi5`) — stale shell env overriding `--env-file` is a known issue
- EF Core migrations — are they applied as part of deployment?
- Branch protection and merge gates — are PRs required? Are CI checks enforced?
- Manual deployment risks — anything that requires undocumented manual steps
- Documentation gaps — can a new developer build, test, and deploy from the README?

## Key context for this repo

- Build gate: `dotnet build` must produce **0 warnings, 0 errors**
- Test gate: `dotnet test --filter "TestCategory!=Live"` must produce **0 failures**
- Gateways deploy to devpi5 via `docker --context devpi5 compose`
- Main site deploys to Azure Container Apps
- Docker Compose shell env precedence: shell variable wins over `--env-file` — a stale shell variable will silently override the `.env` file
- Root `.env` synced to GitHub gist via `scripts/sync-env-gist.sh` — must be run after any IP/credential change

## Output format

1. Executive summary
2. Build/test command discovery
3. CI/CD risk summary
4. Configuration risk summary
5. Recommended branch/pipeline gates
6. Phased remediation plan

## Key files to check

- `.github/workflows/` — all CI pipeline definitions
- `HVO.WebSite.sln` — solution build entry point
- `Directory.Build.props`, `Directory.Packages.props`, `NuGet.config`
- `deploy/pi-gateways/*/docker-compose.yml` — all 5 gateway deployments
- `deploy/pi-gateways/*/.env.example` — template accuracy
- `scripts/` — all deployment and utility scripts
- `src/*/Program.cs` — startup, health check registration
