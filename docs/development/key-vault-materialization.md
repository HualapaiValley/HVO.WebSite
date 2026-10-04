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
  derivation error; the helper does not invent a SQL target. The current nested
  command substitution does not reliably abort the outer synchronization on
  that error: apply can continue, write an empty connection string and exit zero.
  Establish and securely validate this input before apply; an exit code alone
  does not prove a valid SQL materialization.

Also require Azure CLI, an already authenticated account and approved read
access to the selected vault (`HVO_KEY_VAULT_NAME`, default `hvo-central-kv`) and
the helper's full secret allowlist. Do not assume website-only vault permission
is enough for this developer/materialization operation. Runtime identities and
their narrower grants remain separate.

## Understand the whole helper

The root stages run before website or gateway stages. They compare/materialize
allowlisted Tailscale/GitHub, developer Azure, ingest/read, registry, SQL, HA and
ESPHome values, check the sourced SSH field, and derive the root SQL string.
`--apply` is not a complete-bootstrap initializer: it can leave partial changes
or continue after the SQL derivation error described above. It does not populate
the required multiline SSH field from scratch.

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
Review this entire scope and checkpoint owner files before authorized apply;
after any reported error or drift, inspect affected file metadata and approved
values securely before retrying, even if apply returned zero. Correcting that
script failure propagation requires a separate reviewed behavioral change;
these runbooks do not alter it. A successful check is not a runtime
authentication or restore drill.

For private-gist preview/update behavior, see [publishing](../CONTAINER_PUBLISHING.md#root-bootstrap-gist).
That preview does not run this helper or validate credentials.
