# Historical review campaign

This preserves the instructions for review issues #187–#198. It is historical context, not the process for new assignments. Use [the current development process](../development/PROCESS.md) for new work.

## Implementation handoff workflow

After all review issues are complete (comments posted on #187–#198), the consolidated roadmap (#198) drives the next phase.

### Do not create one issue per finding

A review may surface 50–100 findings. Creating a separate GitHub issue and PR for each one is unworkable — it produces an unmanageable review queue and buries the important fixes in noise.

**Instead: group related findings into batches.** Each batch becomes one implementation issue and one PR.

### Batching rules

Group findings by **severity first, then by domain/area**:

| Batch | What it contains | Typical PR size |
|-------|-----------------|-----------------|
| **P0 batch** (one per area) | All P0 findings in a single project or domain | Small — P0s should be few and targeted |
| **P1 batch per domain** | All P1 findings in the same area (e.g., all CSS P1s, all async P1s, all security P1s) | Small-Medium |
| **P2 batch per project** | All P2 findings in one project | Medium |
| **P3 omnibus** | All P3 cleanups together | Medium — one PR, low-risk |

**Examples:**
- 6 P0 findings across 3 projects → 3 small P0 PRs (one per project)
- 18 P1 async findings → 2 PRs: one for gateway workers, one for the main site
- 30 P2 CSS findings → 1 PR (CSS changes are low-risk and easy to review together)
- 20 P3 naming cleanups → 1 omnibus PR

The goal is **5–15 total PRs**, not 50–100.

### Creating batch implementation issues

After the consolidated roadmap (#198) is posted, create one GitHub issue per batch:

```bash
gh issue create \
  --title "fix(P1/async): eliminate sync-over-async in gateway workers" \
  --body "## Findings to address

All P1 async findings from review #192 and #191 that affect gateway background workers.

Source reviews: #191 (logging), #192 (async/perf)

## Findings included
- <paste each finding block: file, evidence, recommended fix>

## Acceptance criteria
- [ ] No .Result / .Wait() in any worker class
- [ ] All EF Core calls use async methods
- [ ] dotnet build: 0 warnings, 0 errors
- [ ] Unit tests pass" \
  --label "P1,finding"
```

### How implementation agents read a batch issue

When assigned a batch implementation issue, an agent should:

1. `gh issue view <number>` — read the full finding list, evidence, and recommended fixes
2. `gh issue view <source-review-issue> --comments` — read the full review report for surrounding context
3. Implement all findings in the batch on one branch
4. Open a single PR — `Closes #<batch-issue>` in the body
5. Keep the PR focused: all changes in the batch should be in the same domain

### Severity triage

| Label | Action |
|-------|--------|
| `P0` + `finding` | Fix immediately — small focused batch, implement before any other work |
| `P1` + `finding` | Fix before next release — batch by domain, schedule promptly |
| `P2` + `finding` | Plan to fix — batch by project, add to backlog |
| `P3` + `finding` | Optional cleanup — one omnibus batch, low priority |

### Labels on batch issues

Every batch implementation issue must have:
- One of: `P0`, `P1`, `P2`, `P3` (the highest severity in the batch)
- The `finding` label
- Optionally: a domain label (e.g. `security`, `css`, `async`, `tests`)

---
