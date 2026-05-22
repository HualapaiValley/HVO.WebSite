# OpenCode Web Devcontainer Setup Notes

Date: 2026-05-22

## Goal

Run `opencode web` automatically inside the devcontainer, forward the web UI through VS Code, and keep all commands, file edits, tests, and source access inside the container.

## Starting State Observed

- Current `opencode` exists at `/home/vscode/.opencode/bin/opencode`.
- Current `opencode` version is `1.15.7`.
- `node`, `npm`, `python3`, and `python` were not available in the active shell when checked.
- The devcontainer already has a `postCreateCommand` and `postAttachCommand`, so the setup should not replace them.
- OpenCode web should bind to `0.0.0.0` so the devcontainer port forwarder can see it reliably.

## Changes Made

- Add port `4096` to VS Code `forwardPorts`.
- Add a `4096` port label: `OpenCode Web`.
- Add `postStartCommand` that runs `.devcontainer/start-opencode-web.sh`.
- Add `.devcontainer/start-opencode-web.sh` to start `opencode web --hostname 0.0.0.0 --port 4096` from `/workspaces/HVO.WebSite`.
- Update `.devcontainer/post-create.sh` to install `nodejs`, `npm`, `python3`, `python3-pip`, and `python3-venv`.
- Update `.devcontainer/post-create.sh` to install opencode with `curl -fsSL https://opencode.ai/install | bash` if `opencode` is missing.

## How To Test After Rebuild

1. Rebuild/reopen the devcontainer.
2. Confirm VS Code forwards port `4096` or manually forward it from the Ports panel.
3. Open `http://localhost:4096` in the host browser.
4. In the container, check the startup log if needed: `less /tmp/opencode-web.log`.
5. Confirm tools: `opencode --version`, `node --version`, `npm --version`, `python3 --version`.

## Recovery If Rebuild Or Startup Fails

- Disable automatic opencode startup by removing or commenting the `postStartCommand` entry in `.devcontainer/devcontainer.json`.
- If port forwarding causes issues, remove `4096` from `forwardPorts` and remove the `4096` entry from `portsAttributes`.
- If opencode install fails but the rest of the container is needed, comment out the `curl -fsSL https://opencode.ai/install | bash` section in `.devcontainer/post-create.sh`.
- If the server starts but the browser cannot connect, inspect `/tmp/opencode-web.log` and try running manually inside the container:

```bash
cd /workspaces/HVO.WebSite
OPENCODE_SERVER_PASSWORD=changeme opencode web --hostname 0.0.0.0 --port 4096
```

- If VS Code shows an extra `409` port, treat it as unrelated or stale unless `ss -ltnp` shows a process actively listening there. This setup only configures OpenCode web on `4096`.
