# Disposable canonical SQL Server tests

The provisioned SQL lane qualifies the production EF Core provider and migration chain.
It complements fast InMemory policy and SQLite rollback tests; neither replaces it.

Build with the exact SDK in `global.json`, then run:

```bash
dotnet restore HVO.WebSite.sln --locked-mode --nologo
dotnet build HVO.WebSite.sln --no-restore --nologo
bash tools/run-sql-server-integration-tests.sh
```

Requirements: a local Linux x64 Docker daemon on the default Unix socket, .NET,
OpenSSL and Python 3. The pinned SQL Server 2022 CU26 image requires roughly 2 GB
of available memory. First use pulls the image; the selected CI `sql-server` job runs
on Ubuntu after building its owned API assembly. No live gateway, Azure secret or
production SQL access is used.

The runner generates throwaway credentials and a unique ownership nonce, publishes
SQL only on a random loopback port and creates no shared volume/network. It refuses
remote Docker endpoints. Cleanup verifies the container's ownership label and removes
only that container and its anonymous volumes; cleanup failure fails the lane.
Passwords are passed through a temporary private environment file and are not logged.

The C# fixture accepts only the runner's loopback endpoint, password and nonce via
`HVO_SQL_TEST_SERVER`, `HVO_SQL_TEST_PASSWORD`, and `HVO_SQL_TEST_RUN_ID`. It also checks
the SQL instance's test ownership marker before creating a database. It never reads
application configuration or a production connection string. Each test owns a fresh
database with a generated `hvo_sql_test_` name, runs real migrations, and drops only
that database. The production `AddHvoDataServices` registration provides MARS, retry
execution strategy, command timeout and `v9.__EFMigrationsHistory` behavior.

Supported upgrade checkpoints are the checked-in gateway-status schema
`20260528073744_AddGatewayStatusSnapshots` and the pre-epic latest schema
`20260812031452_AddSmartShuntDetailSnapshots`. Tests seed existing weather/power rows,
migrate forward and verify identities/data survive. Clean bootstrap verifies every
actual migration and canonical unique indexes. These checkpoints define tested schema
support; they do not assert the migration state of a production database.

Provider checks exercise grouped latest reads, source-isolated history, two-context
duplicate/partial-overlap conflicts and atomic SmartShunt summary/detail fault/retry.
`SqlServerDatabase.CreateContext` and `BeforeFirstSaveInterceptor` are reusable seams
for the ingest and snapshot corrections in #400/#402 and weather/history queries in
#405/#410. The fixture foundation records actual SQL rollback behavior; application
regressions for those corrections accompany their own changes.

All SQL behavior tests have both `Integration` and `SqlServerIntegration` categories.
The fast lane excludes `Integration`, and the HA runner excludes `SqlServerIntegration`.
Running this category without provisioning fails clearly. An absent/empty/skipped TRX
also fails the required runner. Standalone results are retained separately under
`TestResults/sql-server/sql-server.trx`; `HVO_SQL_TEST_RESULTS_DIRECTORY` can select a
different evidence directory. Selected CI uses `TestResults/sql-server/HVO.WebSite.ApiTests`
and retains its owned SQL artifact. Its trusted executor independently checks fresh
nonempty passing results, so the candidate runner cannot attest itself merely by
returning success. The planner discovers SQL ownership from the actual test categories.
The runner explicitly uses `integration.runsettings`: a 900000ms test-session bound
and `MapInconclusiveToFailed=true`. Missing required fixture data fails rather than
becoming an ignored/inconclusive success. This overrides the fast-lane default settings.
Execution explicitly reuses build/restore with --no-build --no-restore, excludes Live,
and enables XPlat Code Coverage only when the inherited `CI_COVERAGE` is true. This
preserves selected CI's PR-versus-main/nightly/manual coverage policy.

Run `node --test tools/sql-server-integration.test.mjs` for runner ownership, failure,
and result-admission tests. The owned SQL executor requires these checks before build
or provider provisioning; missing or failed checks stop the lane. This executor
correction first becomes trusted after merge. Local candidate execution cannot prove
the old target handler ran it; merged hosted SQL qualification remains required.
No schema or production runtime configuration changes are
introduced by this fixture. No deployment or hardware acceptance follows from it.
