# Ingest ownership and proxy trust

Every source-bearing POST uses the same source-authority check before persistence. A batch with any unauthorized valid source is denied in full. The authority is the API key's `source` claims, not a CloudEvents envelope's `source`. `scope` claims remain required independently.

| Write shape | Identity checked |
|---|---|
| Power readings, raw JSON or CloudEvents batch | Every payload `SourceId` and `SourceSystem` |
| Power device inventory, configuration, energy, gateway status | Snapshot `SourceId` and `SourceSystem` |
| Power inverter/MPPT detail, single and batch | Every snapshot `SourceId` and `SourceSystem` |
| SmartShunt observations batch | Each matched summary/detail source; exact owner required |
| Weather raw single, raw JSON or CloudEvents batch | Every `StationId` and `SourceSystem` |
| Davis weather archive batch | Every `StationId`; fixed Davis source system |

The HTTP regression matrix discovers these POST actions, so an added route requires an explicit policy/test decision. BMS writes carry `DeviceAddress`, not `SourceId`/`StationId`; their existing device/scope contract is preserved.

Source IDs are trimmed consistently for authorization and persistence. `kasa:` and `govee:` prefixes (including case aliases), and `homeassistant-*` source systems, require an exact ordinal `source` claim. SmartShunt always requires that exact claim, even for otherwise unreserved sources. An active, unexpired claim on another key reserves a source across every write shape. SQL-side comparisons retain database collation equivalences and check trimmed/case aliases; these aliases never grant exact ownership. Unreserved legacy sources remain writable with the existing ingest scope. Multiple active keys claiming equivalent IDs cause conservative denial rather than allowing one claimant to overwrite another. Provision unique source owners and use the exact spelling from their claims.

Both credential expiry and source reservation use the injected `TimeProvider`. UTC `ExpiresAt` is an inclusive hard boundary: at expiry the credential is rejected and its reservation is no longer active. Authentication caches are clipped to that boundary; nonexpiring positive credentials recheck within five minutes, and negative lookups within thirty seconds. Revocation and changes to cached key claims retain that bounded five-minute delay; absolute credential expiry does not.

## Forwarded headers

Enable forwarding with `ForwardedHeaders:Enabled=true` or the existing host switch `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`. Configure the immediate trusted peer explicitly when it is not loopback:

```json
{
  "ForwardedHeaders": {
    "Enabled": true,
    "KnownProxies": [ "192.0.2.10" ],
    "KnownNetworks": [ "198.51.100.0/24" ]
  }
}
```

These documentation addresses are examples, not a description of production. Environment equivalents include `ForwardedHeaders__KnownProxies__0` and `ForwardedHeaders__KnownNetworks__0`. Proxies are IP literals; networks are canonical CIDRs. Malformed addresses/networks, unspecified proxy addresses, and `/0` networks fail startup. When neither list is supplied, trust is limited to IPv4 loopback `127.0.0.0/8` and IPv6 loopback `::1`. Explicit lists replace those defaults. Both lists cannot jointly express an unrestricted/empty trust policy.

Final options restore these limits even when the framework's host switch initially clears trust lists. Processing accepts only the nearest hop (`ForwardLimit=1`), including IPv4-mapped IPv6 peers in configured IPv4 networks. There is no separate manual `X-Forwarded-Proto` scheme override. Unknown peers cannot alter scheme or client IP through these headers. Missing or malformed forwarding retains the framework's bounded behavior; operators must make the immediate trusted proxy overwrite untrusted incoming headers.

Before rollout, determine the actual immediate peer/network from the deployment topology and configure the smallest applicable trust range. A host-only opt-in that previously relied on trusting any peer now uses loopback defaults. Remote TLS termination therefore requires explicit trust configuration. Verify browser HTTPS, Entra sign-in and `/signin-oidc` redirect URI with the real proxy; an untrusted peer produces the application's original scheme and may break sign-in if its registered redirect requires HTTPS. Do not increase the hop limit merely because an upstream chain exists: the immediate proxy should provide the authoritative nearest values. This change does not deploy or alter Entra registration/topology. Local host tests stub OIDC metadata and exercise redirects without contacting Entra.

The fast route matrix uses EF InMemory to verify authorization and absence of writes. It does not prove SQL Server collation/translation. Provider-specific coverage belongs to the isolated SQL lane from #409 before final epic acceptance.
