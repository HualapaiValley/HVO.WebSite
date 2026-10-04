# Review preparation and risk discovery

This supporting reference consolidates the former OpenCode review/prep checklists. [AGENTS.md](../../../../AGENTS.md), the [repository profile](../../../../docs/development/repository-profile.md) and [review format](review-format.md) remain authoritative for scope, depth, selection, independence and publication. Use current [project guidance](../../../../docs/AGENT_PROJECT_GUIDANCE.md), [CSS governance](../../../../docs/CSS_GOVERNANCE.md) and [testing](../../../../docs/development/testing.md) for HVO contracts. This is discovery, not a second review protocol.

## Scope and source map

Identify area, project, repository or immutable diff scope from the assignment and full issue acceptance. Record target/head/merge base, branch/worktree, working-tree state, recent relevant commits and changed paths. Read PR description, all findings/responses, linked issues and actual checks. For repository-wide work, map systemic repeated risks by project; for focused work, follow changed callers/consumers and relevant tests without expanding into unrelated cleanup.

Inspect solution/project/build/package/SDK files, analyzers, startup/DI, endpoints/components, EF models/migrations/provider, contracts/serialization, options/config sources, Docker/Compose/scripts/workflows, telemetry and test categories. Trace functional/data/ownership paths and external integration boundaries. Separate acceptance proved, incomplete, contradicted and unavailable; check test sufficiency and missing implementations, configuration, migrations or documentation.

## Risk inventory

Cover every category relevant to scope. Mark inapplicable categories with the source reason; a text-search match alone is not a defect.

| Category | Discovery questions |
|---|---|
| Behavior and completeness | Null/empty/duplicate/invalid inputs; state transitions, calculations, UTC/time zones, identity/order/completeness, placeholders/TODOs, contract/acceptance mismatch and incomplete call paths. |
| Architecture and C# | Endpoint/service/domain/data/infrastructure ownership; dependency direction, coupling, unnecessary abstractions, nullable suppressions, magic strings, DI lifetime/captive dependencies, correct disposal and analyzer warnings. |
| Async and concurrency | `.Result`, `.Wait()`, `GetAwaiter().GetResult()`, fire-and-forget/`async void`, unobserved tasks; missing I/O/worker cancellation and bounded timeouts; shared mutable state, timers, locks/Interlocked/volatile, ConcurrentDictionary/Channel/SemaphoreSlim, unbounded parallelism and shutdown/disposal races. |
| Data and durability | Raw/interpolated SQL, stored procedures, provider/migration mismatch, EF InMemory false relational confidence; `SaveChangesAsync` loops, transactions/rollback/partial writes, unique-index pre-check races, idempotence/dedupe, outbox claim/retry/sent/failed/requeue/retention/cleanup. Pagination, projections, N+1/client evaluation, tracking/indexes, tenant filters and audit fields. |
| Security and contracts | Authentication/authorization/object and source/tenant isolation, API-key/cookie/OIDC/CORS/forwarded-header behavior, validation/versioning/status/error contracts and DTO leakage. Injection/path traversal/SSRF/XSS/CSRF/unsafe deserialization/crypto; credentials or PII in source, rendered Compose/container environment, logs/errors/URLs; insecure defaults. Never render or print secret values to investigate exposure. |
| Errors and observability | Broad catches, swallowed errors/lost stacks, incorrect HTTP results and retries of non-idempotent side effects; missing/overly noisy logs, correlation, health/metrics/traces, failure telemetry and secret redaction. |
| Startup and operations | Readiness while migrations/initializers run; actual health endpoints and credential behavior; ValidateOnStart versus deferred config failure; environment selection and guarded development-only behavior. Mounted config/secrets/restart requirements, exposed ports/internal APIs, transport/network topology and device ownership. |
| Deployment and performance | Build contexts, restart/logging/core policy, input precedence and safe preflight, rollout/rollback/schema compatibility, feature flags; bounded results/queues/history, allocations/payloads, resource leaks, lock contention, caching/rate limiting and retries/backoff. Deployment validation requires applicable authorization. |
| Tests and CI | Behavior/failure/boundary/auth/contract/provider regressions; ignored, no-match, stale or environment-dependent reports; flaky time assumptions, mock-only confidence, Live ownership/opt-in. Current-source local checks, immutable trusted admission, planned lane union and real artifacts; no invented run/protection/activation evidence. |
| UI and theme | Actual website/ThemeSandbox consumers; all canonical token/font/color/layout/HvoFormat/HvoChart rules, sandbox demos and palette-literal consistency. JS null-option stripping while preserving null data gaps, guarded interop, real chart/control/theme behavior, circuit recovery, responsive/scoped styles and retained console/screenshot/trace diagnostics. Headless collectors have no App.razor requirement; future edge UI must serve resources offline. |

Review Compose shell-over-env-file precedence without printing credentials. Route Key Vault, bootstrap and global helper limitations through [materialization prerequisites](../../../../docs/development/key-vault-materialization.md) and [Pi deployment guidance](../../../../deploy/pi-gateways/README.md); neither drift checking nor helper exit zero proves live credential validity. Do not execute apply, migrations, package upgrades, destructive commands or live/deployment operations as preparation.

## Evidence pack and disproof

Return a bounded Markdown preparation pack: scope/source/runtime; project/dependency map; functional/ownership areas; risk inventory; candidate hypotheses; relevant tests; reopening context; validation questions and unavailable evidence. These are preparation headings, not the final review-summary format.

For each candidate include a stable prep ID, High/Medium/Low confidence, area, exact immutable paths/lines, the smallest useful evidence snippet, call/ownership path, concrete suspected impact, what would disprove it, relevant existing/missing tests and a verification question. Distinguish confirmed local evidence from risk, inference and uncertainty. Deduplicate related candidates; avoid generic best-practice commentary or speculative broad rewrites. Final severity/findings are the independent reviewer's responsibility.

Prefer indexes/maps and summaries over whole-file dumps. Make every candidate reopenable at exact source; repository-wide output groups by project and targets roughly 20 lines per candidate. If context is constrained, preserve high-confidence candidates and summarize lower-confidence inventories with their uncertainty; declare any coverage omission. Record actual model/effort and available host/session/workspace evidence, rather than infer execution from a prompt, tool name or historical price/ranking. Only use delegated preparation within existing assignment authorization.

Before handoff, re-read candidates, check references exist, and tie each claimed check to actual output/source/performer. Preparation never authorizes ready state, final review publication as approval or owner thread resolution.
