---
name: hybrid-code-review
description: Use when the user asks for a cost-aware Qwen plus GPT code review, pre-review, evidence-pack review, repo review, project review, area review, or model-comparison review workflow.
---

# Hybrid Code Review

Use this skill when the user wants to use local/free Qwen for review grunt work and a stronger remote model for final validation, or when they ask for a code review of a specific area, project, solution, or entire repo while being mindful of model context limits and cost.

## Core Principle

Qwen output is preparation, not authority. The GPT workflow must independently validate every finding, assign severity, assess confidence, and decide whether to include it in the final review output.

## Supported Scopes

- `area`: one folder, feature, controller, service, or subsystem.
- `project`: one `.csproj` plus its nearest tests and dependencies.
- `repo`: the full repository or solution.
- `diff`: current branch or PR changes against a base branch, plus affected callers and tests.

## Output Folders

- Prep agent output: `code-review-qwen-pre/` (creates if missing).
- GPT validation output: `code-review-gpt55/` (creates if missing).

## Stage 0: Scope And Model Selection

Ask the user what scope they want. If unclear, default to `area` and ask for a specific folder or feature.

Select the prep model by editing the `model` field in `.opencode/agents/review-prep.md`. Options:

| Provider | Model | Best for |
|---|---|---|
| `opencode/deepseek-v4-flash-free` | DeepSeek V4 Flash (Zen free) | Broad scope, free. Best signal-to-noise among free models. |
| `opencode/nemotron-3-ultra-free` | Nemotron 3 Ultra (Zen free) | Broad scope, free. Better on medium repos. |
| `opencode/mimo-v2.5-free` | MiMo V2.5 (Zen free) | Broad scope, free. Fast but less thorough. |
| `opencode-go/deepseek-v4-pro` | DeepSeek V4 Pro (Go) | Best prep quality. Catches patterns GPT-level. |
| `opencode/qwen3.7-plus` | Qwen3.7 Plus (Go) | Good prep quality. Balanced cost-effectiveness. |
| `opencode/minimax-m3` | MiniMax M3 (Go) | Good prep quality. Different model bias. |
| `ollama/qwen3-coder-next-q8-256k:latest` | Qwen3 Coder Next (local) | Small scope, cheap. Good on targeted area reviews. |

For small/quick scans, use a free Zen model. For critical or deep reviews, use DeepSeek V4 Pro. If the Go provider is unregistered, use Zen or local Qwen.

## Stage 1: Pre-Review (Prep Agent)

1. Set `cwd` to the repository root.
2. Launch `review-prep` (the single parameterized agent from `.opencode/agents/review-prep.md`) with the user's scope description.
3. Confirm which output file name to use (default: `{model-shortname}-{area}-Review.md`).
4. The prep agent returns markdown. Save it to `code-review-qwen-pre/`.
5. Do not modify, edit, or massage the prep output.

## Stage 2: GPT Validation

1. Read the prep output from `code-review-qwen-pre/`.
2. Use your best reasoning (GPT-5.5-class) to perform the final review, evaluating each prep candidate and discovering any findings the prep agent missed.
3. Follow the `code-review` skill's Markdown Output Template and review standards.
4. Save final output to `code-review-gpt55/`.

## Context Limit Strategy

If the prep output is too large for the GPT model's context window:

1. Ask the prep agent to split its output by project or area.
2. Validate each part in a separate GPT turn.
3. Merge the results into a single output file with a combined summary.

Do not silently truncate the prep output. If context is tight, skip lower-confidence candidates rather than high-confidence ones.

## Quality Gates

- Every prep finding must be independently validated by GPT before inclusion.
- Epistemic confidence: GPT must state for each finding whether it agrees, disagrees, or needs more evidence.
- No severity inflation: the GPT review must assign its own severity labels based on direct evidence, not copy from prep.
- Prep-only findings that GPT cannot validate must be marked as `unvalidated` in the final output.

## When To Stop And Ask

Ask the user if:
- The prep output is empty or contains only low-confidence noise.
- The scope cannot be determined from the request.
- The prep agent failed or returned errors.
- The prep output is too large to validate in one pass.
- The GPT model is unavailable or too expensive for the scope.
