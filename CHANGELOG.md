# Changelog

One repository changelog records notable delivered behavior. Decision rationale
belongs in [project history](docs/PROJECT_HISTORY.md); original undated release and
deployment claims remain in the [complete historical changelog](docs/archive/undated-deployment-changelog.md).
Dates below are verified delivery dates, not inferred deployments.

## Unreleased

- [#466](https://github.com/HualapaiValley/HVO.WebSite/issues/466): JK BMS,
  SmartShunt and EG4 CI multi-arch publishing candidates, immutable registry
  Compose pins and publishing/rollback runbooks; deployment remains separate.
- Grouped navigation, useful active-project owners, corrected schema/headless
  references and marked protocol/model/session archives are proposed by
  [#416](https://github.com/HualapaiValley/HVO.WebSite/issues/416).

## 2026-10-29

- [#464 / PR #465](https://github.com/HualapaiValley/HVO.WebSite/pull/465):
  CI published the Davis prerelease `0.0.0-ci.464.1` for linux/amd64 and
  linux/arm64 to `registry.hualapaivalleyobservatory.org/hvo-davis`, index digest
  `sha256:75112b5aa5834ca89992c8f79f29c8926f5ddd9e48c9b8e2435ac42761231343`.
  [Publication and registry verification passed](https://github.com/HualapaiValley/HVO.WebSite/actions/runs/40923542379);
  the PR remains draft and no collector deployment is included.

## 2026-10-04

- [#417 / PR #438](https://github.com/HualapaiValley/HVO.WebSite/pull/438):
  canonical agent lifecycle/shared preparation references and current project/UI guidance.
- [#415 / PR #436](https://github.com/HualapaiValley/HVO.WebSite/pull/436):
  source-backed operations/testing, self-hosted website deployment, recovery ownership
  and unresolved hazards.
- [#414 / PR #435](https://github.com/HualapaiValley/HVO.WebSite/pull/435):
  quiescent SQLite checkpoints, isolated restore proof and preservation of later observations.
- Website/canonical foundation: interactive scoped dashboard lifecycle, disposable
  SQL acceptance, source/time power observation identity, durable ingest retry,
  complete UTC power windows, bounded weather queries and source/auth/proxy trust.
  [Decision entries](docs/PROJECT_HISTORY.md) link #400–#410 contracts.

## 2026-08-18

- Completed HA setup retained Kasa/Govee acquisition and presentation; permanent
  proxy/H5179 follow-up moved to #385. Exporter stayed disabled.
- Checksum-archived SolarAssistant outbox/data-protection volumes outside Docker
  storage before deleting the named retired volumes.

## 2026-08-14

- Retired direct SolarAssistant/TP-Link source, tests, deployments, containers and
  images after EG4 and HA assumed their responsibilities.

## Historical initial extraction - date unrecorded

- Extracted website, EF layer and shared themes from the HVOv9 monorepo with CI,
  development container and contributor metadata. The original 1.0.0 heading
  supplied no release date; this file does not invent one.
