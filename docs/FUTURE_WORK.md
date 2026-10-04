# Future work and open decisions

Current implementation belongs in [architecture](ARCHITECTURE.md). GitHub owns
issue status; these prerequisites were open at the 2026-10-04 audit. This list
does not authorize deployment or control.

## Issue-owned prerequisites

| Issue | Remaining boundary |
|---|---|
| [#346](https://github.com/HualapaiValley/HVO.WebSite/issues/346) | Davis complete 513-page circular archive recovery, cursor/cancellation and protocol safety |
| [#352](https://github.com/HualapaiValley/HVO.WebSite/issues/352) | Native passive SmartShunt key provisioning, parity and atomic summary/detail bundle before authority cutover |
| [#385](https://github.com/HualapaiValley/HVO.WebSite/issues/385) | Permanent Bluetooth proxy/RF placement, H5179 and temporary bridge/fallback credential retirement |
| [#320](https://github.com/HualapaiValley/HVO.WebSite/issues/320) | HA canonical history/exporter rollout; currently disabled, unmapped and unclaimed |
| [#372](https://github.com/HualapaiValley/HVO.WebSite/issues/372), [SDK #82](https://github.com/RoySalisbury/HVO.SDK/issues/82) | Promote staged APIs before retiring the production-used bridge |
| [#437](https://github.com/HualapaiValley/HVO.WebSite/issues/437) | Secret-helper SQL-derivation failure propagation and partial writes; separate runtime fix |

For #352, current [quiescent backup and exactly-one-writer rollback](gateways/sqlite-backup-and-rollback.md)
governs operations; historical issue ordering is not an executable runbook.
SDK #82's old test-only claim is contradicted by the production Davis moon
projection. Retain Staging until the consumer/package prerequisite is reconciled.

## Decisions without an assigned implementation issue

- Decide the first central gateway-status audience: public status, private operator
  dashboard or both. Existing device-rich diagnostics are inputs.
- Design selective handling of permanent dead letters; automatic RetryExhausted
  requeue does not authorize replay of permanent failures.
- Decide which collectors need local history separate from outboxes, and complete
  the weather/BMS aggregates actually needed by readers.
- Decide whether shared typed ingest DTO/client packages reduce enough real drift
  to justify extraction; gateway sender routing/response mapping currently has owners.
- Decide when the archived brokered ingest POC justifies its operating cost.
- Plan separate least-privilege runtime/migration SQL/Entra identities and missing
  service-specific DR drills, including unresolved SQL disk identity.
- A central command inbox needs authorization, expiry, idempotency, audit, edge
  execution and fail-safe contracts. HA Kasa switching still needs load classification
  and operator approval; a replacement direct HVO Kasa path is not adopted.

[Future candidates](gateways/future-integrations.md) retains camera/NVR/PDU/roof/
motion research and safety. Govee currently belongs to [Home Assistant](../deploy/home-assistant/README.md).
The [prior roadmap](archive/2026-08-18-roadmap.md) preserves old priorities and
completed context; power observation identity and UTC queries are current reference.
