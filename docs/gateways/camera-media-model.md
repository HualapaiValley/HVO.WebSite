# Camera And Media Integration Model

## Status

- Phase 0 status: shared design note for Blue Iris, AllSky, and future image/video sources.
- Last updated: 2026-05-28

## Purpose

Camera and media systems should not be forced into the same gateway pattern as telemetry devices. Blue Iris and AllSky already have or will have native UIs. HVO should integrate with them as event sources, media providers, or consumers of HVO data rather than replacing their full UI.

## Integration Roles

| Role | Description | Example |
|------|-------------|---------|
| Event source | Sends notifications/events to HVO | Blue Iris motion alert |
| Media provider | Provides latest image, snapshot, clip, or timelapse | Blue Iris camera image, AllSky latest image |
| Metadata source | Provides status, camera identity, capture timestamp, health | AllSky status API |
| Consumer | Requests HVO data | AllSky requesting weather data |
| Proxy target | HVO links or proxies selected native assets | Snapshot URL through HVO auth |

## Existing Data Models In Repo

| Model | Area | Fields | Notes |
|-------|------|--------|-------|
| `AllSkyCameraRecord` | Legacy | `RecordDateTime`, `ImageType`, `CameraNumber`, `StorageLocation` | Existing all-sky image record table. |
| `CameraRecord` | Legacy/general | `RecordDateTime`, `ImageType`, `CameraNumber`, `StorageLocation`, `CameraType` | Generic camera record. |
| `SecurityCameraRecord` | Legacy/security | `RecordDateTime`, `ImageType`, `CameraNumber`, `StorageLocation` | Existing security camera record. |
| `WeatherCameraRecord` | Legacy/weather | `RecordDateTime`, `ImageType`, `CameraNumber`, `StorageLocation` | Existing weather camera record. |
| `ImageMetadata` | v9 | `CapturedAt`, `BlobPath`, `CameraId`, `ImageType`, `FileSizeBytes`, `WidthPx`, `HeightPx` | Current v9 metadata model. |

## Candidate Shared Concepts

| Concept | Suggested fields | Notes |
|---------|------------------|-------|
| HVO camera identity | `CameraId`, `DisplayName`, `SourceSystem`, `NativeId`, `Role`, `Location` | HVO metadata for organizing camera/media sources. These are not vendor fields unless mapped to a documented native value. Blue Iris camera short names may become `NativeId`. |
| Media asset | `CapturedAt`, `MediaType`, `ImageType`, `CameraId`, `Uri/BlobPath`, `Width`, `Height`, `Duration`, `FileSize` | Avoid copying full videos by default. |
| Camera event | `EventId`, `CameraId`, `EventType`, `StartedAt`, `EndedAt`, `Confidence`, `MediaRef`, `NativeEventId` | Motion/alert events. |
| Media proxy | `SourceSystem`, `NativeUri`, `ProxyUri`, `AuthPolicy`, `CachePolicy` | Protect native credentials. |
| Notification | `Severity`, `Category`, `Message`, `RelatedCameraId`, `RelatedEventId`, `AcknowledgedAt` | Cross-system alerts. |

## Security Rules Draft

- Never expose native camera credentials to browsers or the main site.
- Prefer short-lived proxy URLs or authenticated HVO endpoints for media.
- Do not copy high-volume or full-resolution media to cloud by default.
- Treat motion/video data as privacy-sensitive.
- Store event metadata separately from media bytes.
- Native UI remains primary for playback/search unless there is a specific HVO need.

## Open Questions

| Question | Why it matters | Status |
|----------|----------------|--------|
| Should central site store images, proxy to local systems, or both? | Storage/privacy/network design | Open |
| What is the retention policy for snapshots/clips/events? | Cost/privacy | Open |
| How should Blue Iris and standalone motion sensors deduplicate alerts? | Notification quality | Open |
| Should AllSky latest image be public, private, or mixed? | Public website design | Open |
