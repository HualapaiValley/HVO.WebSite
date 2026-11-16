# Disabled local policy and coordination prototype

These modules are not imported by the live evaluator/controller, installed into a
harness, or invoked by CI. There is no enable flag, external transport, credential
creation, scheduler, status publisher or merge operation. They implement local
validation and transactional recording without activating the proposed policy.

## Review-policy inspection

`review-policy.mjs` reuses the existing evidence evaluator without changing it. Its
policy-owned minima are one independent reviewer for low/standard work and two
families with at least one deep review for high risk. An ordinary review does not
require a different model. Stronger assignment constraints are retained.

The actual inspector always uses the high-risk fallback: no authenticated impact
assessment exists to justify a lower tier. It checks the supplied source tuple,
existing evidence predicates, review count, declared family diversity and depth.
All inputs, including API-looking permission fields, family names and receipt
claims, remain untrusted. `structuralEligible` means only internal consistency.
It is never an admission verdict. `eligible` is always false and the CLI always
exits 2, including for a structurally valid candidate:

```sh
node tools/policy-pilot/review-policy.mjs /absolute/path/to/local-snapshot.json
```

Missing pieces: a trusted immutable-target policy loading boundary; authoritative
candidate/source and impact assessment; authenticated contributor/reviewer session
receipts; corroborated model-family mapping; receipt integrity/revocation and
completeness checks; qualified publisher and branch enforcement. A field named
`authenticated`, an account permission lookup or a family string cannot supply
these. Do not integrate this inspector as a passing CI gate.

**Hard activation blocker:** derive canonical family IDs from authenticated runtime
provider/model receipts through a trusted mapping, never from declared strings.
Trimming/lowercasing is only a structural comparison: aliases (`claude`/`anthropic`),
Unicode lookalikes and invisible characters can still appear distinct. Before
activation, qualify rejection of unmapped/ambiguous identities and forged mappings,
and prove aliases of the same family cannot fill separate family slots. Normalizing
text or accepting a caller-supplied mapping does not establish provenance. The local
adversarial tests demonstrate these declarations can be structurally consistent
while admission remains blocked; they do not implement authenticated mapping.

## Journal and recovery inspection

`journal.py` is an explicit-call library with no import-time writes. It creates a
new private SQLite file only when `Journal.create(absolute_path, owner)` is called.
Use an owned local filesystem directory, never a shared/network filesystem or
another task's state. Creation does not overwrite; schema mismatches block reads
and mutations. Failed initialization leaves evidence for diagnosis, not an empty
state silently ready for reuse.

Implemented operations:

- `claim`: persist immutable work/source binding, preserving the domain owner.
- `prepare_attempt`: transactionally record intent as AMBIGUOUS before any
  hypothetical dispatch. At most one unresolved intent exists in the domain.
  Repeating the same attempt is an idempotent lookup, never dispatch permission.
- `observe`: persist unverified callbacks, deduplicate event IDs and reject ID
  collisions. Quarantine unknown work/attempts and stale generations/source.
  A persistence acknowledgment is returned only after commit; it is not a trust
  acknowledgment. Callback order cannot advance or regress execution state.
- `recover`: read the transactional journal after restart without writes or replay.
  All attempts remain AMBIGUOUS, with dispatch and merge explicitly disallowed.

SQLite transactions, FULL synchronous commits and initial directory fsync support
the tested process-exit recovery cases. Power-loss durability is unverified:
the current rollback journal uses DELETE mode, with fullfsync off by default.
`persisted: true` acknowledges a SQLite commit, not survival of a power failure.
Neither current tests nor a future PRAGMA change alone qualifies hardware durability.
Before any adapter relies on recorded intent or acknowledgments, qualify the storage
configuration and power-loss recovery on the actual filesystem/hardware; until then
that reliance is blocked. Ownership/generation checks
are cooperative local consistency checks, not authentication or fencing against
an actor able to modify the database. There is no ownership transfer, stale-owner
takeover or recovery reset. Generations cannot be advanced by this prototype.

Even a claimed success, absence or provider failure cannot release an intent or
authorize a new attempt. Without an authoritative outcome verifier, stopping is the
implemented recovery behavior. Live dispatch, authenticated run-handle reconciliation,
transport-driven transitions, retries, watchdog/heartbeats, merge finalization, post-merge
verification and cross-host fencing remain unimplemented. There is no exactly-once
claim. A future adapter must not infer permission from `created` or `persisted`.

## Local tests

Run from the repository root; the Python suite needs only the standard library:

```sh
node --test tools/policy-pilot/review-policy.test.mjs
python3 -m unittest discover -s tools/policy-pilot -p '*_test.py'
```

Fixtures own disposable directories and perform no external calls. They cover
malformed/stale/self-review evidence, stronger assignment constraints, spoofed
trust flags, CLI non-success, concurrent intents, process-exit recovery,
uncommitted rollback, callback write failure/lost acknowledgment, duplicates,
stale owners/generations/heads and refusal to replay after apparent success or
failure. This does not qualify live harness discovery, authenticated evidence,
GitHub protection, power-loss hardware behavior or unattended operations.

## Disabled coordinator and offline evidence

`coordinator_journal.py` adds a separate version 2 journal. It records intent,
launch sent, observed launch, terminal state and evidence collection, and supports
blocked, duplicate and reopened states. Launch sent is ambiguous. The client
request ID is a replay key, never proof. Saved coordinator-recorded hub reads can
identify one launch only when every available binding agrees and pagination is
complete; a partial match stays ambiguous. The current conservative resolver
accepts only a single complete list page because saved pages do not prove their sequence. Duplicate titles are not counted. A later
run reopens evidence and voids its prior collection. Ownership cannot transfer.
Recovery and watchdog planning are read-only reports; there is no dispatch, replay,
resume, activation flag, or transport. SQLite uses DELETE journal mode and FULL
synchronous; power-loss durability remains unverified.

`evidence_collector.py` reads only saved JSON files. The CLI takes a JSON config
with `threadFile`, `listFiles` and `githubFile` paths and metadata, prints a
sorted JSON envelope, and always exits 2. It cannot authenticate evidence, grant
admission or publish. Requested and executed model strings never establish family;
session distinctness remains unknown. Re-check the source and checks after collection.
The modeled CI budget allows an initial attempt-1 run and at most two new attempt-1
runs on an unchanged head/base/merge tuple. Attempt 2 or later does not qualify.
Neither a budget counter nor a transient-cause classifier exists in live CI.

Saved collector inputs are strict UTF-8 JSON objects, one per regular file; wrappers,
trailing text and non-finite numbers are rejected. `githubFile` contains `pr`,
`statuses` (the combined status, including `sha`), and `workflowRuns` (the GitHub
list object with `total_count` and `workflow_runs`). CI tuple bindings come from
the exact HVO-PR-CI display title. Saved list pages used for resolution must include
the coordinator-recorded `request`: environmentId, allProjects and includeSubagents
true, the exact titleContains nonce, no status filter and cursor absent or zero.
Live WebSite admission requires CI attempt 1; only the retry budget is modeled.
