# Website Data Protection

The self-hosted website persists its ASP.NET Core Data Protection key ring in the
Docker volume `hvo-website-data-protection`. Key XML is encrypted with the Azure
Key Vault key identified by `DataProtection__KeyIdentifier` before it is written
to the volume.

Data Protection secures Entra authentication cookies, OIDC state, antiforgery
tokens, and other protected ASP.NET Core payloads. Keep
`DataProtection__ApplicationName=HVO.WebSite.v9` unchanged so every replacement
container uses the same application discriminator.

## Access

The dedicated `hvo-website-runtime` identity needs:

- secret-read access to `hvo-central-kv` for application configuration
- cryptographic wrap and unwrap access to the configured Data Protection key

Do not reuse the development service principal. Website deployment files obtain
the dedicated identity from `WebsiteRuntime--AzureClientId`,
`WebsiteRuntime--AzureClientSecret`, and `WebsiteRuntime--AzureTenantId` in
`hvo-central-kv` through `sync-secrets-from-keyvault.sh`.

The devcontainer identity is intentionally separate and is cached in the private
bootstrap gist for rebuild recovery. Rotations must update
`obs-azure-client-*` in `hvo-central-kv`, materialize root `.env`, verify an
isolated service-principal login, synchronize the private gist, and only then
retire the superseded credential.

Keep access to `hvoobs-kv/keys/hvo-website-dp` while any retained key-ring entry
is wrapped by that key. Do not remove the permission merely because a newer key
version or protector is introduced. Review removal only after all retained keys
have expired or have been migrated and restore-tested.

The key identifier is not secret. Credentials, key XML, cookies, and archives
must never be printed, committed, copied into an image, or synchronized through
the environment gist.

## First Rollout

The migration from the container-local plaintext key ring intentionally requires
one user reauthentication. Do not copy the old plaintext key into the new ring.
Before rollout, preserve it only as a restricted rollback archive and record its
checksum.

Render Compose before deployment and confirm that stale shell variables do not
override the deployment environment:

```bash
docker --context hvo-docker compose \
  --env-file deploy/hvo-docker/.env \
  -f deploy/hvo-docker/docker-compose.yml config --quiet
./scripts/deploy-hvo-website.sh --tag <tested-image-tag> --no-build
./tools/verify-website-data-protection.sh hvo-docker
```

The verifier confirms the volume mount, encrypted XML envelope, and absence of
the ASP.NET Core ephemeral and unencrypted key warnings without displaying key
contents.

## Backup And Restore

Back up the volume while the website is stopped or quiescent. Store the archive
off-host with restricted permissions and record a SHA-256 checksum. Never use
`docker compose down -v` or prune the named volume.

For restore testing, create a separate temporary volume, restore the archive into
it, and start an isolated website container configured with the same application
name and Key Vault protector. Verify that a payload protected before backup can
be unprotected after restore. Delete only the temporary validation volume after
the test; leave the production volume attached and unchanged.

A host restart test must confirm:

- the website returns healthy
- `hvo-website-data-protection` is still mounted at the configured directory
- `verify-website-data-protection.sh` passes
- an authenticated session created after the first rollout remains valid

## Rotation And Rollback

Use a versionless Key Vault key identifier so normal Key Vault rotation selects
the current version for newly generated Data Protection keys. Retain unwrap
permission for older key versions while their wrapped key-ring entries remain.

Rollback the application image without deleting or replacing the key volume. If
the first migration itself must be reversed, stop the replacement container,
restore the pre-rollout container and its restricted key-ring archive, then
verify health and authentication. Never run two website instances against
different writable key rings during rollback.
