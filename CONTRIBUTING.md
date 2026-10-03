# Contributing to WebSite

Use the same [development process](docs/development/PROCESS.md) as agent contributors. The [repository profile](docs/development/repository-profile.md) defines local validation, `main`, squash merging and the available review route. [Project guidance](docs/AGENT_PROJECT_GUIDANCE.md) and [CSS governance](docs/CSS_GOVERNANCE.md) retain WebSite-specific requirements.

## Issue work

Read the complete issue, comments, acceptance and dependencies. Check assignees, claims and linked PRs before taking ownership. Assign the accountable owner, apply `workflow:in-progress` and one review-depth label, and post a friendly [Work started comment](docs/development/PROCESS.md#work-started-comment) with the plan, branch, review needs, delivery boundary and next checkpoint. Keep technical provenance in expandable details. Re-read the record to catch competing claims; age alone does not authorize takeover.

Use a dedicated branch from `main`, preferably `<type>/<issue>-<description>` (`feat`, `fix`, `docs`, `test`, `refactor`, `chore`; existing `feature/` branches are accepted). Implement the defined acceptance, run applicable validation and keep progress/handoff durable. Retain ownership through review and CI. An authorized epic includes dependency-ready child work and final epic acceptance; avoid automatically taking unrelated issues.

## PR and review

Every PR opens draft with `<type>(<component>): <observable change> (#<issue>)` and the [PR template](.github/PULL_REQUEST_TEMPLATE.md). Record actual author/session/system/provider/model/effort and validation performers; include source-bound commands/results and explicit limitations. Human model/effort is N/A.

Mechanical, Standard and Deep specify review coverage, independently of runtime effort or reviewer count. Declare Auto/Preferred/Required selection and any account/session/model separation. The [review procedure](.agents/skills/hvo-code-review/references/review-format.md) defines evidence, per-finding comments, P0–P3 summaries, author replies, independent verification and thread resolution. Same-account reviewer sessions use attributed comments when native self-approval is unavailable.

Draft -> review -> corrections/verification -> ready -> standard CI -> green -> authorized merge. Drafts run only bounded preflight. Failed CI or changed source returns to draft and invalidates readiness; code corrections require focused independent review before another standard run. Reaching a review budget is never approval. Use the [state and label rules](.agents/skills/hvo-pr-lifecycle/references/pr-states-and-labels.md).

PRs squash into `main` only after current-source review, terminal finding verification, resolved actionable threads and current required CI. Merge must be authorized by the assignment. Verify issue closure and publish closeout; cleanup only the completed branch/workspace. Release/deployment remains separately scoped.

## Local validation and history

`global.json` pins the SDK. Build the solution with zero warnings/errors and run the applicable non-live tests. Follow the profile for integration/UI/container checks. Document commands, environment, source, performer and actual results. Never report a missing, skipped or failed result as passing.

Update `docs/PROJECT_HISTORY.md` for structural, architectural or deployment-assumption changes with a concise outcome/decision/follow-up entry. Image-version/publish changes retain their existing `.env`, gist synchronization and CHANGELOG requirements; do not apply that deployment procedure to ordinary documentation/CI work.

Keep existing type, priority and domain labels. `workflow:in-progress` is the issue claim; each PR has exactly one lifecycle label. `workflow:blocked` is an overlay, not a new owner or an approval. The historical #187–#198 review campaign is [archived context](docs/archive/2026-review-campaign-process.md), not a quota for new work.
