import test from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { planWork, aggregate, verifyPlan, BROWSER_TESTS, WEBSITE_TESTS, HA_TESTS } from './ci-plan.mjs';
import { readGraph, parseDiff, classifyTests } from './ci-source.mjs';

const source = { head: '1'.repeat(40), base: '2'.repeat(40), merge: '3'.repeat(40) };
const repositoryHead = execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
const graph = readGraph(repositoryHead);
const changed = path => ({ status: 'M', path });
const plan = (paths, overrides = {}) => planWork({ source, baseGraph: graph, candidateGraph: graph, changes: paths.map(changed), complete: true, ...overrides });
const p = (area, name) => `${area}/${name}/${name}.csproj`;

test('real JK leaf selects its own tests/simulators/image without HA or Chromium', () => {
  const result = plan(['src/HVO.Hardware.JkBms/Program.cs']);
  assert.equal(result.mode, 'selected');
  assert.deepEqual(result.tests, [p('tests', 'HVO.Hardware.JkBms.Tests')]);
  assert.deepEqual(result.images, ['jkbms']);
  assert.deepEqual(result.lanes.simulator, result.tests);
  assert.deepEqual(result.lanes.browser, []);
  assert.deepEqual(result.lanes['home-assistant'], []);
  assert.ok(result.buildDependencies.includes(p('src', 'HVO.Edge.HomeAssistant.Mqtt')));
  // The BLE console uses its own protocol path and has no project reference to
  // the gateway; a shared name alone must not create an invented dependency.
  assert.ok(!result.debugRoots.includes(p('tools', 'HVO.Tools.JkBleConsole')));
  assert.equal(result.operations, false);
});
test('website/theme add explicit browser ownership but no headless images', () => {
  for (const name of ['HVO.WebSite.v9', 'HVO.WebSite.Themes']) {
    const result = plan([`src/${name}/Example.cs`]);
    assert.ok(result.tests.includes(WEBSITE_TESTS));
    assert.ok(result.lanes.browser.includes(BROWSER_TESTS));
    assert.deepEqual(result.images, ['website']);
    assert.deepEqual(result.lanes['home-assistant'], []);
  }
});
test('shared closure and cross-domain references retain every consumer', () => {
  const contracts = plan(['src/HVO.Edge.Contracts/Example.cs']);
  assert.equal(contracts.tests.length, 13); // Twelve references plus explicit browser ownership.
  assert.equal(contracts.images.length, 6);
  const outbox = plan(['src/HVO.Edge.Outbox/Example.cs']);
  assert.equal(outbox.tests.length, 12);
  assert.equal(outbox.images.length, 6);
  assert.ok(plan(['src/HVO.Hardware.VictronSmartShunt/Program.cs']).tests.includes(WEBSITE_TESTS));
  assert.ok(plan(['src/HVO.Hardware.DavisVantagePro2/Program.cs']).tests.includes(p('tests', 'HVO.Tools.HomeAssistantEntityMigration.Tests')));
});
test('test-only work and hidden copied/read inputs select their owners', () => {
  const tests = plan(['tests/HVO.Hardware.JkBms.Tests/NewTests.cs']);
  assert.deepEqual(tests.images, []);
  assert.equal(tests.tests.length, 1);
  assert.equal(tests.releaseRoots.length, 0);
  assert.ok(plan(['src/HVO.Database/Tables/Legacy.sql']).tests.includes(WEBSITE_TESTS));
  const ha = plan(['deploy/home-assistant/configuration/frontend/hvo-weather-wind-card.js']);
  assert.ok(ha.tests.includes(WEBSITE_TESTS));
  assert.ok(ha.tests.includes(BROWSER_TESTS));
  assert.deepEqual(ha.lanes['home-assistant'], HA_TESTS.sort());
  assert.ok(plan(['tools/HVO.Tools.HomeAssistantEntityMigration/hvo-energy-preferences.json']).tests.includes(p('tests', 'HVO.Tools.HomeAssistantEntityMigration.Tests')));
  assert.ok(plan(['scripts/deploy-home-assistant-dashboard.sh']).tests.includes(WEBSITE_TESTS));
});
test('only known non-executable prose gets an explicit empty plan, mixed work unions', () => {
  const docs = plan(['docs/ARCHITECTURE.md', 'README.md']);
  assert.equal(docs.mode, 'selected'); assert.deepEqual(docs.debugRoots, []); assert.deepEqual(docs.images, []);
  assert.match(docs.emptyReasons.build, /documentation/);
  assert.equal(plan(['docs/new-executable.sh']).mode, 'full');
  const mixed = plan(['README.md', 'src/HVO.Hardware.JkBms/Program.cs']);
  assert.deepEqual(mixed.images, ['jkbms']);
});
test('global, unknown and unsupported evaluation broaden, missing diffs fail', () => {
  for (const path of ['global.json', 'Directory.Packages.props', 'Directory.Build.targets', 'NuGet.Config', 'integration.runsettings', 'HVO.WebSite.sln', 'tools/ci-plan.mjs', '.github/workflows/ci.yml', '.dockerignore', 'new/unknown.txt']) {
    assert.equal(plan([path]).mode, 'full', path);
  }
  assert.equal(plan([], { trigger: 'push' }).mode, 'full');
  assert.equal(plan(['README.md'], { trigger: 'schedule' }).mode, 'full');
  assert.equal(plan(['README.md'], { full: true }).mode, 'full');
  assert.equal(plan(['README.md'], { candidateGraph: { ...graph, unsupported: ['conditional reference'] } }).mode, 'full');
  assert.throws(() => plan(['README.md'], { complete: false }), /incomplete/);
  assert.throws(() => plan(['README.md'], { source: { ...source, merge: null } }), /tuple/);
});
test('base and candidate references, rename sides and deleted source remain covered', () => {
  const candidateGraph = structuredClone(graph);
  const jk = candidateGraph.projects.find(x => x.path === p('src', 'HVO.Hardware.JkBms'));
  jk.references = [];
  const result = plan(['src/HVO.Edge.Outbox/Example.cs'], { candidateGraph });
  assert.ok(result.tests.includes(p('tests', 'HVO.Hardware.JkBms.Tests')));
  const rename = plan([], { changes: [{ status: 'R100', previousPath: 'src/HVO.Hardware.JkBms/Old.cs', path: 'src/HVO.Hardware.Eg4/New.cs' }] });
  assert.ok(rename.images.includes('jkbms')); assert.ok(rename.images.includes('eg4'));
  assert.deepEqual(plan([], { changes: [{ status: 'D', path: 'src/HVO.Hardware.JkBms/Old.cs' }] }).images, ['jkbms']);
  assert.equal(plan(['src/NewApp/NewApp.csproj']).mode, 'full');
});
test('unsupported reference evaluation cannot prune full build obligations', () => {
  const candidateGraph = structuredClone(graph);
  candidateGraph.unsupported = ['App: conditional ProjectReference to Lib'];
  const result = plan(['README.md'], { candidateGraph });
  assert.equal(result.mode, 'full');
  assert.deepEqual(result.debugRoots, graph.projects.map(project => project.path).sort());
  assert.deepEqual(result.releaseRoots, graph.projects.filter(project => !project.test).map(project => project.path).sort());
});
test('copied documentation takes precedence over prose classification', () => {
  const candidateGraph = structuredClone(graph);
  candidateGraph.projects.find(x => x.path === WEBSITE_TESTS).inputs.push('docs/fixture.md');
  assert.ok(plan(['docs/fixture.md'], { candidateGraph }).tests.includes(WEBSITE_TESTS));
});
test('cyclic components fail visibly instead of disappearing from full build roots', () => {
  for (const references of [[['tools/B/B.csproj'], ['tools/A/A.csproj']], [['tools/A/A.csproj'], []]]) {
    const candidateGraph = structuredClone(graph);
    candidateGraph.projects.push(...['A', 'B'].map((name, index) => ({
      path: `tools/${name}/${name}.csproj`, references: references[index], inputs: [], test: false
    })));
    assert.throws(() => plan(['HVO.WebSite.sln'], { candidateGraph }), /Cyclic project reference/);
  }
  const result = plan(['HVO.WebSite.sln']);
  const reachable = roots => {
    const found = new Set(roots);
    for (const path of found) for (const next of graph.projects.find(value => value.path === path)?.references || []) found.add(next);
    return found;
  };
  assert.deepEqual([...reachable(result.debugRoots)].sort(), result.buildDependencies);
  assert.ok(result.affectedProjects.filter(path => !graph.projects.find(value => value.path === path).test).every(path => reachable(result.releaseRoots).has(path)));
});
test('plan is deterministic and aggregation rejects stale, missing, failed or skipped work', () => {
  const result = plan(['README.md', 'src/HVO.Hardware.JkBms/Program.cs']);
  assert.deepEqual(plan(['src/HVO.Hardware.JkBms/Program.cs', 'README.md']), result);
  const results = { validation: 'success', 'home-assistant': 'skipped', 'sql-server': 'skipped', browser: 'skipped', operations: 'skipped' };
  assert.equal(aggregate(result, source, results), true);
  for (const status of ['failure', 'cancelled', 'skipped', undefined]) assert.throws(() => aggregate(result, source, { ...results, validation: status }), /validation/);
  assert.throws(() => aggregate(result, { ...source, head: '4'.repeat(40) }, results), /Stale/);
  assert.throws(() => verifyPlan({ ...result, images: [] }, source), /digest/);
  const website = plan(['src/HVO.WebSite.v9/Program.cs']);
  assert.throws(() => aggregate(website, source, results), /browser/);
  assert.equal(aggregate(website, source, { ...results, browser: 'success' }), true);
});
test('diff parser preserves renames/deletions and rejects truncation and unsupported status', () => {
  assert.deepEqual(parseDiff('R100\0old.cs\0new.cs\0D\0gone.cs\0'), [{ status: 'R100', path: 'new.cs', previousPath: 'old.cs' }, { status: 'D', path: 'gone.cs' }]);
  for (const raw of ['M\0file.cs', 'R100\0old.cs\0', 'U\0conflict.cs\0']) assert.throws(() => parseDiff(raw));
});
test('literal categories partition required work and preserve browser assembly requirements', () => {
  const code = category => `[TestClass]${category} class T { [TestMethod] public void Runs() {} }`;
  assert.deepEqual(classifyTests(code(''), 'T.cs', 'tests/T/T.csproj').lanes, ['fast']);
  assert.deepEqual(classifyTests(code(''), 'T.cs', BROWSER_TESTS).lanes, ['browser']);
  for (const [category, lane] of [['Integration', 'simulator'], ['Browser', 'browser']]) assert.deepEqual(classifyTests(code(`[TestCategory("${category}")]`), 'T.cs', 'tests/T/T.csproj').lanes, [lane]);
  assert.deepEqual(classifyTests(code('[TestCategory("Integration"),TestCategory("SqlServerIntegration")]'), 'T.cs', 'tests/T/T.csproj').lanes, ['sql-server']);
  assert.deepEqual(classifyTests(code('[TestCategory("Live")]'), 'T.cs', 'tests/T/T.csproj').lanes, []);
  assert.throws(() => classifyTests(code('[TestCategory(Category)]'), 'T.cs', 'tests/T/T.csproj'), /literal/);
  assert.equal(classifyTests('using Crc = Library.Crc; ' + code(''), 'T.cs', 'tests/T/T.csproj').unsupported, false);
  assert.throws(() => classifyTests(code('[TestCategory("Integration")]'), 'T.cs', BROWSER_TESTS), /combined fixture/);
  assert.throws(() => classifyTests(code('[TestCategory("Integration"), TestCategory("Browser")]'), 'T.cs', 'tests/T/T.csproj'), /separate fixture/);
  assert.throws(() => classifyTests(code('[TestCategory("Integration"), TestCategory("HomeAssistantIntegration"), TestCategory("SqlServerIntegration")]'), 'T.cs', 'tests/T/T.csproj'), /both provisioned/);
});
test('helper inheritance does not broaden selection but unknown test inheritance does', () => {
  const classify = code => classifyTests(code, 'T.cs', 'tests/T/T.csproj');
  const ordinary = '[TestClass] class T { [TestMethod] public void Runs() {} private class Clock : TimeProvider {} }';
  assert.equal(classify(ordinary).unsupported, false);
  assert.equal(classify(ordinary + ' class Factory : WebApplicationFactory<Program> {}').unsupported, false);
  for (const base of ['BunitContext', 'PageTest', 'IDisposable', 'IAsyncDisposable']) {
    assert.equal(classify(ordinary.replace('class T {', `class T : ${base} {`)).unsupported, false);
  }
  for (const code of [
    ordinary.replace('class T {', 'class T : InheritedTests {'),
    '[TestClass] class T : InheritedTests {}',
    ordinary.replace('class T {', 'partial class T {'),
    '#if INCLUDE_TESTS\n' + ordinary + '\n#endif',
    'using Category = CustomAttribute; [Category] ' + ordinary
  ]) assert.equal(classify(code).unsupported, true, code);
});
