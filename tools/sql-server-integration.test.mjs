import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { spawnSync } from 'node:child_process';
import test from 'node:test';

const runner = resolve('tools/run-sql-server-integration-tests.sh');
const reportVerifier = resolve('tools/verify-sql-server-test-results.py');
const passingReport = '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><ResultSummary><Counters total="2" passed="2"/></ResultSummary></TestRun>';

function exercise(mode, coverage = false) {
  const directory = mkdtempSync(join(tmpdir(), 'hvo-sql-runner-check-'));
  try {
    const bin = join(directory, 'bin');
    mkdirSync(bin);
    const log = join(directory, 'calls');
    // A missing report must not accidentally qualify a previous successful invocation.
    writeFileSync(join(directory, 'sql-server.trx'), passingReport);
    writeFileSync(join(bin, 'docker'), `#!/usr/bin/env bash
set -eu
printf '%s\\n' "$*" >> "$SQL_RUNNER_CHECK_LOG"
if [[ "$*" == *"context inspect"* ]]; then
  [[ "$SQL_RUNNER_CHECK_MODE" == remote ]] && printf 'tcp://remote:2375' || printf 'unix:///var/run/docker.sock'
elif [[ "$*" == *" run "* ]]; then
  for argument in "$@"; do
    [[ "$argument" == hvo.sql-test-run=* ]] && printf '%s' "\${argument#*=}" > "$SQL_RUNNER_CHECK_NONCE"
  done
  [[ "$SQL_RUNNER_CHECK_MODE" == startup-fails ]] && exit 1
  printf fixture-container
elif [[ "$*" == *" ps "* && "$*" == *"name="* && "$SQL_RUNNER_CHECK_MODE" == startup-fails ]]; then
  printf fixture-container
elif [[ "$*" == *" port "* ]]; then
  printf '127.0.0.1:49152'
elif [[ "$*" == *" exec "* ]]; then
  [[ "$SQL_RUNNER_CHECK_MODE" == unavailable ]] && exit 1
elif [[ "$*" == *" inspect "* ]]; then
  [[ "$SQL_RUNNER_CHECK_MODE" == foreign ]] && printf foreign-owner || cat "$SQL_RUNNER_CHECK_NONCE"
elif [[ "$*" == *" rm "* && "$SQL_RUNNER_CHECK_MODE" == cleanup-fails ]]; then
  exit 1
fi
exit 0
`, { mode: 0o755 });
    writeFileSync(join(bin, 'sleep'), '#!/usr/bin/env bash\nexit 0\n', { mode: 0o755 });
    writeFileSync(join(bin, 'dotnet'), `#!/usr/bin/env bash
set -eu
printf 'dotnet %s\\n' "$*" >> "$SQL_RUNNER_CHECK_LOG"
[[ "$SQL_RUNNER_CHECK_MODE" == tests-fail ]] && exit 1
if [[ "$SQL_RUNNER_CHECK_MODE" != missing-results ]]; then
  printf '%s' '$REPORT' > "$HVO_SQL_TEST_RESULTS_DIRECTORY/sql-server.trx"
fi
`.replace('$REPORT', passingReport), { mode: 0o755 });
    const result = spawnSync('bash', [runner], {
      env: { ...process.env, PATH: `${bin}:${process.env.PATH}`,
        HVO_SQL_TEST_RESULTS_DIRECTORY: directory, CI_COVERAGE: String(coverage), SQL_RUNNER_CHECK_LOG: log,
        SQL_RUNNER_CHECK_NONCE: join(directory, 'nonce'), SQL_RUNNER_CHECK_MODE: mode },
      encoding: 'utf8', timeout: 15000
    });
    assert.equal(result.error, undefined);
    return { status: result.status, output: result.stdout + result.stderr,
      calls: existsSync(log) ? readFileSync(log, 'utf8') : '' };
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
}

test('runner provisions local owned resources, admits passing results and cleans only its container', () => {
  const result = exercise('pass');
  assert.equal(result.status, 0, result.output);
  assert.match(result.calls, /--publish 127\.0\.0\.1::1433/);
  assert.match(result.calls, /--filter TestCategory=SqlServerIntegration&TestCategory!=Live/);
  assert.match(result.calls, /--no-build --no-restore/);
  assert.match(result.calls, /--settings \S*integration\.runsettings/);
  assert.doesNotMatch(result.calls, /--collect/);
  assert.match(result.calls, /--context default rm -fv fixture-container/);
  assert.doesNotMatch(result.calls, /prune|production|Password=|Hvo-Test-/);
  const covered = exercise('pass', true);
  assert.equal(covered.status, 0, covered.output);
  assert.match(covered.calls, /--collect:XPlat Code Coverage/);
});

test('remote Docker endpoints fail before resource creation', () => {
  const result = exercise('remote');
  assert.notEqual(result.status, 0);
  assert.doesNotMatch(result.calls, / run |dotnet/);
});

test('unavailable fixture fails and still cleans its owned resource', () => {
  const result = exercise('unavailable');
  assert.notEqual(result.status, 0);
  assert.doesNotMatch(result.calls, /dotnet/);
  assert.match(result.calls, / rm -fv fixture-container/);
});

test('test failure propagates with cleanup', () => {
  const result = exercise('tests-fail');
  assert.notEqual(result.status, 0);
  assert.match(result.calls, / rm -fv fixture-container/);
});

test('startup failure after container creation finds and cleans only the owned container', () => {
  const result = exercise('startup-fails');
  assert.notEqual(result.status, 0);
  assert.doesNotMatch(result.calls, /dotnet/);
  assert.match(result.calls, / rm -fv fixture-container/);
});

test('foreign ownership forbids deletion and fails the lane', () => {
  const result = exercise('foreign');
  assert.notEqual(result.status, 0);
  assert.doesNotMatch(result.calls, / rm /);
});

test('cleanup failure and absent report cannot count as passing integration', () => {
  assert.notEqual(exercise('cleanup-fails').status, 0);
  assert.notEqual(exercise('missing-results').status, 0);
});

test('empty or skipped result reports do not qualify', () => {
  const directory = mkdtempSync(join(tmpdir(), 'hvo-sql-report-check-'));
  try {
    for (const counters of ['total="0" passed="0"', 'total="2" passed="1" notExecuted="1"']) {
      const path = join(directory, 'tests.trx');
      writeFileSync(path, passingReport.replace('total="2" passed="2"', counters));
      const result = spawnSync('python3', [reportVerifier, path], { encoding: 'utf8' });
      assert.notEqual(result.status, 0);
    }
  } finally { rmSync(directory, { recursive: true, force: true }); }
});
