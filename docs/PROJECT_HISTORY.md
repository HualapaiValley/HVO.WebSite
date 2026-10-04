# Project history

Decision-oriented context; release notes belong in the [root changelog](../CHANGELOG.md). Keep entries short, dated and source-backed. Complete prior sessions, including hardware measurements, private-GATT research and June model experiments, are preserved in the [source archive](archive/2026-10-04-project-history-source.md); observation dates not recorded there remain unknown.

## 2026-10-04 - Documentation ownership and preservation (#416 candidate)

Grouped navigation now names all active project owners. EF schema references follow actual singular entities/context/migrations; headless collector manuals distinguish implemented bounded commands from future control. Complete marked archives preserve protocol/session/model/deployment evidence before old planning pages become compatibility pointers. Staging remains production-used pending #372/SDK #82; HA exporter stays disabled and authority cutovers retain the #414 contract. This candidate does not perform deployment, hardware writes, migration application or missing DR drills. The [disposition ledger](archive/2026-10-04-documentation-dispositions.md) accounts for every original review path and later guide.

## 2026-10-04 — Unified agent guidance and current UI consumers (#417)

OpenCode review/correction skills now route to the canonical `.agents` lifecycle;
one preparation role uses shared risk/disproof/context and correction checklists.
Claude/Copilot adapters share that profile without a competing review format or
fixed-model claim. Project instructions follow headless mounted-config/secret-file
collectors and owned MSTest/browser fixtures. CSS governance retains token/font/
palette/layout and sandbox sign-off requirements for the actual website and
ThemeSandbox consumers. Adoption/failure history remains source-bound and separate
from current authority; no workflow gate, application behavior, live operation or
repository protection setting changes in this batch.

Current JK MQTT documentation explicitly retains its existing secret-gated
settings-password button/command, positive ACK and DeviceInfo password readback.
That bounded credential-write exception does not imply generic BMS control,
explicit settings-query support or new live hardware qualification.

## 2026-10-04 — Source-backed operations and recovery ownership (#415)

Current runbooks now follow shared endpoint/auth and per-sender retry behavior,
mounted config/secret files, guarded HA transport, hardware-free fixtures and
hosted selective CI. Added a self-hosted website deployment entry; preserved the
former ACA identity/blob/protector record as explicitly superseded history.
Recovery ownership distinguishes existing procedures from missing service drills,
including the unresolved August SQL disk identity. No deployment, live hardware,
credential rotation, schema or runtime policy change is part of this batch.

Correction review added the shared [root-bootstrap/materialization prerequisites](development/key-vault-materialization.md),
qualified gist preview versus credential checking and HA agent-backed backup
creation, and documented the existing SQL derivation error-propagation hazard.
The helper's runtime behavior remains a separate reviewed-change boundary.

## 2026-10-04 — Verified gateway rollback checkpoints (#414)

Replaced SmartShunt's active-volume tar and destructive restore recipe with a
shared quiescent full-volume backup contract. A usable checkpoint now requires a
durable, explicitly owned archive, checksum, SQLite integrity and isolated restore
proof. Rollback preserves the current volume and later observations, restoring an
incompatible older schema only into a separate recovery volume with explicit
reconciliation. Davis/JK historical cutover evidence is retained; future #352 HA
authority migration stays separate. See [backup and rollback](gateways/sqlite-backup-and-rollback.md).

## 2026-10-04 — Canonical v9 weather query boundary (#410)

Added a separate scoped, typed weather query service for new website features over
v9 Davis raw/archive observations. Queries require a station, explicit UTC ranges,
bounded continuation and freshness/no-data handling. Existing legacy weather APIs
and stored-procedure historical access remain unchanged; external production writers
and consumers remain an explicit operational inventory rather than an absence claim.
See [canonical weather queries](development/canonical-weather-queries.md).

## 2026-10-04 — Canonical retry durability and BMS event-time history (#400)

- Reconcile conflicts against committed identities and keep unresolved records retryable;
  clear failed EF state before reuse and preserve atomic child writes.
- Serialize BMS mutation on SQL device rows and rebuild paged event-time suffixes,
  preserving raw observations and stable unaffected intervals across delayed delivery.
- Coordinate snapshot content proof with #402 while retaining public contracts.
  [Behavior and compatibility](development/canonical-ingest-retries.md) describe the boundary.

## 2026-10-04 — Complete UTC power-history windows (#405)

- Select deterministic source/bucket representatives before loading detail JSON, retaining
  the full requested range instead of silently capping newest raw rows.
- Emit complete bounded UTC grids with explicit null outage gaps; preserve PV skew/subtotal,
  source/device isolation and battery-facing signs. Capture and retain both query boundaries.
- Restore stored UTC timestamp kinds, use the injected clock and existing visible display
  zone policy. [Query and compatibility notes](development/power-history-utc.md) explain
  payload-work bounds, malformed-data fallback and timezone behavior.

## 2026-10-04 — Power snapshot observation identity (#402)

- Replaced historical source/content uniqueness with submitted source/time observation
  identity, retaining recurring states and fresh unchanged confirmations without rewriting
  old rows. Inventory/configuration remain producer-driven observed snapshots; no new
  periodic producer or confirmation timestamp is invented.
- Kept content proof for identical replay and rejected different content at an immutable
  identity. Latest/freshness queries retain source chronology and exclude impossible
  future/default timestamps. #400 owns matching uniqueness-race acknowledgement proof.
- Added the non-destructive index migration and actual SQL upgrade/downgrade coverage;
  [semantics and rollout guidance](development/power-observation-identity.md) documents
  per-type producer/reader evidence, storage growth and rollback limits.

## 2026-10-04 — Canonical SQL Server test boundary (#409)

- Added a disposable, owned SQL Server lane using the actual production EF registration,
  real clean/prior migrations, canonical queries, conflict and atomicity checks.
- Kept fast InMemory/SQLite tests and separated the provisioned SQL category from HA
  integration. Standard reviewed CI now requires the SQL lane and retains its report.
- Test-only databases/credentials/resources never use application connection settings.
  [Fixture guidance](development/sql-server-integration-tests.md) records tested upgrade
  checkpoints and reusable concurrency seams for #400/#402/#405/#410.

## 2026-10-04

### Interactive website foundation

- Established a Routes-owned interactive shell while retaining public/authenticated SSR and Entra policy handling. Dashboard reads begin after interactive render; shared layouts no longer retain anonymous navigation-event handlers.
- Added scoped typed dashboard operations and a cancellable, serialized refresh lifecycle. Current readings and history have independent cadences; current-time presentation and content/window history revisions continue updating without new row counts.
- Provider failures retain independently successful sections and expose retry; an interactive dashboard boundary contains unexpected rendering failures. Browser fixtures use the actual host with test-owned clock/providers/authentication, without production testing switches.
- #405 owns history projection refinements. Deployment, hardware and exporter enablement remain separately authorized work.

## 2026-10-03

### Shared Development Process Pilot

- Added the shared issue/epic/review/PR procedures and explicit AGENTS/Claude/Copilot routing while preserving project engineering guidance in `docs/AGENT_PROJECT_GUIDANCE.md`.
- Standardized actual author/reviewer/model/effort provenance, validation performers, independently verified finding threads, and the draft -> review -> gated CI lifecycle.
- Kept main/squash and operational authorization boundaries; archived the fixed #187–#198 campaign instructions as history. Adoption is tracked by #394; metadata automation becomes active once adopted on main.
- Excluded the archived `HVO.Database.sqlproj` from the active solution at the owner's direction. Preserved its SQL reference files and direct file-reading tests; EF Core migrations remain authoritative. The active build retains the zero-warning requirement.
- The owner adopted PR #395 and its first hosted main build/test and Docker smoke passed. Live controller qualification exposed GitHub's additional contents-write requirement for draft conversion; the follow-up scopes that capability to the trusted metadata job and requires a live token check. Independent follow-up review also identified that Actions run/job metadata associates target-event runs with the PR head; the controller now separates that association from trusted base admission and tested-merge binding, with realistic success/failure regression coverage. Candidate CI stays read-only; branch-protection activation remains open until qualification finishes.

## 2026-08-18

### Home Assistant Core Setup Completion

- Closed the broad Home Assistant setup scope after validating Mosquitto, dedicated credentials, 15 Kasa parent integrations, managed YAML deployment, the reviewed ESPHome proxy definition and transport, and native H5074/H5075 `govee_ble` entities.
- Kept the HA telemetry exporter disabled with no production mappings or source claims.
- Moved permanent HVO Bluetooth proxy placement, temporary bridge retirement, HOME fallback-credential cleanup, and H5179 commissioning to focused follow-up issue #385.

### Direct Gateway Retirement Completion

- Archived the retired SolarAssistant outbox and data-protection volumes to `/home/roys/backups/hvo-issue-330` on `devPi5`.
- Verified both archives by SHA-256 and tar listing before deleting only the two explicitly named SolarAssistant Docker volumes.
- Confirmed Davis, JK BMS, EG4, and SmartShunt remained healthy and retained all active outbox and data-protection volumes.

## 2026-08-14

### Direct Gateway Retirement

- Retired the direct SolarAssistant and TP-Link/Kasa applications, tests, deployment definitions, containers, and images.
- Confirmed the active direct headless vNext collectors are Davis, JK BMS, EG4, and SmartShunt.
- Confirmed Home Assistant owns Kasa and Govee acquisition and presentation.
- Kept the implemented HA exporter intentionally disabled with no production mappings or source claims.

## 2026-05-24 - Collector ownership and BLE research

Kept Staging because real Davis code and tests consume the bridge; moved JK console
utilities to tools and retained Themes as active shared assets. Deferred and removed
the broker POC. Persistent per-device JK sessions/backoff and seven-device baseline
measurements informed isolation of mixed BLE workloads. Paired public SmartShunt
GATT became the collection baseline; private protocol and adapter experiments
remain historical evidence, not current enrichment or a new write permission.
The [complete session](archive/2026-10-04-project-history-source.md) retains exact
counts, captures, hardware/kernel identifiers, settings/history/private fields,
deployment observations and deferred questions; later append observation dates
are not inferred from this section heading.

## 2026-06-13 - Review experiments later superseded

Compared model prep outputs; removed a hanging token plugin and retained uncertainty
about exact token telemetry. June fixed-model, price and catalog statements are
[historical experiments](archive/2026-06-model-review-experiments.md). #417 adopted
canonical source-bound independent review using actual live capabilities; no old
ranking or prep role can approve its own implementation.

## Recovery evidence retained

Current [website data protection](WEBSITE_DATA_PROTECTION.md) owns persistent key
rings, HVO.WebSite.v9 application identity, encrypted wrapping-key version access
and rollback. [Former ACA/identity migration](archive/website-container-app.md)
retains old plaintext-restricted archive and hvoobs-kv protector evidence; old key
versions remain needed to unwrap old rings. Retired SolarAssistant archive paths
above remain recovery evidence. [Shared infrastructure](SHARED_INFRASTRUCTURE.md)
and [gateway operations](GATEWAY_OPERATIONS.md) distinguish written procedures
from missing service DR drills and the unresolved August SQL disk identity.

## 2026-10-04 — Ingest trust boundaries (#401)

Source authority now covers every source-bearing weather/power single, batch, archive and snapshot write through a shared check; unreserved legacy ingest remains supported and SmartShunt keeps exact ownership. Authentication cache entries carry hard UTC expiry and bounded deadlines using the injected clock. Forwarded header configuration validates explicit peers/networks at startup, preserves loopback defaults even with the framework host switch, and uses one trusted hop without a manual scheme override. Independent F1 review exposed duplicate framework/application forwarding: the application now defers to the active host startup filter so all switch combinations consume one hop, qualified by real-environment trusted multihop and OIDC regressions. The route discovery matrix and actual-host OIDC tests document the security boundary; deployment topology and Entra rollout verification remain operator work. See [ingest trust boundaries](development/ingest-trust-boundaries.md).

SQL-provider acceptance reuses the same route inventory and payloads against the disposable #409 fixture. It checks actual denied/authorized persistence, mixed batches, case/accent/trim aliases, competing reservations and hard UTC expiry with primed and empty authentication caches. SmartShunt's already-trimmed payload contract is preserved. Test collation changes remain confined to owned disposable databases.
