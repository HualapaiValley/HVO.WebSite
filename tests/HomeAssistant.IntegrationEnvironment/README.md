# Home Assistant integration environment

This disposable environment validates edge MQTT behavior without physical devices or production Home Assistant state. docker-compose.yml pins Home Assistant Core, Mosquitto and WireMock and uses fresh named volumes for each run.

From the repository root, run the provisioned HA category:

```bash
bash tools/run-home-assistant-integration-tests.sh
```

Standalone execution performs locked restores and builds for the three HA assemblies before provisioning. The runner validates managed YAML and HA configuration, completes onboarding, configures MQTT, checks fake-ingest normal/outage responses, then runs HomeAssistantIntegration excluding Live. Assemblies run sequentially because tests restart the shared stack. Simulator integration is separate and does not provision HA:

```bash
dotnet test HVO.WebSite.sln --no-build --no-restore --settings integration.runsettings --filter "TestCategory=Integration&TestCategory!=HomeAssistantIntegration&TestCategory!=SqlServerIntegration&TestCategory!=Browser&TestCategory!=Live" --logger trx --results-directory TestResults/integration/simulators
```

After an explicit locked restore/build, CI or a local caller can reuse prepared assemblies:

```bash
bash tools/run-home-assistant-integration-tests.sh --prebuilt --projects tests/HVO.Edge.HomeAssistant.Mqtt.Tests/HVO.Edge.HomeAssistant.Mqtt.Tests.csproj
```

--projects accepts exact repository-relative paths for HVO.Edge.HomeAssistant.Mqtt.Tests, HVO.Edge.Exporter.HomeAssistant.Tests and HVO.Tools.HomeAssistantEntityMigration.Tests. Omission selects all three; empty, unknown and repeated selections fail before provisioning. --prebuilt skips preparation; every test invocation uses --no-build --no-restore. --configuration Debug|Release and --results-directory ROOT are optional. --coverage explicitly collects XPlat coverage; routine PR CI omits it.

integration.runsettings gives each assembly a 15-minute session budget and maps inconclusive missing-fixture results to failure. Each selected project owns `TestResults/integration/home-assistant/<project>` by default. The runner clears stale TRX and strictly verifies fresh nonempty passing results, so a no-match filter or ignored/missing-fixture case cannot count as success.

Readiness is bounded. Failure captures stack status and recent broker logs before teardown. The runner revokes the temporary refresh token after successful execution and removes its containers, networks, volumes, derived images and temporary Docker configuration even on failure. No credentials or generated HA .storage state are intentionally retained. Update image pins deliberately and verify the fixture before merging an update.

MQTT command entities remain excluded until the separate safety contract defines authorization, idempotency, stale-command rejection and acknowledgement semantics. Physical-device behavior belongs in opt-in Live tests. See [test lanes](../../docs/development/testing.md) and [selective CI](../../docs/development/selective-ci.md) for the remaining adoption and validation requirements.
