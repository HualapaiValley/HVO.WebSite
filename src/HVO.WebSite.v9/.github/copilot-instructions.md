```instructions
# Copilot Instructions for HVO.WebSite.v9

## Project Overview
This project delivers the main HVO v9 Blazor Server + ASP.NET Core site. It hosts the production UI, versioned REST APIs, health probes, and OpenAPI docs backed by `HVO.DataModels`.

## Key Reminders
- Follow the global theme guidance: load `_content/HVO.WebSite.Themes/css/themes/hvo-dark.css`, set `data-theme="hvo-dark"`, and reuse theme variables instead of hard-coded colors.
- API controllers must use URL-segment versioning (`/api/v1.0/...`) and return `Result<T>`-aware responses. Keep controller logic thin; push business rules into services under `Services/`.
- Register dependencies in `Program.cs` using extension methods when logic grows. Keep health checks tagged (`roof`, `hardware`, etc.) and update documentation when new tags appear.
- Maintain parity between the REST contract and any docs living under `docs/projects/roof-controller-v4-rpi` or other project folders when endpoints overlap.

## Documentation
- Primary reference: `src/HVO.WebSite.v9/README.md`.
- Related guides: `docs/skymonitor-v5-operations-runbook.md`, `docs/skymonitor-v5-json-migration-guide.md`, and other entries under `docs/projects/` depending on the feature.

## Testing Expectations
- Use MSTest or integration tests built on `WebApplicationFactory<HVO.WebSite.v9.Program>`. Cover health checks, API behavior, and UI endpoints.
- Ensure new APIs have both success and failure path tests, including ProblemDetails responses for errors.

## Security & Configuration
- Keep HTTPS toggles (`EnableHttpsRedirect`, `TrustDevCertificates`) in sync with deployment needs. Document changes in `README.md` and `docs/`.
- Sanitize user input via model binding + data annotations. Never expose raw exceptions; rely on `HvoServiceExceptionHandler` for uniform error output.
```
