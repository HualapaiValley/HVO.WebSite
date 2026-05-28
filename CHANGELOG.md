# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/), and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- Added a local SolarAssistant gateway monitor UI with Davis-style shell layout, basic status/settings cards, and Playwright coverage
- Added typed SolarAssistant `gateway.status.v1` snapshots, central ingest/read APIs, outbox forwarding, and central power-card runtime status
- Added SolarAssistant REST/MQTT discovery inventory documentation and deployed `hvo-solarassistant` container image `1.0.2` with the gateway `/inventory` endpoint
- Added read-only SolarAssistant MQTT discovery/state inventory and deployed `hvo-solarassistant` container image `1.0.3` with the gateway `/mqtt-inventory` endpoint
- Converted the SolarAssistant gateway monitor to the Davis-style MudBlazor shell, header, footer, and card layout
- Added local rolling power-history chart cards for SolarAssistant PV, load, grid, and battery power
- Added local SolarAssistant gateway health alerts for stale REST/MQTT, outbox backlog/failures, low battery, high load, and high battery discharge
- Published `hvo-solarassistant` container image `1.0.6` to `hvoobsacr.azurecr.io`
- Added `Seeding:PowerReadApiKey` support for production read-only power API verification
- Added independent Azure ACR publishing scripts and documentation for `hvo-website`, `hvo-davis`, `hvo-jkbms`, and `hvo-solarassistant`
- Added Docker and Docker Compose packaging for the `HVO.Gateway.SolarAssistant` service
- Published `hvo-website` container image `1.0.6` to `hvoobsacr.azurecr.io` and deployed the latest power-system snapshot API to Azure Container Apps
- Published `hvo-website` container image `1.0.8` to `hvoobsacr.azurecr.io`, fixed ACA HTTPS sign-in redirects, and verified browser sign-in to the live power snapshot card
- Published `hvo-website` container image `1.0.11` to `hvoobsacr.azurecr.io`, deployed JK BMS bank details on the live power card, and resolved PR review feedback on BMS query efficiency and bank-card accessibility
- Published `hvo-website` container image `1.0.12` to `hvoobsacr.azurecr.io` and deployed per-bank JK BMS freshness on the live power card
- Published `hvo-website` container image `1.0.13` to `hvoobsacr.azurecr.io` and deployed fresh/aging/stale JK BMS bank highlighting on the live power card
- Published `hvo-website` container image `1.0.14` to `hvoobsacr.azurecr.io` with ASP.NET Core, Azure Key Vault, and Azure Monitor OpenTelemetry package maintenance updates
- Added website Azure Container App deployment notes and standardized the project-specific Key Vault target on `hvoobs-kv`
- Made website container HTTPS listener configuration deployment-dependent so local Docker can keep HTTPS while ACA stays HTTP-only behind ingress
- Documented the website configuration strategy: Key Vault for secrets, env/appsettings for deployment shape, and `v9.SiteConfiguration` for live runtime settings
- Added a cached `ISiteConfigurationService` over `v9.SiteConfiguration` for runtime-editable website settings
- Published `hvo-website` container image `1.0.3` to `hvoobsacr.azurecr.io`, added forwarded-header handling for ACA HTTPS, and redeployed the `hvo-website` Azure Container App in `observatory-rg`
- Removed `.LocalPackages` directory — all HVO packages now sourced from nuget.org
- Removed `LocalPackages` NuGet source from `NuGet.config`
- Removed `.LocalPackages` COPY from Dockerfile
- Repository documentation standardization
- `CONTRIBUTING.md` with PR workflow and coding standards
- `CHANGELOG.md` (this file)
- `LICENSE` file
- `.editorconfig` for consistent formatting
- GitHub issue templates (bug report, feature request)
- GitHub pull request template
- `.github/copilot-instructions.md` with project context
- `.github/dependabot.yml` for automated dependency updates

### Changed

- Rewrote `README.md` to serve as a documentation hub

## [1.0.0]

### Added

- Initial extraction from HVOv9 monorepo
- HVO.WebSite.v9 — main observatory website (Blazor SSR + ASP.NET Core API)
- HVO.DataModels — Entity Framework Core data models and DbContext
- HVO.WebSite.Themes — shared CSS themes and fonts (Razor Class Library)
- CI/CD workflow (`ci.yml`)
- Dev container configuration
