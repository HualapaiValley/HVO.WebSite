# Future Integration Candidates

Unselected systems retain research and safety questions here; Govee has a current HA owner. These are design candidates, not approved APIs, deployments or control contracts.

## Govee Environmental Sensors

Current owner: [Home Assistant](../../deploy/home-assistant/README.md), with native
H5074/H5075 govee_ble entities and the temporary isolated Pi hci1 transport.
[#385](https://github.com/HualapaiValley/HVO.WebSite/issues/385) owns permanent
proxy/RF placement, H5179 commissioning and temporary/fallback cleanup.
[#320](https://github.com/HualapaiValley/HVO.WebSite/issues/320) owns canonical HVO
history; the exporter remains disabled, unmapped and without source claims.
A new direct Govee gateway is not the adopted acquisition boundary.

Remaining decisions: verify exact firmware/revisions and additional model fields;
retain official/community protocol evidence and local-only transport assumptions;
choose a capability/sparse environmental model without pretending every sensor
matches Davis. Any selected controllable device needs a separate safety contract.
The earlier unselected-hardware placeholder is superseded by this actual ownership.

---

## Digital Loggers Power Controller / PDU

### Status

- **Phase 0 status**: Placeholder.
- **Confidence**: Low; exact model and API docs needed.

### Identity

| Field | Value |
|-------|-------|
| HVO manual subject | Digital Loggers power controller / PDU |
| HVO integration role | Source/controller |
| Hardware model | Needs confirmation |
| HVO project/service | None |
| HVO safety classification | High-risk command if switching observatory power |

### Research Targets

| Topic | What to find | Status |
|-------|--------------|--------|
| Exact model | Web Power Switch, PDU, Pro Switch, etc. | Needed |
| Official API docs | HTTP endpoints, auth, response formats | Needed |
| Outlet capabilities | Per-outlet state, labels, current/power telemetry | Needed |
| Command semantics | On/off/cycle/pulse, idempotency, timing | Needed |
| Authentication | Basic auth, digest, token, HTTPS support | Needed |

### Design Notes

- Treat all power-switching commands as local-only until explicit safety, confirmation, audit, and interlock requirements exist.
- Main site should show state/health first, not expose commands.

---

## Blue Iris Camera / NVR

### Status

- **Phase 0 status**: Placeholder.
- **Confidence**: Low until Blue Iris deployment and API docs are gathered.

### Identity

| Field | Value |
|-------|-------|
| HVO manual subject | Blue Iris camera/NVR system |
| HVO integration role | Event source and media proxy |
| HVO project/service | None |
| HVO safety classification | Mostly telemetry/media; commands possible but not primary |

### Research Targets

| Topic | What to find | Status |
|-------|--------------|--------|
| Blue Iris version | API behavior can vary by version | Needed |
| HTTP/JSON API docs | Auth, session, endpoints, status, camera list | Needed |
| Webhook/event support | Motion/alert event payloads | Needed |
| Snapshot/clip access | URL formats, auth, cache behavior, retention | Needed |

### Design Notes

- Do not rebuild Blue Iris playback or camera management UI.
- HVO should receive events and expose notification history/status.
- Media proxy must avoid leaking camera credentials.
- Central cloud should store event metadata and selected thumbnails only, not full-resolution video by default.

---

## AllSky Camera

### Status

- **Phase 0 status**: Placeholder.
- **Confidence**: Low until AllSky hardware/software is selected.

### Identity

| Field | Value |
|-------|-------|
| HVO manual subject | AllSky camera system |
| HVO integration role | Standalone system; possible weather consumer and image provider |
| Hardware model | Needs selection/confirmation |
| HVO project/service | None |
| HVO safety classification | Telemetry/media; commands TBD |

### Existing HVO Data Models

| Model | Fields | Notes |
|-------|--------|-------|
| `AllSkyCameraRecord` | `RecordDateTime`, `ImageType`, `CameraNumber`, `StorageLocation` | Legacy model |
| `ImageMetadata` | `CapturedAt`, `BlobPath`, `CameraId`, `ImageType`, `FileSizeBytes`, `WidthPx`, `HeightPx` | v9 model |

### Design Notes

- Treat AllSky first as a standalone system, not a full HVO-controlled gateway.
- HVO may expose a local weather endpoint for AllSky if needed.
- HVO may ingest or proxy latest image/status if useful.
- Avoid making AllSky operationally dependent on HVO unless intentionally designed.

---

## Roof / Dome System

### Status

- **Phase 0 status**: Placeholder.
- **Confidence**: Low until API documentation is provided and reviewed.

### Identity

| Field | Value |
|-------|-------|
| HVO manual subject | Observatory roof/dome system |
| HVO integration role | Safety controller/source |
| Hardware model | Existing external system; details needed |
| HVO project/service | None |
| HVO safety classification | Safety-critical |

### Research Targets

| Topic | What to find | Status |
|-------|--------------|--------|
| API documentation | Status endpoints, command endpoints, auth | Needed from owner |
| State model | Open/closed/opening/closing/fault/manual/auto | Needed |
| Interlocks | Weather, telescope position, physical sensors, manual override | Needed |
| Command semantics | Open, close, stop, park, reset, acknowledge fault | Needed |

### Safety Rules Draft

- Status-only integration comes first.
- No cloud commands until explicitly approved.
- Local commands, if added, require authentication, confirmation, interlock display, and audit log.
- Weather state, telescope/park state, and manual override state must be visible before any automated command.
- Fail-safe behavior must be documented from the vendor/system API, not inferred.

---

## Motion Sensors (TBD)

### Status

- **Phase 0 status**: Purchase and integration criteria placeholder.
- **Confidence**: Low; brand/model not selected.

### Identity

| Field | Value |
|-------|-------|
| HVO manual subject | Motion sensors TBD |
| HVO integration role | Event source |
| Hardware model | Not selected |
| HVO project/service | None |
| HVO safety classification | Telemetry/event unless paired with commands |

### Selection Criteria

| Criterion | Preference | Why |
|-----------|------------|-----|
| Local API/event support | Strongly preferred | Avoid cloud dependency and latency |
| Webhook/MQTT/event push | Preferred | Better than polling for motion |
| Battery level | Preferred for wireless sensors | Maintenance visibility |
| Timestamp quality | Required | Event ordering and notification correctness |
| Cloud-only dependency | Avoid if possible | Reliability/security |

### Design Notes

- Treat motion as an event stream, not a periodic measurement.
- Design idempotency around event timestamp + sensor id + event type.
- Main site should show recent events/notifications, not raw high-volume event spam.
