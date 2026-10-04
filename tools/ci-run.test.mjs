import test from 'node:test';
import assert from 'node:assert/strict';
import { buildCommands, testFilter } from './ci-run.mjs';
import { BROWSER_TESTS } from './ci-plan.mjs';
import { planWork } from './ci-plan.mjs';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { basename, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

test('selected roots restore locked once and build warnings as errors', () => {
  const project = 'tests/Example/Example.csproj';
  const commands = buildCommands([project, project]);
  assert.equal(commands.length, 2);
  assert.ok(commands[0].includes('--locked-mode'));
  assert.ok(commands[1].includes('--no-restore'));
  assert.ok(commands[1].includes('-warnaserror'));
  assert.throws(() => buildCommands(['../Other.csproj']), /Unsupported project/);
  assert.throws(() => buildCommands(['tests/x/$(command).csproj']), /Unsupported project/);
});

test('direct lane filters partition routine, simulator and browser tests', () => {
  assert.equal(testFilter('fast', 'P'), 'TestCategory!=Integration&TestCategory!=Browser&TestCategory!=Live');
  assert.match(testFilter('simulator', 'P'), /TestCategory!=HomeAssistantIntegration&TestCategory!=SqlServerIntegration/);
  assert.equal(testFilter('browser', 'P'), 'TestCategory=Browser&TestCategory!=Live');
  assert.equal(testFilter('browser', BROWSER_TESTS), 'TestCategory!=Integration&TestCategory!=Live');
  assert.throws(() => testFilter('unknown', 'P'));
});

const trustedRunner = fileURLToPath(new URL('./ci-run.mjs', import.meta.url));
const haProjects = ['HVO.Edge.HomeAssistant.Mqtt.Tests', 'HVO.Edge.Exporter.HomeAssistant.Tests']
  .map(name => `tests/${name}/${name}.csproj`).sort();
const sqlProject = 'tests/HVO.WebSite.ApiTests/HVO.WebSite.ApiTests.csproj';
const passingReport = '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results><UnitTestResult outcome="Passed"/></Results><ResultSummary><Counters total="1" passed="1" failed="0"/></ResultSummary></TestRun>';

function fixtureExecution(lane, mode, { stale = false, missingSafety = false } = {}) {
  const root = mkdtempSync(resolve(tmpdir(), 'hvo-ci-fixture-execution-'));
  try {
    mkdirSync(resolve(root, 'tools'));
    mkdirSync(resolve(root, 'bin'));
    const source = { head: 'a'.repeat(40), base: 'a'.repeat(40), merge: 'a'.repeat(40) };
    const projects = lane === 'home-assistant' ? haProjects : lane === 'browser' ? [BROWSER_TESTS] : [sqlProject];
    const graph = { projects: projects.map(path => ({ path, test: true, lanes: [lane], references: [], inputs: [] })) };
    const plan = planWork({ source, baseGraph: graph, candidateGraph: graph, changes: [{ status: 'M', path: projects[0] }], complete: true, full: true });
    const resultsRoot = resolve(root, 'TestResults', lane === 'home-assistant' ? 'integration/home-assistant' : lane);
    if (stale) for (const project of projects) {
      const directory = resolve(resultsRoot, basename(project, '.csproj'));
      mkdirSync(directory, { recursive: true });
      writeFileSync(resolve(directory, 'stale.trx'), passingReport);
    }
    // Preparation is inert; the actual trusted Node runner and Python verifier run.
    writeFileSync(resolve(root, 'bin/dotnet'), '#!/usr/bin/env bash\npython3 tools/fixture-build-report.py "$@"\n', { mode: 0o755 });
    writeFileSync(resolve(root, 'tools/fixture-build-report.py'), `
import os,sys,subprocess
from pathlib import Path
Path('build-called').touch()
if os.environ['FIXTURE_MODE']=='build-stale' and sys.argv[1]=='build':
    lane=os.environ['FIXTURE_LANE']
    root=Path('TestResults')/('integration/home-assistant' if lane=='home-assistant' else lane)
    directory=root/Path(sys.argv[2]).stem
    directory.mkdir(parents=True,exist_ok=True)
    (directory/'stale.trx').write_text('${passingReport}')
if os.environ['FIXTURE_LANE']=='browser' and sys.argv[1]=='test':
    subprocess.run([sys.executable,'tools/fixture-helper.py',*sys.argv[1:]],check=True)
`);
    if (lane === 'sql-server' && !missingSafety) writeFileSync(resolve(root, 'tools/sql-server-integration.test.mjs'), `
import test from 'node:test';
import assert from 'node:assert/strict';
import { writeFileSync } from 'node:fs';
test('owned SQL safety check', () => {
  writeFileSync('safety-called', 'checked');
  assert.notEqual(process.env.FIXTURE_MODE, 'safety-failed', 'SQL safety failure');
});
`);
    writeFileSync(resolve(root, 'bin/pwsh'), '#!/usr/bin/env bash\nexit 0\n', { mode: 0o755 });
    writeFileSync(resolve(root, 'bin/git'), `#!/usr/bin/env bash\nprintf '%s\\n' '${source.merge}'\n`, { mode: 0o755 });
    writeFileSync(resolve(root, 'tools/ci-results.py'), "from pathlib import Path\nPath('candidate-verifier-called').touch()\n");
    writeFileSync(resolve(root, 'tools/fixture-helper.py'), `
import json,os,sys,subprocess
from pathlib import Path
args=sys.argv[1:]
Path('helper-arguments.json').write_text(json.dumps(args))
mode=os.environ['FIXTURE_MODE']
if os.environ['FIXTURE_LANE']=='home-assistant':
    start=args.index('--projects')+1
    end=next((i for i in range(start,len(args)) if args[i].startswith('--')),len(args))
    projects=args[start:end]
    root=Path(args[args.index('--results-directory')+1])
    directories=[root/Path(project).stem for project in projects]
elif os.environ['FIXTURE_LANE']=='browser':
    directories=[Path(args[args.index('--results-directory')+1])]
else:
    directories=[Path(os.environ['HVO_SQL_TEST_RESULTS_DIRECTORY'])]
if any(list(directory.glob('*.trx')) for directory in directories):
    sys.exit('trusted execution did not clear stale reports before helper')
if mode=='candidate-verifier':
    subprocess.run([sys.executable,'tools/ci-results.py'],check=True)
if mode not in ('none','candidate-verifier','build-stale'):
    for index,directory in enumerate(directories):
        if mode=='first-only' and index>0: continue
        directory.mkdir(parents=True,exist_ok=True)
        outcome='Failed' if mode=='failed' else 'NotExecuted' if mode=='ignored' else 'Passed'
        passed=1 if outcome=='Passed' else 0
        (directory/'current.trx').write_text(f'<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results><UnitTestResult outcome="{outcome}"/></Results><ResultSummary><Counters total="1" passed="{passed}" failed="0"/></ResultSummary></TestRun>')
`);
    const helperName = lane === 'home-assistant' ? 'run-home-assistant-integration-tests.sh' : 'run-sql-server-integration-tests.sh';
    writeFileSync(resolve(root, 'tools', helperName), '#!/usr/bin/env bash\npython3 tools/fixture-helper.py "$@"\n');
    const executionEnvironment = { ...process.env, PATH: `${resolve(root, 'bin')}:${process.env.PATH}`, CI_HEAD: source.head,
      CI_BASE: source.base, CI_MERGE: source.merge, CI_PLAN: JSON.stringify(plan),
      CI_COVERAGE: 'false', FIXTURE_LANE: lane, FIXTURE_MODE: mode };
    // Model a standalone CI process. Node otherwise suppresses nested --test
    // execution when it inherits this parent test worker's internal context.
    delete executionEnvironment.NODE_TEST_CONTEXT;
    const result = spawnSync(process.execPath, [trustedRunner, lane], {
      cwd: root, encoding: 'utf8', timeout: 15000,
      env: executionEnvironment
    });
    assert.ifError(result.error);
    const directories = projects.map(project => resolve(resultsRoot, basename(project, '.csproj')));
    return { status: result.status, output: result.stdout + result.stderr,
      args: existsSync(resolve(root, 'helper-arguments.json')) ? JSON.parse(readFileSync(resolve(root, 'helper-arguments.json'), 'utf8')) : null,
      safetyCalled: existsSync(resolve(root, 'safety-called')),
      buildCalled: existsSync(resolve(root, 'build-called')),
      reports: directories.map(directory => existsSync(resolve(directory, 'current.trx'))),
      stale: directories.some(directory => existsSync(resolve(directory, 'stale.trx'))),
      candidateVerifierCalled: existsSync(resolve(root, 'candidate-verifier-called')) };
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
}

for (const lane of ['home-assistant', 'sql-server', 'browser']) {
  test(`${lane}: helper exit zero without fresh tests cannot qualify`, () => {
    const result = fixtureExecution(lane, 'none');
    assert.notEqual(result.status, 0);
    assert.match(result.output, /No TRX reports/);
    assert.equal(result.candidateVerifierCalled, false);
  });

  test(`${lane}: stale passing reports are cleared before helper execution`, () => {
    const result = fixtureExecution(lane, 'none', { stale: true });
    assert.notEqual(result.status, 0);
    assert.match(result.output, /No TRX reports/);
    assert.equal(result.stale, false);
  });

  test(`${lane}: preparation cannot leave reports that replace fixture execution`, () => {
    const result = fixtureExecution(lane, 'build-stale');
    assert.notEqual(result.status, 0);
    assert.match(result.output, /No TRX reports/);
    assert.equal(result.stale, false);
  });

  test(`${lane}: candidate verification cannot substitute for trusted evidence`, () => {
    const result = fixtureExecution(lane, 'candidate-verifier');
    assert.equal(result.candidateVerifierCalled, true);
    assert.notEqual(result.status, 0);
    assert.match(result.output, /No TRX reports/);
  });

  test(`${lane}: fresh passing reports for every planned project qualify`, () => {
    const result = fixtureExecution(lane, 'all', { stale: true });
    assert.equal(result.status, 0, result.output);
    assert.equal(result.stale, false);
    assert.ok(result.reports.every(Boolean));
    assert.equal(result.candidateVerifierCalled, false);
    if (lane === 'home-assistant') {
      assert.ok(result.args.includes('--prebuilt'));
      assert.ok(result.args.includes('--ha-only'));
      assert.ok(result.args.includes('--results-directory'));
      assert.ok(haProjects.every(project => result.args.includes(project)));
    } else if (lane === 'sql-server') {
      assert.equal(result.safetyCalled, true);
    } else {
      assert.equal(result.safetyCalled, false, 'Browser execution does not provision SQL');
    }
  });

  test(`${lane}: failed or ignored fixture reports fail without an allowance`, () => {
    for (const mode of ['failed', 'ignored']) {
      const result = fixtureExecution(lane, mode);
      assert.notEqual(result.status, 0);
      assert.match(result.output, /did not complete successfully/);
    }
  });
}

test('SQL: failed safety checks stop execution before build or provisioning', () => {
  const result = fixtureExecution('sql-server', 'safety-failed');
  assert.equal(result.safetyCalled, true);
  assert.notEqual(result.status, 0);
  assert.match(result.output, /SQL safety failure/);
  assert.equal(result.buildCalled, false);
  assert.equal(result.args, null);
  assert.deepEqual(result.reports, [false]);
});

test('SQL: missing required safety checks cannot silently skip execution', () => {
  const result = fixtureExecution('sql-server', 'all', { missingSafety: true });
  assert.notEqual(result.status, 0);
  assert.equal(result.safetyCalled, false);
  assert.equal(result.buildCalled, false);
  assert.equal(result.args, null);
});

test('HA: one passing project cannot hide another missing planned report', () => {
  const result = fixtureExecution('home-assistant', 'first-only');
  assert.deepEqual(result.reports, [true, false]);
  assert.notEqual(result.status, 0);
  assert.match(result.output, /No TRX reports/);
});
