// Read candidate files as data from immutable Git objects. This module, its XML
// reader and category conventions MUST execute from the trusted base checkout.
import { execFileSync } from 'node:child_process';
import { appendFile, writeFile } from 'node:fs/promises';
import { posix } from 'node:path';
import { pathToFileURL } from 'node:url';
import { BROWSER_TESTS, planWork, validSource } from './ci-plan.mjs';
import { validateSource } from './test-categories.mjs';

const git = args => execFileSync('git', args, { encoding: 'utf8', maxBuffer: 32 * 1024 * 1024 });
const sha = value => { if (!/^[a-f0-9]{40}$/.test(value ?? '')) throw new Error('Immutable Git SHA required'); return value; };

export function parseDiff(raw) {
  if (raw && !raw.endsWith('\0')) throw new Error('Truncated Git diff');
  const tokens = raw ? raw.slice(0, -1).split('\0') : [];
  const changes = [];
  for (let index = 0; index < tokens.length;) {
    const status = tokens[index++];
    const first = tokens[index++];
    if (!first) throw new Error('Incomplete Git diff record');
    if (/^[RC]\d+$/.test(status)) {
      const path = tokens[index++];
      if (!path) throw new Error('Incomplete renamed/copied file');
      changes.push({ status, path, previousPath: first });
    } else if (/^[AMDT]$/.test(status)) changes.push({ status, path: first });
    else throw new Error(`Unsupported Git file status: ${status}`);
  }
  return changes;
}

export function classifyTests(source, path, project) {
  const result = validateSource(source, path);
  if (result.errors.length) throw new Error(result.errors.join('\n'));
  // The adopted convention uses literal attributes, no conditional test source
  // or user-defined test-class inheritance. Ambiguity must remain visible.
  const unsupported = /^\s*#(if|elif|else)/m.test(source)
    || /\b(?:DataTestMethod|TestMethod|TestClass|TestCategory)Attribute\b/.test(source)
    || [...source.matchAll(/\busing\s+(\w+)\s*=/g)].some(match => new RegExp(`\\[[^\\]]*\\b${match[1]}\\b`).test(source))
    || result.testClasses.some(type => type.partial || type.bases.some(base => !['BunitContext', 'PageTest', 'IDisposable', 'IAsyncDisposable'].includes(base)));
  const lanes = new Set();
  for (const declaration of result.declarations) {
    const categories = new Set(declaration.categories);
    if (categories.has('Live')) continue;
    if (project === BROWSER_TESTS && categories.has('Integration')) throw new Error(`${path}: Playwright integration requires explicit combined fixture ownership`);
    if (categories.has('HomeAssistantIntegration')) lanes.add('home-assistant');
    else if (categories.has('SqlServerIntegration')) lanes.add('sql-server');
    else if (categories.has('Browser') || project === BROWSER_TESTS) lanes.add('browser');
    else if (categories.has('Integration')) lanes.add('simulator');
    else lanes.add('fast');
  }
  return { lanes: [...lanes].sort(), methods: result.methods, unsupported };
}

export function readGraph(commit) {
  sha(commit);
  const paths = git(['ls-tree', '-r', '--name-only', '-z', commit]).split('\0').filter(Boolean);
  const files = {};
  for (const path of paths.filter(path => path === 'HVO.WebSite.sln' || path.endsWith('.csproj'))) {
    files[path] = git(['show', `${commit}:${path}`]);
  }
  const graph = JSON.parse(execFileSync('python3', [new URL('./ci-projects.py', import.meta.url).pathname], {
    input: JSON.stringify(files), encoding: 'utf8', maxBuffer: 8 * 1024 * 1024
  }));
  for (const project of graph.projects.filter(project => project.test)) {
    const lanes = new Set();
    let methods = 0;
    for (const path of paths.filter(path => path.startsWith(`${posix.dirname(project.path)}/`) && path.endsWith('.cs'))) {
      const result = classifyTests(git(['show', `${commit}:${path}`]), path, project.path);
      methods += result.methods;
      for (const lane of result.lanes) lanes.add(lane);
      // Full builds cannot repair an unknown category partition. Stop visibly
      // until its test convention/ownership is supported rather than running
      // an apparently complete plan with undiscovered fixture requirements.
      if (result.unsupported) throw new Error(`${path}: unsupported test metadata syntax`);
    }
    if (!methods) throw new Error(`No test declarations discovered for ${project.path}; explicit ownership is required`);
    project.lanes = [...lanes].sort();
    project.testMethods = methods;
  }
  return graph;
}

export function derivePlan(source, { trigger = 'pull_request_target', full = false, benchmark } = {}) {
  validSource(source);
  if (trigger === 'pull_request_target') {
    const parents = git(['show', '-s', '--format=%P', source.merge]).trim().split(' ');
    if (parents.length !== 2 || parents[0] !== source.base || parents[1] !== source.head) throw new Error('Git merge does not bind the admitted head/base');
  }
  const baseGraph = readGraph(source.base), candidateGraph = readGraph(source.merge);
  let changes = parseDiff(git(['diff', '--name-status', '-z', '--find-renames', source.base, source.merge, '--']));
  if (benchmark) {
    if (trigger !== 'benchmark' || source.head !== source.base || source.head !== source.merge) throw new Error('Benchmarks require one trusted main snapshot');
    const paths = { jkbms: 'src/HVO.Hardware.JkBms/Program.cs', website: 'src/HVO.WebSite.v9/Program.cs', shared: 'src/HVO.Edge.Contracts/Contract.cs' };
    if (!paths[benchmark]) throw new Error('Unknown benchmark profile');
    changes = [{ status: 'M', path: paths[benchmark] }];
  }
  return planWork({ source, baseGraph, candidateGraph, changes, complete: true, trigger, full });
}

async function main() {
  if (process.env.GITHUB_EVENT_NAME === 'workflow_dispatch' && process.env.GITHUB_REF !== 'refs/heads/main') throw new Error('Manual CI profiles run only on trusted main');
  const source = { head: process.env.CI_HEAD, base: process.env.CI_BASE, merge: process.env.CI_MERGE };
  const plan = derivePlan(source, { trigger: process.env.CI_TRIGGER, full: process.env.CI_FULL === 'true', benchmark: process.env.CI_BENCHMARK || undefined });
  await writeFile(process.env.CI_PLAN_PATH || 'ci-plan.json', `${JSON.stringify(plan, null, 2)}\n`);
  if (process.env.GITHUB_OUTPUT) await appendFile(process.env.GITHUB_OUTPUT, `plan=${JSON.stringify(plan)}\n`);
  if (process.env.GITHUB_STEP_SUMMARY) await appendFile(process.env.GITHUB_STEP_SUMMARY, `## Affected-work plan\n\nMode: ${plan.mode}; digest: ${plan.digest}\n\n\`\`\`json\n${JSON.stringify(plan, null, 2)}\n\`\`\`\n`);
  console.log(JSON.stringify(plan));
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) main().catch(error => { console.error(error.message); process.exitCode = 1; });
