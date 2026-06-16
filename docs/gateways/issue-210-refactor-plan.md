# Issue 210 Refactor Plan

Issue #210 is intentionally split into focused PR-sized changes. This PR handles the low-risk extraction work that preserves endpoint contracts and gateway behavior:

- `PowerIngestController` delegates normalized reading ingest to `IPowerReadingIngestService`.
- `BmsController` delegates device upsert, reading persistence, snapshot comparison, transaction orchestration, and alarm transitions to `IBmsIngestService`.
- Davis astronomical chart construction is extracted to `DavisAstronomicalChartBuilder` with unit coverage.

## Outbox And Contract Convergence

Do not switch gateway persistence and forwarding implementations opportunistically in the controller/UI refactor PR. Davis and TplinkKasa should converge on `HVO.Edge.Contracts` and `HVO.Edge.Outbox` in separate gateway-focused PRs with live gateway verification.

Recommended sequence:

1. Add a Davis adapter that maps existing Davis live/archive payloads to `HVO.Edge.Contracts` DTOs while keeping the current local SQLite outbox schema intact.
2. Replace Davis custom forwarding with `HVO.Edge.Outbox` after adapter tests prove payload parity.
3. Add TplinkKasa contracts for device status, command capability, and telemetry snapshots.
4. Replace TplinkKasa forwarding with `HVO.Edge.Outbox` and verify `deploy/pi-gateways/tplink-kasa/docker-compose.yml` environment settings still cover all required endpoint/API-key values.
5. Run targeted gateway verification on devpi5 for Davis and TplinkKasa before removing any legacy outbox code.

## Repository Pattern Decision

`src/HVO.DataModels/Repositories/Repository.cs` and `IRepository.cs` remain unused and are tied to the legacy `HvoDbContext`. Do not revive them for v9 ingest work. Current API services should continue using explicit, async EF Core queries through `HvoV9DbContext` so query shape, projection, idempotency checks, and cancellation remain visible at the service boundary.

If repository cleanup is desired, remove the legacy repository files in a separate `HVO.DataModels` cleanup PR after confirming no external consumers reference them.
