# Documentation Index

This directory holds current reference, operational notes, discovery findings, and the consolidated future-work roadmap.

Production currently uses direct headless vNext collectors for Davis, JK BMS, EG4, and SmartShunt. Home Assistant owns Kasa and Govee acquisition and presentation. The implemented HA exporter is intentionally disabled with no production mappings or source claims. The direct SolarAssistant and TP-Link/Kasa applications, containers, images, and Docker volumes have been retired.

## Current Reference

- `ARCHITECTURE.md`: current system baseline, deployment boundaries, and active data-flow direction
- `FUTURE_WORK.md`: single consolidated list of open future work after the shared outbox migrations
- `CONTAINER_PUBLISHING.md`: self-hosted registry publishing workflow and versioning process
- `development/key-vault-materialization.md`: existing root-bootstrap inputs and whole-helper secret synchronization scope
- `WEBSITE_DATA_PROTECTION.md`: website key-ring deployment, backup, restore, rotation, and rollback
- `SHARED_INFRASTRUCTURE.md`: reusable hvo-docker infrastructure and observability stacks, storage policy, and migration guidance
- [Self-hosted website deployment](../deploy/hvo-docker/README.md): current website prerequisites, configuration, identity/key-ring and authorized rollout
- [Gateway operations](GATEWAY_OPERATIONS.md) and [Pi commissioning](../deploy/pi-gateways/README.md): current endpoint/auth, mounted secret and source-authority contracts
- [CI runners](CI_RUNNERS.md): hosted execution and review admission; [testing](development/testing.md) owns lane/fixture prerequisites
- `PROJECT_HISTORY.md`: session-by-session summary of recent work, decisions, and follow-up context
- `CSS_GOVERNANCE.md`: active CSS and theme authoring policy for all projects

Project history guidance:

- Keep `PROJECT_HISTORY.md` updated as work happens or at the end of a session.
- Keep entries short and curated.
- Capture decisions and next-context notes, not a command transcript.

## Current Discovery And Design Notes

- `SMARTSHUNT_PLAN.md`: SmartShunt data inventory and BLE/reference notes; implementation status now follows `ARCHITECTURE.md`
- `davis-console-nonloop-values.md`: Davis console field inventory and UI-relevant protocol values
- `JKBMS_SESSION_LIFECYCLE.md`: JK BMS session-lifecycle notes for the current BLE runtime direction

## Reference Assets

- [Former ACA deployment](archive/website-container-app.md): historical, superseded record preserving identity/blob/wrapping-key migration evidence; old `WEBSITE_CONTAINER_APP.md` remains a redirect

- `VantageSerialProtocolDocs_v261.pdf`: Davis protocol reference PDF used by the Davis collector notes
