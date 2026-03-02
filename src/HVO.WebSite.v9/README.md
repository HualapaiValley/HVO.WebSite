# HVO.WebSite.v9

A production-style ASP.NET Core + Blazor Server application for HVO v9. It exposes a versioned REST API, built-in health endpoints, and OpenAPI documentation, and integrates with the HVO data models via Entity Framework Core.

## Purpose

- Serve the HVO v9 web UI using Blazor Server components
- Host versioned REST APIs (e.g., weather) following repository standards
- Provide built-in health probes for readiness/liveness and detailed diagnostics
- Publish OpenAPI documentation and an interactive API explorer
- Integrate with HvoDbContext from `HVO.DataModels` for data access

## Technologies Used

- .NET 9.0 / ASP.NET Core
- Blazor Server (Interactive Server components)
- MVC Controllers for API endpoints
- Entity Framework Core (SQL Server) via `HVO.DataModels` and `HvoDbContext`
- Health Checks (`Microsoft.Extensions.Diagnostics.HealthChecks`), including EF Core checks
- ProblemDetails + global exception handling via `AddExceptionHandler` and custom `HvoServiceExceptionHandler`
- API Versioning (`Asp.Versioning.Mvc`) using URL segments (default v1.0)
- OpenAPI & API Explorer (`Microsoft.AspNetCore.OpenApi`) with Scalar UI (`Scalar.AspNetCore`)
- HttpClient via `IHttpClientFactory` (dev cert trust configurable)
- Bootstrap 5 for styling

## Notable Endpoints

- Health: `/health`, `/health/ready`, `/health/live`
- OpenAPI: `/openapi/v1.json`
- API Explorer (Scalar): `/scalar/v1`

## Configuration

- `EnableHttpsRedirect` (bool): Toggles HTTPS redirection (typically disabled in Development)
- `TrustDevCertificates` (bool): Allows local dev certificate trust for HttpClient (useful in containers)

See `Program.cs` for service registration and middleware pipeline details.
