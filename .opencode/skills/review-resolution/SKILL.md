---
name: review-resolution
description: Use when resolving GitHub or Azure DevOps PR/issue review comments, code review findings, requested changes, or reviewer feedback by fixing targeted issues, searching for similar defects, replying with resolution details, and marking threads resolved.
---

# Review Resolution

Use this skill after a PR, issue, or work item has been reviewed and the task is to address comments, requested changes, review threads, CI feedback, or code-review findings.

The goal is not only to patch the exact line mentioned by the reviewer. Resolve the underlying defect, search for similar instances, verify the fix, reply with what changed, and mark the review item resolved only when the issue is actually addressed.

## Core Rules

- Start from the review comments, issue comments, PR review threads, requested changes, CI failures, and linked work items.
- Treat each comment as a hypothesis about a defect. Confirm it against current code before changing anything.
- Fix the root cause, not just the symptom on the commented line.
- Search for similar issues in nearby code and across the repo when the pattern is likely repeated.
- Do not introduce broader refactors while resolving a focused review item.
- Do not mark a thread resolved until code, tests, and verification support that it is resolved.
- Reply to each resolved thread with a concise explanation of how it was addressed, then mark it resolved.
- If a comment is no longer valid, reply with evidence and mark it resolved when appropriate.
- If a fix requires a larger refactor or design decision, do not attempt the refactor. Call it out, leave or create a follow-up issue/task, and do not mark the original item as fully resolved unless the reviewer agrees or the current PR has a safe mitigation.
- If a requested fix conflicts with product intent, safety, or another review comment, pause and ask for direction.
- Avoid drive-by changes unrelated to the review items unless they are the same root-cause pattern.
- Preserve user changes and unrelated worktree changes.

## Agent And Reasoning Guidance

Use the primary model for final decisions, code edits, verification choice, and whether a thread is truly resolved.

Use subagents for non-trivial review resolution:

- Use an `explore` agent to inventory all review threads/comments and map each to current files and code paths.
- Use a second `explore` or `general` agent to search for similar patterns across the repo.
- Use a `general` agent for focused risk review of the proposed fixes when changes touch concurrency, security, data access, deployment, or public contracts.
- Use agents for research only unless the user explicitly asks for autonomous edits by agents.
- Reconcile subagent results yourself; do not blindly implement every suggestion.

Suggested parallel split for larger reviews:

- Thread triage agent: list comments, severity, current validity, target files, and proposed disposition.
- Similarity agent: search for repeated bug patterns and summarize exact additional locations.
- Verification agent: identify existing tests/build commands and missing regression coverage.

For small PRs or one-line comments, inspect directly without subagents.

## Review Comment Triage

For each review item, classify it before fixing:

- `Valid and fix now`: the comment identifies a real issue that fits the PR scope.
- `Valid but broader refactor`: the comment is real, but fixing properly requires a larger redesign or follow-up PR.
- `Already fixed`: current code already addresses it.
- `Outdated`: the commented code no longer exists or changed enough that the original concern no longer applies.
- `Not reproducible / cannot verify`: context or evidence is insufficient.
- `Disagree`: the requested change appears incorrect or harmful; explain with evidence and ask for reviewer/user direction.

Track every item with:

- PR/issue number
- thread/comment URL or ID
- file and line when available
- reviewer concern
- classification
- fix or disposition
- verification performed
- reply text to post
- resolve/not-resolve decision

Use `todowrite` for multi-comment resolution work.

## Platform Workflow

When authenticated tooling is available, use native platform comments and thread resolution.

GitHub workflow with `gh`:

```bash
gh pr view <number> --json title,body,reviewDecision,statusCheckRollup,comments,reviews
gh api repos/<owner>/<repo>/pulls/<number>/comments --paginate
gh api graphql ... reviewThreads ...
gh pr diff <number>
gh pr checks <number>
gh pr comment <number> --body "..."
```

Use GraphQL to resolve review threads after replying:

```graphql
mutation($threadId: ID!) {
  resolveReviewThread(input: { threadId: $threadId }) {
    thread { id isResolved }
  }
}
```

Reply to a review thread/comment before resolving it. If exact reply APIs are unavailable or awkward, post a top-level PR comment that references the thread URL/comment and explains the resolution, then resolve the thread if appropriate.

Azure DevOps workflow:

- Use `az repos pr` or the ADO REST API when configured.
- Fetch active threads and comments.
- Reply to each thread with resolution details.
- Mark thread status resolved/closed only after verification.
- If ADO auth/tooling is unavailable, provide paste-ready replies and state that threads were not updated.

## Fix Strategy

Before editing:

1. Confirm the current branch and worktree state.
2. Identify base branch and PR branch.
3. Read the relevant review thread/comment and current code.
4. Inspect nearby conventions and existing tests.
5. Search for similar patterns if the issue may repeat.
6. Decide whether the fix is in scope.

When editing:

- Make the smallest correct change that addresses the root cause.
- Add or update tests for behavior changes, regressions, edge cases, and similar repeated issues.
- Prefer existing architecture and conventions.
- Avoid broad refactors, package upgrades, or unrelated cleanup.
- Avoid backward-compatibility code unless there is a real compatibility need.
- Preserve public contracts unless the review item explicitly requires a contract change.

After editing:

- Re-run focused tests first.
- Run broader build/test checks appropriate to the repo and risk.
- Run formatting/whitespace checks when available.
- Re-review the fix for regressions and new issues.
- Push updates only when requested or when resolving an existing PR branch task.
- Reply to review comments with exact resolution and verification.
- Mark threads resolved only after the reply and verification.

## Similar-Issue Search

For every valid review item, ask whether the same issue could exist elsewhere.

Search by:

- same method/API usage
- same configuration key or env var pattern
- same exception handling pattern
- same async/timer/background-service pattern
- same DTO/property/validation rule
- same SQL/EF query shape
- same security or logging pattern
- same UI component or test pattern

If similar issues are found:

- Fix them in the same PR only if they are the same root cause and low-risk.
- Add tests covering the generalized behavior where practical.
- Mention them in the reply or PR summary.

If similar issues are broader or risky:

- Document them as follow-up work.
- Do not expand the current PR into a large refactor without user confirmation.

## Large Refactor Or Follow-Up Criteria

Do not attempt the fix in the current PR when it requires:

- changing major architecture or dependency direction
- large public API or database schema redesign
- migration strategy beyond the PR scope
- broad security model changes
- replacing a subsystem or package
- substantial UI/UX redesign
- high-risk deployment workflow changes
- touching many unrelated modules
- unclear product requirements or acceptance criteria

For these cases:

- Add a PR/issue comment explaining why it is larger than the current review item.
- Describe the risk of leaving it as-is.
- Propose a concrete follow-up issue title and acceptance criteria.
- If needed, add a minimal safe mitigation in the current PR.
- Leave the thread unresolved unless the reviewer agrees the follow-up is acceptable or the current PR includes a sufficient mitigation.

## Verification

Choose verification based on risk and repo conventions.

Common checks:

```bash
git status --short --branch
git diff --check
dotnet build <solution-or-project> --no-restore
dotnet test <solution-or-project> --no-build --filter "TestCategory!=Live" --logger "console;verbosity=minimal" --blame-hang-timeout 30s
dotnet format --verify-no-changes
```

For scripts, run syntax or focused checks where available, such as:

```bash
python3 -m py_compile <script.py>
```

For UI changes, use existing Playwright/e2e tests only when they are configured and safe. Live tests must stay opt-in unless the user explicitly requests live verification.

Do not claim resolution if verification fails. Fix the failure or report the blocker.

## Reply Templates

Use concise, evidence-based replies.

Fixed with code and tests:

```text
Resolved in <commit>. The fix <what changed>. I also checked for the same pattern in <scope> and found/fixed <summary>. Verified with `<command>`.
```

Already fixed/outdated:

```text
This is no longer present in the current diff. The code now <evidence>. Verified by inspecting <file/method> and <optional command>.
```

Valid but follow-up:

```text
This is valid, but a complete fix requires <scope/refactor>. I did not fold that into this PR because <risk>. Proposed follow-up: <issue title> with acceptance criteria <short list>. Current PR impact is <mitigation/risk>.
```

Disagree/request direction:

```text
I do not think this change is safe as requested because <evidence>. The current behavior is <why>. Please confirm whether you want <option A> or <option B>.
```

## Output Format

When reporting back to the user, use this structure.

```markdown
# Review Resolution Summary

## Scope
- PR/issue/work item:
- Threads/comments reviewed:
- Branch/commit:

## Resolved Items
- Comment/thread: <URL or file/line>
- Concern:
- Resolution:
- Similar issues searched:
- Verification:
- Thread status:

## Deferred Items
- Comment/thread:
- Why deferred:
- Proposed follow-up issue title:
- Suggested acceptance criteria:
- Current PR risk/mitigation:

## Not Changed
- Comment/thread:
- Reason:
- Evidence:

## Verification Run
- `<command>`: passed/failed/not run

## Remaining Risk
- Risk or unverified context.

## Next Steps
- Concrete next action.
```

If all items were resolved, say so clearly. If any thread could not be replied to or resolved due to permissions/tooling, list it with paste-ready reply text.

## Final Checklist

Before finishing:

- Every review item was triaged.
- Valid items were fixed or explicitly deferred.
- Similar patterns were searched when appropriate.
- Tests or checks were run, or inability was documented.
- The fix was reviewed for newly introduced issues.
- Replies were posted for resolved/deferred/disagreed items when tooling allowed.
- Threads were marked resolved only when appropriate.
- Final response lists exactly what was resolved, deferred, or left open.
