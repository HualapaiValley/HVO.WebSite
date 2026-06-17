# Future Work

Status: current forward-looking roadmap after the shared outbox standardization and gateway migrations completed.

Last updated: 2026-06-17

This document is the single place for future planning items that are not yet implemented. Completed outbox migration plans were removed or replaced by current standards and reference docs.

## Completed Baseline

The following work is complete on `main` and should be treated as the current baseline, not future work:

- `HVO.Edge.Outbox` shared durable outbox library with `EdgeOutboxFailureKind`, retry/backoff, sent and failed compaction, `RetryExhausted` auto-requeue support, and health evaluation.
- SolarAssistant uses the shared outbox and typed power/inventory/configuration/energy/inverter-detail/gateway-status streams.
- TPLink Kasa has local polling, local UI, and shared-outbox forwarding for energy/inventory payloads.
- SmartShunt uses the shared outbox path.
- JK BMS uses the shared outbox path and no longer has all-or-nothing behavior for per-record permanent failures.
- Davis local station persistence is split from the telemetry outbox, and Davis telemetry uses the shared outbox path.
- Pi gateway containers for Davis, JK BMS, SolarAssistant, SmartShunt, and TPLink Kasa are deployable independently via `scripts/deploy-pi-gateway.sh`.

## Priority Candidates

| Priority | Area | Goal | Notes |
|----------|------|------|-------|
| P1 | Website gateway status UI | Add simple main-site cards for gateway health, outbox status, source freshness, and links to local dashboards. | Use `GatewayStatusPayload`/shared health concepts; keep detail pages local until central models stabilize. |
| P1 | Power model source precedence | Document and test source precedence across SolarAssistant, JK BMS, and SmartShunt. | PV/load/grid likely SolarAssistant; per-cell health JK BMS; shunt current/power requires sign/convention validation. |
| P1 | Operator outbox replay tooling | Add safe operator-visible handling for permanent failures and retry-exhausted rows. | Automatic requeue is only for `RetryExhausted`; permanent dead letters need explicit operator/code/config action. |
| P2 | Local history separation | Decide which gateways need persistent local history separate from the outbox. | Outbox is transport durability only; local chart preload/history should be gateway-owned storage. |
| P2 | Weather/BMS aggregates | Complete minute/hourly aggregate population where still incomplete. | Existing tables/read paths exist, but rollup coverage remains partial. |
| P2 | Domain-rich health | Expand health checks beyond process liveness to device freshness, outbox sync, and local protocol state for every gateway. | Several gateways expose this already; central display is still minimal. |
| P2 | Website/Azure hardening | Move SQL auth toward least-privilege Entra/managed identity separation for runtime vs migrations. | Current architecture doc keeps this as a security modernization item. |
| P3 | ESPHome/Govee/future integrations | Prototype only after real hardware/topics are available. | Keep future integrations in `docs/gateways/future-integrations.md`; avoid speculative code. |
| P3 | TPLink command safety | Add command execution only after connected-load classification, local-only audit, and explicit operator approval. | Cloud-originated TPLink commands remain out of scope. |

## Open Design Questions

- Should every gateway expose a local operator endpoint or UI action to inspect and selectively requeue dead-lettered outbox rows?
- Which gateway UIs need a persistent local history database for chart preload and offline viewing, separate from the outbox?
- What is the first central website audience for gateway status: public observatory status, private operator dashboard, or both?
- Should typed API client/DTO packages be extracted to reduce payload drift between gateways and website controllers?
- When, if ever, does the archived brokered ingest path justify its added operational complexity?
