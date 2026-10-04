# Gateway manual template

Use a useful single manual first. Split protocol, implementation, contracts and
validation only when the amount of real evidence needs separate owners; five
empty files are not a requirement. See [gateway standards](../common-gateway-standards.md)
and [current manuals](../README.md).

## Identity and current status

Record purpose, accountable owner, implemented versus proposed capability,
source commit/blob, original observation date (or unknown), last documentation
update and related issues. HVO-created labels are documentation metadata,
not vendor fields.

## Protocol evidence

Link official/vendor material, current source and dated captures. For each real
field or operation give encoding, units, scale, range/sentinel, transport/auth,
reset behavior and safety consequence. Separate vendor names from HVO aliases.
Mark inference `Needs validation`; do not invent enum values, commands or APIs.

## Implementation and configuration

Name actual entry points, dependencies, worker/session ownership, retry/failure
behavior, startup config, mounted secrets, prerequisites and supported platforms.
Explain current write/control exceptions precisely, with authorization and
ACK/readback boundaries. A future proposal is not implemented capability.

## Delivery and consumers

Describe actual outbox payload/version/source/time identity and sender accounting,
central endpoint/auth/schema and reader semantics. Link canonical shared contracts;
do not duplicate their runbooks or handwritten DDL.

## Operation and recovery

Link commissioning, health/diagnostics/auth, safe shutdown, quiescent backup,
isolated restore and rollback. Identify source authority, preserved later
observations and separately authorized live/deployment actions.

## Validation and remaining questions

List meaningful owned unit/simulator/integration evidence, exact source binding
and artifact/provenance limits. Label historical measurements by their fact date.
Link an issue for material pending behavior; retain unassigned design questions
explicitly. Documentation changes do not supply live qualification.

## Optional split illustration

The following filenames illustrate a complex manual's organization. This fenced
example is not rendered navigation and needs no nonexistent-link exemption.

```text
README.md
manufacturer-protocol.md
hvo-implementation.md
hvo-api-contracts.md
validation-notes.md
```
