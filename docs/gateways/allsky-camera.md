# AllSky Camera Gateway Manual

## Status

- Phase 0 status: placeholder with existing legacy/v9 image model notes.
- Last updated: 2026-05-28
- Confidence: low until AllSky hardware/software is selected.

## Identity

This table is HVO documentation metadata unless a row explicitly references the selected AllSky system API. It is not a list of AllSky API fields.

| Field | Value |
|-------|-------|
| HVO manual subject | AllSky camera system |
| HVO integration role | Standalone system; possible weather consumer and image provider |
| Hardware/software model | Needs selection/confirmation |
| HVO project/service | None |
| Native UI exists | Expected yes |
| Native UI is primary | Yes |
| HVO UI responsibility | Level 1: status, link/proxy latest image/timelapse, weather API support if needed |
| HVO safety classification | Telemetry/media; commands TBD |

## Existing HVO Data Models

| Model | Fields | Notes |
|-------|--------|-------|
| `AllSkyCameraRecord` | `RecordDateTime`, `ImageType`, `CameraNumber`, `StorageLocation` | Legacy model. |
| `ImageMetadata` | `CapturedAt`, `BlobPath`, `CameraId`, `ImageType`, `FileSizeBytes`, `WidthPx`, `HeightPx` | v9 model for image metadata. |

## Research Targets

| Topic | What to find | Status |
|-------|--------------|--------|
| Exact AllSky software/hardware | Determines APIs/file locations | Needed |
| Image access method | HTTP latest image, file share, upload, API | Needed |
| Status API | Camera health, exposure, last capture, disk space | Needed |
| Timelapse/video access | URL/file/API and retention | Needed |
| Weather consumer needs | What weather endpoint/fields AllSky may request | Needed |
| Auth/security | Whether media endpoints need protection | Needed |

## Expected Capability Areas

| Capability group | Read-only | Read-write | Command/action | Local UI | Outbox/cloud | Notes |
|------------------|-----------|------------|----------------|----------|--------------|-------|
| Latest image | Yes | No | No | Proxy/link | Metadata and optional media | Native UI primary. |
| Timelapse/video | Yes | No | No | Proxy/link | Metadata maybe | Storage policy needed. |
| Camera status | Yes | No | Maybe restart? | Status | Gateway status candidate | Exact API TBD. |
| Weather consumption | HVO provides | No | No | Config | No outbox | AllSky may request weather from HVO. |

## Design Notes

- Treat AllSky first as a standalone system, not a full HVO-controlled gateway.
- HVO may expose a local weather endpoint for AllSky if needed.
- HVO may ingest or proxy latest image/status if useful.
- Avoid making AllSky operationally dependent on HVO unless intentionally designed.

## Open Questions

| Question | Why it matters | Status |
|----------|----------------|--------|
| Which AllSky platform will be used? | Determines API and integration | Open |
| Does AllSky need weather data from HVO, and in what schema? | Local API design | Open |
| Should images be stored centrally or only proxied? | Storage/privacy/cost | Open |
