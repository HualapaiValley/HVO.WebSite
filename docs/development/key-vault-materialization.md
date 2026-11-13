# Key Vault materialization prerequisites

This is the shared prerequisite for every current use of
[`sync-secrets-from-keyvault.sh`](../../scripts/sync-secrets-from-keyvault.sh),
including website publishing, website identity and Pi commissioning. The helper
is global: selecting a website or gateway in a later command does not limit its
secret synchronization scope.

## Obtain the root bootstrap first

Before either `--check` or `--apply`, obtain an existing, owner-approved root
`.env` through the approved restricted bootstrap handoff. The private root-env
gist is a recovery cache used by the devcontainer's `dc_bootstrap_env` path in
[post-create setup](../../.devcontainer/post-create.sh); Key Vault remains the
credential authority. Review the authorized bootstrap source and arrange secure
materialization with its owner when the cache is unavailable or stale. There is
no checked-in root `.env.example`, and copying only a deployment template does
not establish these inputs. Preserve existing owner files; do not overwrite
them or print/cache their contents in logs.

The root `.env` must be a trusted, shell-sourceable file with owner-only access:
the helper **sources it as shell code** when checking `SSH_PRIVATE_KEY`. Its
existing inputs must include:

- `SSH_PRIVATE_KEY`, whose sourced value is compared with `obs-ssh-private-key`.
  A mismatch is reported as drift; even `--apply` does not rewrite this multiline
  field. Arrange its approved manual materialization separately.
- `ConnectionStrings__HualapaiValleyObservatory`, with the intended existing SQL
  host/database/options and the exact `Password=` segment followed by a semicolon.
  The helper replaces the password from `obs-docker-mssql-sa-password` while
  preserving the rest of that string. A missing or unparsable string produces a
  derivation error; the helper does not invent a SQL target. Both modes reject
  this input before any managed destination writes. The helper preserves the
  existing exact `Password=`/semicolon format; it is not a general SQL
  connection-string parser.

Also require Azure CLI, an already authenticated account and approved read
access to the selected vault (`HVO_KEY_VAULT_NAME`, default `hvo-central-kv`) and
the helper's full secret allowlist. Do not assume website-only vault permission
is enough for this developer/materialization operation. Runtime identities and
their narrower grants remain separate.

## Understand the whole helper

Before any managed destination writes, both modes validate and source the trusted
root bootstrap in a separate shell, require its nonempty `SSH_PRIVATE_KEY`,
derive SQL for the root and every selected existing website file, and read all
selected vault secrets into memory. A caller's exported SSH value cannot replace
the required root input. Bootstrap output and raw Azure CLI secret-read errors
are suppressed; diagnostics identify fields or secret names without values.
Empty required secrets, failed reads/derivations, and values that cannot use the
existing dotenv/YAML representation fail nonzero during preparation. Rejected
preparation leaves managed destination contents and metadata unchanged. Sourcing
the bootstrap still executes trusted owner shell code; this is not a sandbox.
Because `.devcontainer/post-create.sh` invokes apply under `set -e`, a preparation failure stops the remaining devcontainer setup, including developer-certificate generation.

Only the root `.env` requires shell-safe plain secret assignments. Its validation
uses `LC_ALL=C` and rejects ASCII whitespace, dollar signs, backticks, quotes,
backslashes, semicolons, ampersands, pipes, angle brackets, parentheses and a tilde
at the start or immediately after `:` or `=`. Literal assignment characters such
as `!`, `,`, `*`, `?`, brackets, braces, `^`, `#` and non-ASCII text remain valid
regardless of the caller's locale. Website, handoff and gateway dotenv files are
not shell-sourced by the helper and retain their existing one-line value checks.
Root SQL is derived first so its explicit single-quote password check runs before
plain-secret validation. Errors name the field, never the rejected value.
Existing website/handoff CRLF dotenv inputs are read
with one trailing carriage return removed from each line; matching files need
no rewrite. Selected JK BMS/Davis JSON must parse and have the expected
secret-selection types; jq errors fail preparation rather than disabling secrets.

After preparation succeeds, root stages run before website or gateway stages.
They compare/materialize allowlisted Tailscale/GitHub, developer Azure,
ingest/read, registry, SQL, HA and ESPHome values, including the prepared root
SQL string.
`--apply` is not a complete-bootstrap initializer. It does not populate the
required multiline SSH field from scratch. An existing SSH mismatch remains
reported drift requiring approved manual rotation; apply preserves that field,
and a subsequent check remains nonzero until it matches.

Subsequent stages include:

- Existing `deploy/hvo-docker/.env` and `.env-handoff/hvo-docker.env`: registry,
  dedicated `WebsiteRuntime--*` identity, Key Vault URI, seeding values and SQL.
  Each existing website file also needs its intended parsable SQL string.
  Missing website files are skipped, not created. Custom website/publisher env
  paths are not discovered automatically.
- Existing legacy Davis/JK env and handoff copies; the fixed mounted secret
  directories for all four active collectors; conditional Davis external-delivery
  files; the disabled HA exporter's token/diagnostics files; and an existing
  ESPHome `secrets.yaml`. See the [Pi exceptions](../../deploy/pi-gateways/README.md#mounted-configuration-and-secrets)
  for filename selection, custom paths and omitted exporter ingest credentials.

`--check` is the default. It reads vault values and reports drift by name without
writing managed destination files; a nonzero result can indicate drift or an
unmet prerequisite. `--apply` changes approved local materializations, with
restricted file permissions, across the scope above. It does not rotate vault
secrets, deploy files to hosts, restart runtimes or provide an atomic rollback.
Writes after preparation are sequential, not a multi-file transaction. An I/O
failure exits nonzero, prints an explicit warning that partial updates may
remain, and does not print the final successful-apply summary. Each dotenv, YAML
and secret-file replacement uses a mode-0600 temporary file beside the destination
and renames it only after a successful write, preserving the old destination if
writing fails. The exit handler removes the in-flight temporary file on failure,
SIGINT and SIGTERM; ignored dotenv temporary-file patterns provide additional
protection against accidental staging. Earlier completed replacements may remain;
there is no automatic rollback across files.
Review this entire scope and checkpoint owner files before authorized apply.
After a write failure or reported drift, inspect affected file metadata and
approved values securely before retrying. Avoid concurrent edits or concurrent
helper runs while preparing/applying owner files. A successful check establishes
only allowlisted equality for the observed inputs, not runtime authentication
or a restore drill.

Offline regression command (copies the helper to disposable fixtures and stubs
Azure CLI; does not read a real vault or owner materializations):

```sh
python3 -m unittest discover -s tools -p secrets_materialization_test.py
```

The existing Davis deployment-policy validator also runs these regressions in CI.

Before merge, Roy or an authorized operator may separately run a real `--check`
to confirm compatibility with current vault values without exposing them; offline
stub validation does not establish that operational result.

For private-gist preview/update behavior, see [publishing](../CONTAINER_PUBLISHING.md#root-bootstrap-gist).
That preview does not run this helper or validate credentials.
