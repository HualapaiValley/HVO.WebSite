# Home Assistant configuration

This directory contains source-controlled Home Assistant configuration for the
observatory instance. It intentionally excludes `.storage`, secrets, generated
state, and acquisition logic.

## Kasa power dashboard

`configuration/dashboards/hvo-kasa.yaml` provides the first HVO presentation
slice using the commissioned TP-Link entities on `192.168.1.0/24`.

- Critical infrastructure is status-only. The dashboard does not provide
  actionable controls for routers, network switches, Proxmox hosts, cameras,
  roof equipment, or telescope equipment.
- Permitted switch and light actions are available only from the tile icon and
  always show a confirmation dialog.
- Parent power-strip controls, LED settings, unnamed outlets, restart buttons,
  and cloud diagnostics are omitted.
- The dashboard uses only built-in Home Assistant cards and remains independent
  of internet-hosted resources or custom frontend packages.

## Deployment

The deployment script uses the Proxmox QEMU guest agent to copy the tracked
files into the HA OS configuration volume. It never writes `.storage`.

Prerequisites:

- SSH access to the Proxmox host.
- `HOME_ASSISTANT_TOKEN` in the ignored root `.env` or current environment.
- HA OS VM `101` running with the QEMU guest agent.

Validate entity references without changing HA:

```bash
./scripts/deploy-home-assistant-dashboard.sh --check
```

Deploy, run Home Assistant configuration validation, and restart Core:

```bash
./scripts/deploy-home-assistant-dashboard.sh --apply
```

Optional overrides:

```bash
HVO_PROXMOX_HOST=root@192.168.1.240 \
HVO_HOME_ASSISTANT_VMID=101 \
HVO_HOME_ASSISTANT_URL=http://192.168.1.113 \
./scripts/deploy-home-assistant-dashboard.sh --apply
```

The script creates a timestamped copy of `configuration.yaml` in `/config`
before adding the single `lovelace: !include hvo/lovelace.yaml` declaration. If
HA validation fails, it restores the prior main configuration and exits without
restarting Core.

After deployment, run the focused live Playwright test with:

```bash
HVO_HOME_ASSISTANT_URL=http://192.168.1.113 \
HOME_ASSISTANT_TOKEN=<long-lived-token> \
dotnet test tests/HVO.WebSite.PlaywrightTests \
  --filter "FullyQualifiedName~HomeAssistantKasaDashboardPlaywrightTests"
```

The test opens the dashboard at desktop and mobile widths. It does not click a
switch or call any device service.
