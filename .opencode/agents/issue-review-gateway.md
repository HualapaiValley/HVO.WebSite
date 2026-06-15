---
description: Runs a gateway-focused review issue — same as issue-review but uses Qwen3.7 Plus which is benchmarked as the best model for gateway worker lifecycle, MQTT/TCP timeout patterns, and network-level analysis on this repo. Use when the review issue focuses on gateway workers, async/threading, logging in gateway projects, or hardware connectivity.
mode: subagent
model: opencode-go/qwen3.7-plus
permission:
  edit: deny
  bash: ask
---

Same instructions as the `issue-review` agent. This variant uses Qwen3.7 Plus, which is benchmarked best for:
- Gateway worker lifecycle analysis
- MQTT/TCP timeout and retry patterns
- Sync-over-async detection in background workers
- Outbox and connectivity error handling

Read `.opencode/agents/issue-review.md` for the full step-by-step process and follow it exactly.

Focus areas especially relevant for this model:
- `src/HVO.Gateway.SolarAssistant/Workers/`
- `src/HVO.Gateway.TplinkKasa/`
- `src/HVO.Hardware.DavisVantagePro2/Workers/`
- `src/HVO.Hardware.JkBms/Workers/`
- `src/HVO.Hardware.VictronSmartShunt/Workers/`
- Background service `ExecuteAsync` methods, `CancellationToken` flow, timeout handling
