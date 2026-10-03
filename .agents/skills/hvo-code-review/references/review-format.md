HualapaiValley code review procedure and publication format

This is the review standard for the WebSite pilot. It defines how an independent reviewer examines a change, publishes a concise summary and separate code comments, and verifies resolutions. It is the reference for `hvo-code-review`; it does not require AgentControl's experimental bot.

The PR author maintains the submission description and local validation evidence. The independent reviewer publishes the review. CI supplies a separate record of automated checks. Keep those responsibilities distinguishable even when sessions use the same GitHub account.

All PRs start draft and remain draft through review and corrections; only simple automated preflight checks run then. Completed independent review and verified finding/thread resolution permit the ready transition and standard CI. A CI failure returns the PR to draft; changed source receives correction review before the next run. Native state, lifecycle labels and retained ownership follow the [PR state rules](../../hvo-pr-lifecycle/references/pr-states-and-labels.md).

**Review depth**

Depth specifies the coverage required by the change. It is selected from the issue, affected behavior and risk before choosing a reviewer, and can be escalated when review uncovers wider effects. It is separate from a model's reasoning-effort setting, the number of reviewers and the number of correction rounds.

| Depth | Appropriate changes | Required coverage |
|---|---|---|
| Mechanical | Pure prose, spelling, formatting or other demonstrably non-behavioral edits. | Inspect the complete change, confirm meaning and executable behavior are preserved, check relevant references and applicable inexpensive validation. Promote when behavior, contracts or executable configuration change. |
| Standard | Ordinary application changes with bounded effects. | Inspect the complete change and relevant surrounding callers/contracts; verify acceptance, ordinary edge cases, error handling and focused validation. |
| Deep | Security, durability, concurrency, migration, safety, CI control, deployment or effects across components. | Standard coverage plus trace the affected interactions, challenge failure/recovery assumptions, investigate applicable security/race/data-loss risks, and examine adversarial cases and the evidence that establishes acceptance. Record material coverage limits. |

All depths require an independent reviewer, the same publication format, a bound source range and verified finding dispositions. Deep does not require reviewing unrelated code, choosing the maximum available effort, or automatically obtaining multiple reviews. Scope the risk lenses explicitly. For the WebSite workflow pilot, Deep covers draft/ready transitions, CI bypass and suppression, stale review/head evidence, failure recovery, finding resolution and any permissions used by automation.

**Author provenance and separation rules**

The author description records the actual GitHub PR author and every materially contributing implementation session/person. Use the same identity fields as the reviewer: distinct session or human identity, system/harness, provider, actual model and effort, with contribution ranges and task/runtime evidence when available. Keep multiple contributors or mid-task model changes visible; do not attribute the whole change to whichever model wrote the PR body. Human model/effort is `N/A`; unavailable agent metadata is explicitly `unknown`.

Each local validation result identifies its performer through that provenance record, and the source it validates. Add a contributor entry when another session ran a check. Results the author merely inspected must be identified as inspected rather than personally run. The reviewer reads this provenance before accepting the assignment and reports its compliance with the requested separation rule.

| Separation requirement | What must differ or match |
|---|---|
| Baseline independent review | A distinct reviewer person/session that did not materially author the implementation under review. The same operator/GitHub account and same model can be used in a separate reviewer session. |
| Different GitHub author/reviewer | The review-performing account differs from the PR author account. Record a relayed publication account separately; a publisher is not necessarily the reviewer. |
| Same author account, different model | The operator/GitHub account matches, but a distinct reviewing session uses a different actual underlying model from the implementation model set. |
| Different model or provider | Require a distinct underlying model or provider respectively, in addition to session independence; changing a tool, reasoning effort or service tier is insufficient. |

Different-model comparisons use the actual provider/model identities, with any verified alias mapping supplied by the tool adapter. Default comparison scope is all models that materially authored the candidate; an assignment can explicitly select a narrower role/range. Do not infer distinct underlying models from different branded tools or unverified aliases. A human reviewer can satisfy independence but cannot meet a specifically required two-model constraint unless the policy names a human alternative.

Switching models in the implementation conversation is self-review and may supplement validation. To satisfy the independent-review gate under the same author account, use a distinct reviewing session with the source, acceptance, provenance and findings supplied as review inputs. If required account/session/model facts cannot be established, retain `INCOMPLETE` for that role. A different identity or model does not by itself demonstrate adequate review coverage.

Metadata checks can reject missing fields and contradictory account/session/model records. They cannot prove who actually performed the work from editable PR prose alone. Where strict verification is required, compare supported task/runtime records and actual GitHub authorship; declare evidence limits rather than claiming an unverifiable identity/model constraint is enforced. This does not make an experimental publication bot mandatory.

**Reviewer selection**

Record selection separately from depth. A system/harness is the reviewing tool, such as Claude Code, Codex, Copilot or OpenCode. The model provider and exact model ID identify the model used by that tool; a tool or publication account name is not proof of the underlying model. A different tool may still use the same model.

| Selection policy | Meaning | If the requested route is unavailable |
|---|---|---|
| Auto | Select an eligible independent reviewer using the repository's configured reviewer profiles and capabilities available in the current environment. | Try another eligible route; if none can complete the required coverage, record the blocker and retain draft state. |
| Preferred | Try an ordered list of systems/models/settings, with a stated permitted fallback. | Use only the declared fallback and report the substitution. If no permitted route is available, keep the requirement unmet. |
| Required | Use the specified route or one of the explicitly allowed alternatives, including any mandatory model/settings and reviewer-count constraints. | Do not silently substitute. Record the unmet requirement and retain draft state until it can be met or the authorized owner changes it. |

Auto is the proposed ordinary default with one independent reviewer. A qualified human is eligible unless the assignment specifically requires an agent/model. Keep changing exact model mappings in the repository's reviewer configuration or tool adapter rather than baking them into the common skill. Such configuration must identify eligible models/settings and any permitted human or managed-model route; do not guess that every available model is suitable. Discover current capabilities at assignment time and check access to the full diff/context, validation evidence, requested lenses and publication route. A model name alone cannot establish review quality.

An allowed list normally means **one of these alternatives**, not one review from every entry. A requirement for two or more independent reviewers must say so explicitly and name any model/provider diversity constraint. Each required reviewer produces an attributed report on the same current candidate; carry all findings through verification. Approval by one reviewer cannot erase a blocker reported by another. Prefer one consistent set of finding IDs and threads across rounds/reviewers.

Effort controls are specific to the model and tool. Resolve supported settings through the actual environment; do not equate Deep with `high` or `xhigh`, or treat similarly named settings across providers as proven equivalents. A prompt asking for thorough review does not prove that a runtime effort setting was applied. [OpenAI reasoning-effort documentation](https://developers.openai.com/api/docs/guides/reasoning) likewise identifies supported effort values as model-dependent.

For a managed reviewer that hides the model or effort, record the available metadata and the visibility limit. An Auto or Preferred policy may permit that route when it can establish the required coverage; it cannot satisfy a strict, unverifiable exact-model/effort or provider-diversity requirement. For that required role, publish `INCOMPLETE` rather than treating hidden metadata as compliance. A supplementary bot review can still provide useful findings without satisfying the required review role.

Example request for this pilot:

```text
Depth: Deep
Required reviewers: 1 independent reviewer
Focus: CI gates, source/review binding, failure recovery, permissions
Selection: Auto from configured eligible reviewer profiles
Model / effort: Resolve from the selected tool's live capabilities
Fallback: Another eligible configured route; retain draft if none is available
```

An exact-model request can instead say `Required: one of <provider/model/settings A> OR <provider/model/settings B>; no other fallback`. A two-model request says `Required: two independent reviews, one from A AND one from B`. These describe selection constraints, not reviews already performed or blanket authorization to launch extra agents.

**How to perform the review**

1. Read the assigned issue or epic child, acceptance criteria, applicable AGENTS.md/project instructions, review procedure, required depth, reviewer-selection constraints and risk lenses. Confirm the selected route can satisfy the assignment. Establish scope from the issue and source, not merely from the author's summary.
2. Identify the target tip, merge base and current head as full immutable SHAs. Initial review covers the complete PR diff from merge base to head. Correction review covers the previous reviewed head to the current head, every outstanding finding, and relevant interactions. Synchronization review examines target-update effects and conflict resolutions.
3. Inspect the changed code and the relevant surrounding methods, callers, contracts, configuration and tests. Follow cross-project effects when the change touches shared contracts, themes, persistence or infrastructure. A diff-only skim is insufficient when correctness depends on those interactions.
4. Evaluate correctness and acceptance first, then applicable failure handling, security, concurrency, durability, compatibility, performance and UI behavior. Identify concrete, actionable defects. Distinguish an introduced regression from an unrelated pre-existing issue; do not expand the implementation scope to satisfy stylistic preference.
5. Check the author's validation against the actual source. Run focused tests/reproducers when needed and available; distinguish tests personally run from existing results inspected. Record unavailable validation honestly. An uncertain hypothesis belongs in a clearly identified question or limitation, not a confidently asserted defect.
6. For each finding, provide evidence, impact and a possible solution. Use a stable finding ID and severity. A suggestion is a proposed fix whose suitability the implementer must evaluate and test.
7. Publish the summary and individual comments. Check the PR head around publication. A report tied to a previous head remains historical evidence; a changed candidate needs appropriate review. Approval must never silently migrate to a newer commit.

The independent reviewer does not edit the implementation branch. Review budgets affect how work is organized, never whether a security, data-loss, acceptance or material-correctness blocker is considered fixed.

**Reviewer metadata**

Every parent summary records the following in its expandable **Agent details**, rather than leading with a long metadata list:

- Reviewer: the actual person or distinct agent/session that performed the review.
- Review-performing account, and a different publication account if its report was relayed.
- System: the reviewing application/harness, such as Copilot, Claude Code or Codex through T3; include its version when available.
- Agent location: actual host/server, known harness instance ID and exact checkout/worktree path with branch; keep these alongside session identity in expandable Agent details. Undisclosed cloud/instance locations stay unknown or accurately provider-managed.
- Provider, model and reasoning effort: actual values exposed by the runtime. A requested model/effort may be recorded separately if execution differed.
- Required depth, reviewer-selection and independence policy, coverage actually completed, and whether selection/separation constraints were met. Reference the author's provenance record; explain permitted substitutions and visibility limits.
- Mode, reviewed commit range and UTC time.
- Verdict and concise validation/acceptance coverage.

For human reviews, model and effort are `N/A`. For agent values the system does not expose, use `unknown` or an accurately identified `provider-managed` setting. Do not infer a model from a bot's name or infer high effort from the requested review level. A relevant test environment, such as macOS/ARM64, belongs with validation evidence; "System" identifies the reviewer tooling.

**Summary format**

Use one short parent summary per review round. Lead with the verdict, a plain-language coverage sentence and priority counts. Follow immediately with one-line finding bullets grouped by P0/P1/P2/P3: stable ID, short observable problem, current disposition and code-thread link. Detailed evidence, reproductions and solutions remain in that thread. Empty priority groups can be omitted because the counts expose them.

Keep validation and remaining limits to a few short bullets. Put exact commands and extensive coverage/history in a second expandable block when needed. Put all reviewer provenance, actual model/effort, selection/separation and source details together in Agent details; do not scatter some above and some inside that block. The visible summary should be understandable without opening it. Clean reviews use the same layout, with a brief no-actionable-findings statement.

```markdown
## Code review summary

**<Changes required | Approved | Incomplete>** — <one sentence describing the result>.

Reviewed <complete change/correction scope and relevant context>.
**Counts:** P0 <n> · P1 <n> · P2 <n> · P3 <n>.

### P0 Critical

- **F1 — <short observable problem>** — OPEN — [code comment](<thread URL>)

### P1 High

- **F2 — <short observable problem>** — OPEN — [code comment](<thread URL>)

### P2 Medium

- **F3 — <short observable problem>** — OPEN — [code comment](<thread URL>)

### P3 Low

- **F4 — <short observable problem>** — OPEN — [code comment](<thread URL>)

### Validation and limits

- **Run by reviewer:** <short result, useful counts and evidence link>.
- **Inspected author/CI evidence:** <short result; distinguish skipped/pending from green>.
- **Remaining limits:** <material omissions or unmet acceptance, or None>.

<details>
<summary>Agent details</summary>

| Field | Actual review details |
|---|---|
| Reviewer / operator | <actual person or distinct agent; performing account> |
| Publisher / route | <authenticated publisher; native review/comment or faithful relay> |
| System / version | <application/harness; version or unknown> |
| Provider | <actual provider; human when applicable> |
| Model | <actual underlying model ID; N/A for human, unknown if hidden> |
| Reasoning effort | <actual executed setting; N/A for human, unknown if hidden> |
| Session / task | <distinct reviewing session and available task/runtime link> |
| Host/server / platform | <actual host identifier and relevant OS/architecture, or unknown> |
| Harness instance | <actual instance ID, or unknown when not exposed> |
| Checkout/worktree | <exact absolute path, or unknown if unavailable> |
| Branch / target | <actual source branch and integration target> |
| Review mode / UTC time | <Initial, Correction or Synchronization; timestamp> |
| Depth / actual coverage | <required coverage level; completed scope and risk lenses> |
| Required reviewers | <count and any panel requirements> |
| Selection / fallback | <Auto, Preferred or Required; choices and actual substitution/reason> |
| Independence / compliance | <author provenance comparison; constraints; MET, UNMET or UNVERIFIED> |
| Runtime evidence / limits | <execution evidence; declarations and unavailable facts stay explicit> |
| Reviewed head | <full immutable raw PR head SHA> |
| Target tip / merge base | <full immutable target and merge-base SHAs> |
| Reviewed range | <full immutable initial or correction range> |

</details>

<details>
<summary>Validation and coverage details</summary>

<Exact commands and results personally run; evidence inspected; coverage omissions;
prior finding/review history when needed. Keep source and performer attribution explicit.>

</details>
```

The template's rows are illustrative; publish only real findings. A clean review still includes identity, source range, actual coverage, counts and verdict, followed by a brief statement that no actionable findings were found. Missing required acceptance evidence, coverage or reviewer-selection compliance yields `INCOMPLETE`, not an implied clean result. Unknown model metadata is a declared limitation; it fails an exact-model requirement but does not automatically invalidate a policy that permits a managed reviewer.

Where the publishing tool allows it, submit the summary as a native GitHub review with individual line comments. A qualified distinct account may use the native approve/request-changes state. Same-account sessions can publish comments with provenance; they must not pretend to have submitted an independent native self-approval. If tooling requires separate publication calls, create the finding comments first and then publish the summary with their actual links. Failed publication is reported as a gap.

Presentation-only edits may improve an existing summary while preserving its source, verdict, findings, reviewer identity and machine record. Identify a faithful formatting relay in the details when another session makes that edit. Publish a new source-bound summary for a new decision, correction round or changed verdict; presentation edits must never reorder native review decisions.

**Individual code comment format**

Create one thread per finding. Anchor it to the most relevant changed line or range in Files changed. If the problem spans files, cite the interaction in the comment. If the API cannot anchor the relevant location, name `path:line` and link to the source in a separate finding comment; keep it individually addressable and explicitly tracked until its verified terminal disposition.

```markdown
### F1 — P1 — <short title>

**Problem and impact:**
When <concrete condition>, <observed behavior> causes <impact>.

**Evidence and references:**
- [Source code at reviewed commit](<immutable permalink with relevant lines>).
- <Reproducer, focused test, trace or demonstrated code-path reasoning>.
- [Supporting specification or official documentation](<link>), when applicable.

**Possible solution:**
<A bounded correction that addresses the failure; explain an important tradeoff.>

**Verification:**
<Test or observable behavior that would establish the correction.>
```

Source-code references should identify the reviewed commit so later edits do not change what the comment meant. Cite the issue criterion, project contract or official API documentation when it supports the finding. Do not invent an external reference where code evidence is sufficient. Clearly identify inference when evidence supports only a hypothesis.

An exact small edit may additionally use a GitHub `suggestion` block. Suggestions are optional: a credible explanation or pseudocode is preferable to speculative replacement code. Never make acceptance of the precise suggested implementation mandatory when another correction satisfies the required behavior.

**Severity and merge implications**

| Severity | Meaning | Disposition |
|---|---|---|
| P0 Critical | Critical security exposure, serious data loss, or similarly urgent failure. | Blocks; correct and independently verify. |
| P1 High | Material correctness, safety, acceptance, reliability or security defect. | Blocks; correct and independently verify. |
| P2 Medium | Meaningful scoped defect that should be addressed before acceptance. | Blocks by default; eligible work outside current acceptance can be deferred only with owner authorization, a linked follow-up and reviewer verification. |
| P3 Low | Minor non-blocking issue or useful optional improvement. | Correct or explicitly disposition according to scope; avoid filling the review with low-value stylistic comments. |

Finding severity is distinct from scheduling priority on a backlog issue. A mislabeled security/data-loss/acceptance/material-correctness defect remains blocking. Record any severity reassessment and its reason in the finding thread.

**Author response and reviewer verification**

The author responds in the existing finding thread, preserving its ID. Use one of these dispositions:

- `CORRECTED`: correction commit, changed behavior and validation evidence.
- `DEFERRED`: linked follow-up issue, authorized acceptance of deferral and residual risk.
- `NON_ACTIONABLE`: source evidence demonstrating that the reported failure does not apply.
- `SUPERSEDED`: link to the finding or correction that covers this one.

For a correction:

```markdown
**F1 — CORRECTED**

- Correction commit: `<SHA>`
- Change: <observable correction>
- Validation: <exact command/reproducer and result>
```

The independent reviewer checks that claim and replies in the same thread:

```markdown
**F1 — VERIFIED_CORRECTED**

- Reviewed head: `<SHA>`
- Verification: <evidence that the reported failure is corrected>
- Disposition: ready to resolve.
```

Equivalent terminal replies are `VERIFIED_DEFERRED`, `VERIFIED_NON_ACTIONABLE` and `VERIFIED_SUPERSEDED`. A failed correction is `STILL_OPEN`, with the remaining behavior/evidence. The implementer cannot supply its own independent verification.

An authorized PR owner or maintainer resolves the thread only after the reviewer's terminal verification is present. This need not require an additional routine human approval when the assignment already authorizes the lifecycle. A reply saying "fixed", an outdated annotation, or reaching a round cap is not independent verification. Keep the original finding and replies; do not erase the history or reset IDs.

**Correction summaries and completion**

Each correction summary uses the same metadata and priority groups. List every carried finding with its verified or open disposition, linking the original thread. New findings take the next unused ID. Do not recreate existing threads or repeat the original evidence in the summary.

The PR lifecycle owner may record final merge readiness after verifying that the current head is reviewed, every finding has a verified terminal disposition, actionable conversations are resolved, accepted follow-ups are linked, and required CI passes. Preserve the independent reviewer's judgment as its own record. A later product correction or material synchronization invalidates the relevant approval/evidence and requires the appropriate review.

The planned required checks should verify author/reviewer provenance and summary format, a current reviewed head, required reviewer count and observable selection/separation constraints, linked finding records and dispositions, and the configured CI results. They can enforce observable records, not prove review quality or infer a hidden model. Distinguish checks of declared metadata from constraints corroborated by supported runtime evidence. Review reports, code comments and resolution replies are GitHub metadata; do not commit them to the implementation branch merely to publish them.
