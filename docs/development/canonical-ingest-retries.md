# Canonical ingest retries and BMS chronology

Canonical batches acknowledge observations only when the corresponding identities
are committed. A uniqueness error can roll back unrelated novel rows, so it cannot
prove that the entire batch is a duplicate.

Power readings and raw weather requery durable identities after a SQL conflict.
They clear failed EF tracking before that query. If any attempted identity remains
absent, the existing transport contract returns a retryable HTTP500; it does not
put the persistence error into a permanent per-record validation failure. Repeated
valid aliases are counted only with a committed canonical observation. Invalid
weather rows do not reserve an in-batch identity.

Power snapshot conflict recovery additionally compares the stored content:
the existing timestamp-stripped payload hash for hash-backed snapshots, and an
ordinal serialized-JSON comparison for MPPT. SQL Server's default string collation
must not compare MPPT JSON case-insensitively. Different content at the same
identity is not an identical replay. #402 owns the corresponding pre-insert replay
checks and observation-identity/index semantics. Inverter/MPPT detail batches
propagate a persistence500 for the whole transport request, so senders retry it;
earlier individually committed details then replay safely.

## BMS transaction boundary

Device registration retries at most three times, clearing failed Added entities
before requerying competing registrations and retrying still-novel addresses. Each
registration attempt has its own explicit transaction within the execution strategy.
An unresolved registration remains retryable and never produces DeviceId0.
Device registration can remain committed after a subsequent reading failure;
readings and their required children, config/info writes and alarm updates share
one explicit transaction.

Every execution-strategy attempt recreates entities and lookups. Each affected
device row is locked in ascending device-ID order with SQL Server UPDLOCK/HOLDLOCK
inside that transaction. The lock serializes canonical BMS writers across process
instances, rather than using an in-process semaphore. Cancellation and provider
timeouts propagate and roll back the transaction.

After a transaction conflict, dispose/roll back the transaction and clear tracking
before checking durability. Required voltage/resistance cells must have complete
one-based indices; a bare summary is insufficient. Every alias's requested child
shape must be accounted for. Unresolved or partial legacy identities return500
rather than a fabricated skip. The raw rows remain available for investigation;
this code does not silently overwrite a partial legacy summary.

## Event-time alarm history

The retained raw readings determine alarm intervals. Equal nonzero bitmasks extend
an interval; changed bitmasks close the previous interval and open the next; zero
closes an active interval. Same device/time is a single authoritative observation.

When new readings arrive, sort them by event time and rebuild each affected device's
suffix starting at its earliest new observation. Recover the predecessor's
contiguous alarm run from retained readings, preserve its matching interval ID,
and replace only affected derived intervals. The suffix is read with keyset pages
of512 observations under the same device lock. No raw history is discarded and no
silent history cap truncates the reducer. Existing unrelated prefix intervals stay.

A delayed healthy observation cannot clear a newer alarm, and a delayed alarm
before a retained healthy observation cannot reopen current state. Ordered and
reordered delivery produce the same event-time intervals. Config/info comparison
state advances only for equal/newer event timestamps; a late value cannot cause a
later return to that value to be incorrectly suppressed.

The paged suffix bounds memory, not total transaction duration. Long-delayed
observations can rebuild a long suffix while holding the device lock; existing
command timeout, execution-strategy and cancellation limits apply. This correction
does not claim a global audit/backfill of unrelated historical corrupt intervals.

## Compatibility and validation

Public request/response JSON and sender accounting rules stay unchanged. Validation
failures remain per-record failures; persistence failures remain retryable. The
SmartShunt atomic summary/detail and strict source-ownership contract stays separate
and is covered by the existing regression suites. No schema migration, new package,
production configuration or deployment is introduced here.

Run the pinned-SDK build, the documented Fast selection and the provisioned
SQL runner. Focused SQL regressions force actual competing writes after lookups,
device-registration overlap, SQL547 between summary/child writes, subsequent
context reuse, mixed detail batches and concurrent BMS writers. HTTP checks exercise
authenticated requests, retryable failures and exact replay accounting against the
same isolated provider. SQLite checks cover chronology permutations, metadata
ordering and page boundaries; they do not claim SQL locking/provider qualification.

See [SQL fixture setup](sql-server-integration-tests.md) and
[test lane selections](testing.md). Deployment and live-hardware commissioning need
their own authorization and evidence.
