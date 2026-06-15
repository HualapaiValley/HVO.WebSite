# .codex — Task Queue for Codex and Cloud Agents

Codex runs in a sandboxed environment with no internet access and no `gh` CLI.
Issue descriptions must be placed as local files here so the agent can read them.

## Workflow

1. When creating a Codex task, copy the GitHub issue body to `.codex/tasks/<issue#>-<slug>.md`
2. Submit the Codex task with this prompt:
   ```
   Read .codex/tasks/<filename>.md for the full task description and requirements.
   Follow AGENTS.md for all operating rules, workflows, and output format.
   ```
3. When the task is complete (PR merged or report delivered), move the file to `.codex/tasks/completed/`

## Why this exists

Codex cannot access the GitHub API or `gh` CLI from its sandbox.
It can only read local files from the checked-out repository.
Placing task descriptions here makes every task self-contained.

## Current tasks

See files in this directory.

## Completed tasks

See `.codex/tasks/completed/`
