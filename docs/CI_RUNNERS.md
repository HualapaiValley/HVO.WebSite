# CI Runners

## Website Runner Pool

`HVO.WebSite` uses two repository-scoped self-hosted runners on the private `github-runner` host:

| Runner | Service | Labels |
|---|---|---|
| `github-runner-04` | `actions.runner.RoySalisbury-HVO.WebSite.github-runner-04.service` | `self-hosted`, `Linux`, `X64`, `hvo-website`, `ubuntu-24.04`, `dotnet`, `docker` |
| `github-runner-05` | `actions.runner.RoySalisbury-HVO.WebSite.github-runner-05.service` | `self-hosted`, `Linux`, `X64`, `hvo-website`, `ubuntu-24.04`, `dotnet`, `docker` |

The host has .NET 10, Docker with BuildKit, PowerShell, Chromium dependencies, and enough capacity to run `build-and-test` and `docker-smoke` concurrently. The repository is private; do not expose these runners to public or untrusted fork workflows.

## Cache Policy

NuGet and Playwright package caches persist under the `actions` account. Docker smoke removes each run-unique image tag but retains useful BuildKit layers within a hard 30 GB cache ceiling enforced before and after each smoke suite.

The Docker smoke script remains ephemeral by default for developer machines and GitHub-hosted runners. CI opts into persistent mode with `DOCKER_SMOKE_CACHE_MODE=persistent`.

## Operations

Check registration and status:

```bash
gh api repos/RoySalisbury/HVO.WebSite/actions/runners
ssh roys@github-runner 'systemctl status actions.runner.RoySalisbury-HVO.WebSite.github-runner-04.service actions.runner.RoySalisbury-HVO.WebSite.github-runner-05.service'
```

Check runner-host capacity and Docker cache:

```bash
ssh roys@github-runner 'df -h / && sudo docker system df'
```

Do not run deployment commands from CI. The runner executes build, test, policy, and smoke validation only.
