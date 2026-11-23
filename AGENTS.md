# Agent instructions

## Unified-policy pilot candidate

The [HVO policy 2026-11-17.2](docs/development/policy/common.md) defines scope,
platform/security > common policy > repo profile precedence, and local skills;
see the [WebSite profile and pilot stages](docs/development/policy/website.md#pilot-stages)
and [adapters](docs/development/policy/adapters.md). Independent review starts after
the profile's latest named draft checks pass on the exact head. The review minimum is checked
at Ready and before merge, and does not block implementing, committing, pushing or
opening a draft PR; see the [review contract](docs/development/policy/common.md#independent-review-contract).

The six folded common rules apply to all WebSite agent work. Other pilot policy
applies to designated pilot work and policy text or enforcement changes; other
procedures remain in force subject to those rules.

## Scope and authorization

These instructions apply to HVO.WebSite. Other repositories remain read-only unless the user authorizes their changes. Read-only inspection does not authorize unrelated implementation. Preserve current user scope and authorization when resuming work.

Use a dedicated branch/checkout from `main`; never commit issue work directly to `main`. Do not overwrite another owner's work. Releases, deployment, live hardware operations and other repositories need applicable authorization. The common standing rule authorizes reviewed, green merge and closeout subject to assignment limits, but does not authorize reboots or deletions; Kepler performs agent PR operations under the common policy. Do not stop at PR creation or repeatedly ask to continue. An epic assignment includes its authorized child work and final acceptance. Honor any assignment endpoint that narrows that authority.

## Mandatory procedure routing

For WebSite work, repository-local skills (`.agents/skills`) govern; global `hvo-*` skills on the host are not substitutes and do not authorize dispatch or installation — see [profile discovery](docs/development/policy/website.md#repository-specific-scope-and-discovery). Do not edit or uninstall global skills.

Before acting, read [the repository profile](docs/development/repository-profile.md) and the applicable files below. These are required local instructions, even when the tool does not automatically discover skills.

| Request | Required procedures |
|---|---|
| Pick up, implement or resume an issue | [.agents/skills/hvo-issue-work/SKILL.md](.agents/skills/hvo-issue-work/SKILL.md), then [hvo-pr-lifecycle](.agents/skills/hvo-pr-lifecycle/SKILL.md) for its PR |
| Run an issue through the full lifecycle | Issue and PR procedures; independent reviewer uses [hvo-code-review](.agents/skills/hvo-code-review/SKILL.md) |
| Complete an epic | [hvo-epic-work](.agents/skills/hvo-epic-work/SKILL.md), plus issue/PR procedures for each child and code-review procedure for independent review |
| Review a PR or verify corrections | [hvo-code-review](.agents/skills/hvo-code-review/SKILL.md); PR procedure before changing GitHub state |
| Review-only investigation without a PR | Code-review procedure adapted to the assigned issue; publish attributed findings on that issue |

Missing required files are a setup failure to report, not permission to skip the procedure. Read [WebSite project guidance](docs/AGENT_PROJECT_GUIDANCE.md) and applicable scoped instructions before implementation/review. Read [CSS governance](docs/CSS_GOVERNANCE.md) before CSS or Blazor markup work. Humans follow the same baseline through [CONTRIBUTING.md](CONTRIBUTING.md).

## Submission and evidence

Every PR starts draft. Use [.github/PULL_REQUEST_TEMPLATE.md](.github/PULL_REQUEST_TEMPLATE.md), a conventional title describing the observable change, and the linked issue. Record actual implementation and validation contributors: identity/session, system, provider, model and effort, contribution/source range and available runtime evidence. Unknown metadata stays unknown; a requested value is not an actual executed setting.

Run `dotnet build` at zero warnings/errors and the applicable non-live tests before requesting review. The SDK pin is authoritative; use its exact SDK locally or in a container. Record failed/unavailable checks and source-bound results honestly. New tests should verify meaningful behavior, failure handling or workflow invariants; documentation alone does not require invented tests.

Draft automated checks are bounded preflight only. Independent current-source review, verified finding dispositions and resolved actionable threads precede ready state and standard CI. Failed CI or changed source returns to draft for correction/review. A green current candidate can merge within the common standing authorization and any assignment limit. Labels show state but cannot create approval or merge authority.

The reviewer is a distinct person/session that did not materially implement the candidate and, by default, uses a different model from every contributor to the reviewed range. Only Roy’s own recorded decision for an exact full head SHA can address a disclosed independence or overlap defect; it never carries over to a later head, and an agent can never waive the requirement or grant the exception. See the common review contract for its constraints. The common policy specifies distinct-model reviewer minima and defaults; the assignment can require a different account or provider. The current gate requires direct publication for strict different-account separation, which Kepler's agent relay cannot satisfy; such a requirement is BLOCKED until a compatible gate exists. Same operator account with a distinct reviewer session is supported. Review summaries identify the actual reviewer and group linked findings by P0–P3; each finding has its own code comment, evidence, possible solution and independent disposition verification. Kepler faithfully publishes agent reviews as GitHub metadata; do not commit review reports merely to publish them.

Keep project history curated when structure, architecture or deployment assumptions change. Do not introduce unrelated packages/frameworks, public contracts or configuration changes without explaining their need and compatibility effects. Keep changes focused; preserve existing behavior outside the assignment.
