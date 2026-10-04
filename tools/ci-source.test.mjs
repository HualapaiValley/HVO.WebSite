import test from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { planWork } from './ci-plan.mjs';

const reader = fileURLToPath(new URL('./ci-source.mjs', import.meta.url));
const fast = '[TestClass] class Fast { [TestMethod] public void Runs() {} }';
const fixture = '[TestClass] class Fixture { [TestMethod][TestCategory("Integration")][TestCategory("HomeAssistantIntegration")] public void RequiresBroker() {} }';

function readFixture(t, include, sharedFiles, identity = '<PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup>', links = {}) {
  const directory = mkdtempSync(join(tmpdir(), 'hvo-ci-source-'));
  t.after(() => rmSync(directory, { recursive: true, force: true }));
  const files = {
    'HVO.WebSite.sln': 'Project("id") = "Tests", "tests/Tests/Tests.csproj", "a"',
    'tests/Tests/Tests.csproj': `<Project Sdk="Microsoft.NET.Sdk">${identity}${include ? `<ItemGroup><Compile Include="${include}"/></ItemGroup>` : ''}</Project>`,
    'tests/Tests/Fast.cs': fast,
    ...sharedFiles
  };
  for (const [path, source] of Object.entries(files)) {
    mkdirSync(dirname(join(directory, path)), { recursive: true });
    writeFileSync(join(directory, path), source);
  }
  for (const [path, target] of Object.entries(links)) {
    mkdirSync(dirname(join(directory, path)), { recursive: true });
    symlinkSync(target, join(directory, path));
  }
  const git = args => execFileSync('git', args, { cwd: directory, encoding: 'utf8' }).trim();
  git(['init', '-q']); git(['add', '.']);
  git(['-c', 'user.name=CI fixture', '-c', 'user.email=ci@example.invalid', 'commit', '-qm', 'Fixture']);
  const commit = git(['rev-parse', 'HEAD']);
  return {
    commit,
    read: () => JSON.parse(execFileSync(process.execPath, ['--input-type=module', '-e',
      'const [,reader,commit] = process.argv; process.argv.length = 1; const {readGraph} = await import(reader); console.log(JSON.stringify(readGraph(commit)));', reader, commit
    ], { cwd: directory, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }))
  };
}

test('linked Compile files and globs contribute fixture lanes and changed-input ownership', t => {
  for (const [include, path] of [['../../shared/Hidden.cs', 'shared/Hidden.cs'], ['../../shared/**/*.cs', 'shared/nested/Hidden.cs']]) {
    const { commit, read } = readFixture(t, include, { [path]: fixture });
    const graph = read();
    assert.equal(graph.projects[0].testMethods, 2);
    assert.deepEqual(graph.projects[0].lanes, ['fast', 'home-assistant']);
    const plan = planWork({ source: { head: commit, base: commit, merge: commit }, baseGraph: graph, candidateGraph: graph,
      changes: [{ status: 'M', path }], complete: true });
    assert.deepEqual(plan.lanes['home-assistant'], ['tests/Tests/Tests.csproj']);
  }
});

test('untracked or unsupported linked test sources fail planning visibly', t => {
  const missing = readFixture(t, '../../shared/Missing.cs', {});
  assert.throws(missing.read, /Compile input has no tracked C# sources/);
  const conditional = readFixture(t, '../../shared/Hidden.cs', { 'shared/Hidden.cs': '#if FIXTURE\n' + fixture + '\n#endif' });
  assert.throws(conditional.read, /unsupported test metadata syntax/);
});

test('implicit Test SDK identity retains all lanes when IsTestProject is omitted', t => {
  const { commit, read } = readFixture(t, '../../shared/Hidden.cs', { 'shared/Hidden.cs': fixture },
    '<ItemGroup><PackageReference Include="microsoft.net.test.sdk"/></ItemGroup>');
  const graph = read();
  assert.equal(graph.projects[0].test, true);
  const plan = planWork({ source: { head: commit, base: commit, merge: commit }, baseGraph: graph, candidateGraph: graph,
    changes: [{ status: 'M', path: 'tests/Tests/Tests.csproj' }], complete: true });
  assert.deepEqual(plan.lanes.fast, ['tests/Tests/Tests.csproj']);
  assert.deepEqual(plan.lanes['home-assistant'], ['tests/Tests/Tests.csproj']);
});

test('symlinked C# files and directories fail instead of hiding compiled tests', t => {
  for (const links of [
    { 'tests/Tests/Hidden.cs': '../../shared/Hidden.cs' },
    { 'tests/Tests/linked': '../../shared' }
  ]) {
    const { read } = readFixture(t, null, { 'shared/Hidden.cs': fixture }, undefined, links);
    assert.throws(read, /Unsupported Git entry/);
  }
});

test('external input globs cannot traverse symlinks; unrelated agent links are allowed', t => {
  const external = readFixture(t, '../../shared/**/*.cs', { 'shared/Present.cs': fast, 'hidden/Fixture.cs': fixture },
    undefined, { 'shared/linked': '../hidden' });
  assert.throws(external.read, /Unsupported Git entry/);
  const unrelated = readFixture(t, null, { '.agents/skills/README.md': 'Skill documentation' },
    undefined, { '.claude/skills': '../.agents/skills' });
  assert.deepEqual(unrelated.read().projects[0].lanes, ['fast']);
});
