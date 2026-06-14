---
name: hybrid-review-resolution
description: Use when resolving PR review comments, local code-review findings, requested changes, or CI feedback with a cost-aware Qwen prep plus GPT final-fix workflow.
---

# Hybrid Review Resolution

Use this skill when the user wants to resolve review feedback while using local/free Qwen for comment inventory, triage, similar-issue search, and draft reply preparation, then using the configured primary/GPT model for final decisions, code edits, verification, and resolution judgement.

## Core Principle

Qwen output is preparation, not authority. The primary/GPT workflow must independently validate every review item, decide whether it is valid, implement any code changes, choose verification, and decide whether a PR thread or local finding is actually resolved.

## Supported Sources

- GitHub PR review threads, inline comments, top-level PR comments, requested changes, linked issue comments, and CI/check feedback.
- Azure DevOps PR threads, work item comments, and pipeline feedback when tooling is available.
- Local review artifacts, such as `code-review-*` markdown files, pasted findings, analyzer output, TODO lists, or a branch diff with no PR.
- Mixed sources where a PR exists but the user also provides local review files.

## Output Folders

- Prep agent output: `code-review-resolution-prep/`
- Final resolution output: `code-review-resolution-final/`

## Stage 0: Scope And Safety

1. Confirm the source type — a PR, local findings, or both.
2. For PR sources, verify `gh` CLI is authenticated and has access to the repo/PR.
3. For local findings, confirm the file path or paste location.
4. Select the prep agent: `qwen-review-resolution-prep` for cheap local triage, or a Go agent for deeper analysis.
5. Warnings:
   - Never push, merge, or force-push unless explicitly asked.
   - Never modify files outside the fix scope.
   - Never resolve PR threads yourself — the user or primary session handles that.

## Stage 1: Qwen Resolution Prep

1. Launch the prep agent with:
   - Source type and location (PR URL or local file path).
   - The list of comments, findings, or review items to triage.
   - Any user-provided context or preferences.
2. Save prep output to `code-review-resolution-prep/`.

## Stage 2: GPT Validation And Fix Plan

1. Read the prep output.
2. For each triaged item, independently validate:
   - Is the finding valid in the current code?
   - What is the root cause?
   - What is the smallest correct change?
3. Implement fixes — following the `review-resolution` skill's implementation rules.
4. For each fix, search for repeated instances of the same root-cause pattern across the codebase.
5. Verify each fix builds cleanly (zero warnings, zero errors).

## Stage 3: Verification

- Run the existing tests that cover the affected code.
- Confirm no regressions.
- For new code paths, add or update tests.

## Stage 4: Replies, PR Updates, And Resolution

- For PR threads: post a concise reply explaining what was fixed and how. Reference the commit SHA.
- For local findings: summarize the resolution in the output file.
- Do not mark threads resolved yourself — present the resolution for the user or primary session to confirm and resolve.

## Resolution Classifications

- `Fixed` — code change applied, tests pass, thread reply posted.
- `Not a bug / working as intended` — explained with evidence, thread replied.
- `Deferred` — valid but out of scope; filed as a follow-up issue.
- `Needs discussion` — requires product/design input before implementing.
- `Cannot reproduce` — explained, asked for more details.

## Output Format

Final resolution output saved to `code-review-resolution-final/` with:
1. Summary of items resolved, deferred, or disputed.
2. Per-item resolution details with evidence.
3. List of similar-issue fixes applied.
4. Verification results (build + test output).
5. Draft thread replies.

## Quality Gates

- Zero warnings, zero errors on build.
- All affected tests pass.
- No regression in unrelated tests.
- Each fix is the smallest correct change.
- Similar-issue search covers at least the same file and adjacent callers.

## When To Stop And Ask

Ask the user if:
- The prep output has low-confidence triage for critical items.
- A fix requires broader refactoring or breaking changes.
- A thread cannot be resolved without product/design input.
- CI checks fail after the fix.
- The prep agent did not produce usable output.
