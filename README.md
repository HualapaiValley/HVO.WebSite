# HVO.WebSite

[![CI](https://github.com/RoySalisbury/HVO.WebSite/actions/workflows/ci.yml/badge.svg)](https://github.com/RoySalisbury/HVO.WebSite/actions/workflows/ci.yml)
![.NET](https://img.shields.io/badge/.NET-9.0-blue)
![License](https://img.shields.io/badge/license-proprietary-lightgrey)

Observatory dashboard and monitoring web application built with ASP.NET Core and Blazor Server (SSR). Provides real-time observatory status, weather monitoring, imaging session tracking, and equipment control interfaces.

---

## Projects

| Project | Description |
|---------|-------------|
| **HVO.WebSite.v9** | Main observatory dashboard — Blazor SSR pages + ASP.NET Core API endpoints |
| **HVO.DataModels** | Entity Framework Core models and DbContext for observatory data |
| **HVO.WebSite.Themes** | Shared CSS themes, fonts, and static assets (Razor Class Library) |

## Features

| Feature | Description |
|---------|-------------|
| **Observatory Dashboard** | Real-time observatory status and equipment monitoring |
| **Weather Monitoring** | Live weather station data display and historical trends |
| **Imaging Sessions** | Track and review astronomy imaging sessions |
| **Real-Time Updates** | Live data refresh for sensor readings and equipment status |
| **Dark Theme** | Purpose-built dark UI theme for observatory use |
| **Responsive Layout** | Mobile-friendly dashboard for remote monitoring |

## Dependencies

- [HVO.Core](https://github.com/RoySalisbury/HVO.SDK) — core shared library (NuGet)
- [HVO.Core.SourceGenerators](https://github.com/RoySalisbury/HVO.SDK) — source generators (NuGet)

---

## Quick Start

```bash
cd src/HVO.WebSite.v9
dotnet build
dotnet run
```

---

## Dev Container

This repository includes a [dev container](.devcontainer/) configuration for a consistent development environment. Open the repository in VS Code or GitHub Codespaces to get started automatically.

---

## Documentation

| Guide | Description |
|-------|-------------|
| [Contributing](CONTRIBUTING.md) | PR workflow, branch naming, coding standards |
| [Changelog](CHANGELOG.md) | Release history and notable changes |

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for PR workflow, coding standards, and branch naming conventions.

---

## License

See [LICENSE](LICENSE). Internal use only. Not for external distribution.
