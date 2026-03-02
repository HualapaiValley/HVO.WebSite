```instructions
# Copilot Instructions for HVO.NinaClient

## Project Overview
HVO.NinaClient is a .NET library that talks to NINA's REST and WebSocket APIs. It provides typed clients, resilient connectivity, and the `Result<T>` pattern used across the observatory stack.

## Key Reminders
- All public operations must return `Result<T>` or `Result` and never throw for normal failures. Use the helper methods in `Result.cs` and wrap exceptions with rich context.
- Maintain parity with the official NINA API specs. Before adding or modifying endpoints, cross-check the schemas at https://github.com/christian-photo/ninaAPI.
- Configure clients via `NinaApiClientOptions` and `NinaWebSocketClientOptions`. Keep defaults conservative and expose knobs through configuration binding.
- Reuse the logging conventions described in `docs/projects/nina-client/resilience-architecture.md`. Connection state changes log at `Information`, retries at `Warning`, and protocol errors at `Error`.
- Honor cancellation tokens on every async operation; tests depend on cooperative cancellation for time-bound scenarios.

## Documentation
- Architecture and usage notes live in `docs/projects/nina-client/`. Update those documents whenever you change connection flows, retry policies, or payload models.

## Testing Expectations
- Expand MSTest coverage under `HVO.NinaClient.Tests` (create if missing) or within consuming projects. Favor mocking HttpMessageHandler / WebSocket abstractions rather than hitting live services.
- Include contract tests for serialization when new DTOs are introduced.

## Resilience Patterns
- Keep the resilience infrastructure under `Resilience/` authoritative. Extend the existing Polly policies rather than duplicating retry logic.
- Ensure WebSocket reconnection logic respects the semaphore guard and surfaces terminal failures through `Result<T>` for the caller to handle.
```
