#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
sql_image='mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04@sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89'
run_id="$(openssl rand -hex 16)"
container_name="hvo-sql-test-$run_id"
fixture_dir="$(mktemp -d)"
container_id=""
provision_started=0
results_directory="${HVO_SQL_TEST_RESULTS_DIRECTORY:-$repo_root/TestResults/sql-server}"
coverage=()
if [[ "${CI_COVERAGE:-false}" == true ]]; then
    coverage=(--collect:"XPlat Code Coverage")
fi
mkdir -p "$results_directory"
chmod 700 "$fixture_dir"

cleanup() {
    exit_status=$?
    trap - EXIT
    # docker run can create a container and fail before returning its ID.
    if (( provision_started == 1 )) && [[ -z "$container_id" ]]; then
        container_id="$(docker --context default ps -aq \
            --filter "name=^/$container_name$" --filter "label=hvo.sql-test-run=$run_id")"
    fi
    if [[ -n "$container_id" ]]; then
        owner="$(docker --context default inspect "$container_id" --format '{{index .Config.Labels "hvo.sql-test-run"}}' 2>/dev/null || true)"
        if [[ "$owner" != "$run_id" ]]; then
            printf 'Refusing SQL fixture cleanup: ownership does not match.\n' >&2
            exit_status=1
        elif ! docker --context default rm -fv "$container_id" >/dev/null; then
            printf 'Failed to remove the owned SQL fixture.\n' >&2
            exit_status=1
        fi
        if [[ -n "$(docker --context default ps -aq --filter "label=hvo.sql-test-run=$run_id")" ]]; then
            printf 'Owned SQL fixture remains after cleanup.\n' >&2
            exit_status=1
        fi
    fi
    rm -rf "$fixture_dir"
    exit "$exit_status"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

docker_endpoint="$(docker --context default context inspect default --format '{{.Endpoints.docker.Host}}')"
if [[ "$docker_endpoint" != unix://* ]]; then
    printf 'SQL tests require the local default Docker socket; remote contexts are refused.\n' >&2
    exit 1
fi
docker --context default info >/dev/null
password="Hvo-Test-$(openssl rand -hex 24)!"
printf 'ACCEPT_EULA=Y\nMSSQL_PID=Developer\nMSSQL_SA_PASSWORD=%s\n' "$password" >"$fixture_dir/sql.env"
chmod 600 "$fixture_dir/sql.env"
provision_started=1
container_id="$(docker --context default run -d --name "$container_name" \
    --label "hvo.sql-test-run=$run_id" --env-file "$fixture_dir/sql.env" \
    --publish 127.0.0.1::1433 "$sql_image")"
sql_address="$(docker --context default port "$container_id" 1433/tcp)"
sql_port="${sql_address##*:}"
if [[ "$sql_address" != 127.0.0.1:* || ! "$sql_port" =~ ^[0-9]+$ ]]; then
    printf 'SQL fixture did not publish an isolated loopback endpoint.\n' >&2
    exit 1
fi

ready=0
for _ in $(seq 1 60); do
    if docker --context default exec "$container_id" bash -c \
        'SQLCMDPASSWORD=$MSSQL_SA_PASSWORD exec /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -l 2 -b -Q "SELECT 1"' >/dev/null 2>&1; then
        ready=1
        break
    fi
    sleep 2
done
if (( ready == 0 )); then
    printf 'Required SQL Server fixture did not become ready within 120 seconds.\n' >&2
    exit 1
fi

docker --context default exec "$container_id" bash -c \
    'SQLCMDPASSWORD=$MSSQL_SA_PASSWORD exec /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -Q "$1"' \
    fixture-owner "EXEC master.sys.sp_addextendedproperty @name=N'hvo.sql-test-run', @value=N'$run_id'" >/dev/null

rm -f "$results_directory/sql-server.trx"
HVO_SQL_TEST_SERVER="127.0.0.1,$sql_port" \
HVO_SQL_TEST_PASSWORD="$password" \
HVO_SQL_TEST_RUN_ID="$run_id" \
dotnet test "$repo_root/tests/HVO.WebSite.ApiTests/HVO.WebSite.ApiTests.csproj" \
    --no-build --no-restore -c Debug --nologo -v minimal --settings "$repo_root/integration.runsettings" \
    --filter "TestCategory=SqlServerIntegration&TestCategory!=Live" "${coverage[@]}" \
    --logger "trx;LogFileName=sql-server.trx" --results-directory "$results_directory"

python3 "$repo_root/tools/verify-sql-server-test-results.py" "$results_directory/sql-server.trx"
