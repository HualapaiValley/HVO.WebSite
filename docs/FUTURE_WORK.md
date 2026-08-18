# Future Work

Status: current forward-looking roadmap after the vNext collector migrations and direct SolarAssistant/TP-Link gateway retirement.

Last updated: 2026-08-18

This document is the single place for future planning items that are not yet implemented. Completed outbox migration plans were removed or replaced by current standards and reference docs.

## Completed Baseline

The following work is complete on `main` and should be treated as the current baseline, not future work:

- `HVO.Edge.Outbox` shared durable outbox library with `EdgeOutboxFailureKind`, retry/backoff, sent and failed compaction, `RetryExhausted` auto-requeue support, and health evaluation.
- Davis, JK BMS, EG4, and SmartShunt are the active direct headless vNext collectors and use the shared outbox path.
- Davis local station persistence is split from its telemetry outbox.
- JK BMS no longer has all-or-nothing behavior for per-record permanent failures.
- Home Assistant owns Kasa and Govee acquisition and presentation.
- The HA exporter is implemented but intentionally disabled in production with no mappings or source claims.
- The direct SolarAssistant and TP-Link/Kasa applications, containers, and images have been removed.
- The retired SolarAssistant volumes were checksum-archived outside Docker storage and removed.

## Priority Candidates

| Priority | Area | Goal | Notes |
|----------|------|------|-------|
| P1 | Website gateway status UI | Add simple main-site cards for gateway health, outbox status, source freshness, and protected diagnostic references. | Use `GatewayStatusPayload`/shared health concepts; active collectors have no local dashboards. |
| P1 | Power model source precedence | Document and test measurement-point precedence across EG4, JK BMS, and SmartShunt. | PV/load/inverter branches come from EG4; per-cell health from JK BMS; whole-bus current/power from SmartShunt. |
| P1 | Operator outbox replay tooling | Add safe operator-visible handling for permanent failures and retry-exhausted rows. | Automatic requeue is only for `RetryExhausted`; permanent dead letters need explicit operator/code/config action. |
| P2 | Local history separation | Decide which gateways need persistent local history separate from the outbox. | Outbox is transport durability only; local chart preload/history should be gateway-owned storage. |
| P2 | Weather/BMS aggregates | Complete minute/hourly aggregate population where still incomplete. | Existing tables/read paths exist, but rollup coverage remains partial. |
| P2 | Domain-rich health | Expand health checks beyond process liveness to device freshness, outbox sync, and local protocol state for every gateway. | Several gateways expose this already; central display is still minimal. |
| P2 | Website/Azure hardening | Move SQL auth toward least-privilege Entra/managed identity separation for runtime vs migrations. | Current architecture doc keeps this as a security modernization item. |
| P3 | Future integrations | Prototype only after real hardware/topics are available. | Keep future integrations in `docs/gateways/future-integrations.md`; avoid speculative code. |
| P3 | HA-owned device command safety | Keep Kasa actions in HA presentation policy and require connected-load classification, confirmation, and explicit operator approval. | A replacement direct HVO Kasa command path is out of scope. |

## Open Design Questions

- Should every collector expose a protected operator endpoint or use shared tooling to inspect and selectively requeue dead-lettered outbox rows?
- Which collectors, if any, need persistent local history separate from the outbox?
- What is the first central website audience for gateway status: public observatory status, private operator dashboard, or both?
- Should typed API client/DTO packages be extracted to reduce payload drift between gateways and website controllers?
- When, if ever, does the archived brokered ingest path justify its added operational complexity?
