# Blue Iris Gateway Manual

## Status

- Phase 0 status: placeholder.
- Last updated: 2026-05-28
- Confidence: low until Blue Iris deployment/API docs are gathered.

## Identity

This table is HVO documentation metadata. It is not a list of Blue Iris API fields.

| Field | Value |
|-------|-------|
| HVO manual subject | Blue Iris camera/NVR system |
| HVO integration role | Event source and media proxy |
| Hardware/software model | Blue Iris server; camera models TBD |
| HVO project/service | None |
| Native UI exists | Yes, full-featured Blue Iris UI |
| Native UI is primary | Yes |
| HVO UI responsibility | Level 1: status, notifications, proxy/link selected media |
| HVO safety classification | Mostly telemetry/media; commands possible but not primary |

## Research Targets

| Topic | What to find | Status |
|-------|--------------|--------|
| Blue Iris version | API behavior can vary by version | Needed |
| HTTP/JSON API docs | Auth, session, endpoints, status, camera list | Needed |
| Webhook/event support | Motion/alert event payloads and delivery semantics | Needed |
| Snapshot/clip access | URL formats, auth, cache behavior, retention | Needed |
| Camera inventory | Camera short names, groups, enabled/disabled state | Needed |
| Security model | Whether HVO proxies credentials or uses service account | Needed |

## Expected Capability Areas

| Capability group | Read-only | Read-write | Command/action | Local UI | Outbox/cloud | Notes |
|------------------|-----------|------------|----------------|----------|--------------|-------|
| Camera status | Yes | Maybe | Enable/disable possible | Status | Candidate | Native UI remains primary. |
| Motion/alert events | Yes | No | No | Notifications | Event stream | HVO should ingest events. |
| Snapshots | Yes | No | No | Proxy/link | Maybe selected latest | Media privacy/security. |
| Clips/videos | Yes | No | No | Proxy/link | Maybe metadata only | Avoid unnecessary cloud media copies. |
| Profiles/schedules | Yes | Maybe | Change profile possible | Link/native | Local-only if ever | Command safety needed. |

## Design Notes

- Do not rebuild Blue Iris playback or camera management UI.
- HVO should receive events and expose notification history/status.
- Media proxy must avoid leaking camera credentials and should have explicit auth/caching rules.
- Central cloud should likely store event metadata and selected thumbnails only, not arbitrary full-resolution video by default.

## Open Questions

| Question | Why it matters | Status |
|----------|----------------|--------|
| Will Blue Iris send webhooks to HVO or will HVO poll? | Event design | Open |
| Which media should be proxied vs linked vs copied? | Privacy/storage | Open |
| Should Blue Iris commands ever be available from HVO? | Safety/security | Open |
