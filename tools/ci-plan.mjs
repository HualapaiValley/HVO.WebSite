// Pure affected-work selection. Inputs are data read by trusted base tooling.
import { createHash } from 'node:crypto';
import { posix } from 'node:path';

export const PLAN_VERSION = 1;
export const LANES = ['fast', 'simulator', 'home-assistant', 'sql-server', 'browser'];
const project = (area, name) => `${area}/${name}/${name}.csproj`;
export const WEBSITE_TESTS = project('tests', 'HVO.WebSite.UnitTests');
export const BROWSER_TESTS = project('tests', 'HVO.WebSite.PlaywrightTests');
export const HA_TESTS = ['HVO.Edge.HomeAssistant.Mqtt.Tests', 'HVO.Edge.Exporter.HomeAssistant.Tests', 'HVO.Tools.HomeAssistantEntityMigration.Tests'].map(name => project('tests', name));
export const IMAGES = Object.freeze({
  website: project('src', 'HVO.WebSite.v9'),
  davis: project('src', 'HVO.Hardware.DavisVantagePro2'),
  jkbms: project('src', 'HVO.Hardware.JkBms'),
  smartshunt: project('src', 'HVO.Hardware.VictronSmartShunt'),
  eg4: project('src', 'HVO.Hardware.Eg4'),
  'ha-exporter': project('src', 'HVO.Edge.Exporter.HomeAssistant')
});
const browserTargets = ['HVO.WebSite.v9', 'HVO.WebSite.Themes', 'HVO.ThemeSandbox'].map(name => project('src', name));
const sorted = items => [...new Set(items)].sort();
const globalInput = path => /(^|\/)(global\.json|Directory\.[^/]+\.(props|targets)|NuGet\.[Cc]onfig|nuget\.config|[^/]+\.runsettings|[^/]+\.sln[x]?)$/.test(path)
  || /^(\.github\/workflows\/|tools\/(ci-|pr-process|test-categories|validate-test-categories))/.test(path)
  || ['.dockerignore', '.gitmodules', '.gitattributes'].includes(path);
const documentation = path => /^docs\/.+\.md$/.test(path)
  || ['README.md', 'CHANGELOG.md', 'LICENSE', '.github/PULL_REQUEST_TEMPLATE.md', '.github/copilot-instructions.md'].includes(path)
  || /^\.github\/ISSUE_TEMPLATE\/[^/]+\.md$/.test(path);

export function validSource(source) {
  if (!source || !['head', 'base', 'merge'].every(key => /^[a-f0-9]{40}$/.test(source[key] ?? ''))) throw new Error('A complete immutable head/base/merge tuple is required');
}

export function matches(pattern, path) {
  // MSBuild's literal repo-relative includes use * and **. Unsupported item
  // evaluation has already requested full validation in the metadata reader.
  const expression = pattern.split(/(\*\*\/|\*\*|\*|\?)/).map(part => {
    if (part === '**/') return '(?:.*/)?';
    if (part === '**') return '.*';
    if (part === '*') return '[^/]*';
    if (part === '?') return '[^/]';
    return part.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  }).join('');
  return new RegExp(`^${expression}$`).test(path);
}

function closure(seeds, edges) {
  const found = new Set(seeds), pending = [...seeds];
  while (pending.length) for (const next of edges.get(pending.pop()) || []) {
    if (!found.has(next)) { found.add(next); pending.push(next); }
  }
  return found;
}

export function planWork({ source, baseGraph, candidateGraph, changes, complete, full = false, trigger = 'pull_request_target' }) {
  validSource(source);
  if (complete !== true || !Array.isArray(changes)) throw new Error('Changed-file input is missing or incomplete');
  if (!baseGraph?.projects?.length || !candidateGraph?.projects?.length) throw new Error('Both complete active project graphs are required');
  const current = new Map(candidateGraph.projects.map(value => [value.path, value]));
  const allProjects = [...baseGraph.projects, ...candidateGraph.projects];
  const reverse = new Map(), forward = new Map();
  for (const value of allProjects) {
    for (const dependency of value.references) {
      if (!reverse.has(dependency)) reverse.set(dependency, new Set());
      reverse.get(dependency).add(value.path);
    }
  }
  for (const value of candidateGraph.projects) forward.set(value.path, value.references);
  const visited = new Set(), visiting = new Set();
  const visit = path => {
    if (visiting.has(path)) throw new Error(`Cyclic project reference: ${[...visiting, path].join(' -> ')}`);
    if (visited.has(path)) return;
    visiting.add(path);
    for (const dependency of forward.get(path) || []) visit(dependency);
    visiting.delete(path);
    visited.add(path);
  };
  for (const path of current.keys()) visit(path);
  const reasons = [], seeds = new Set();
  let operations = false;
  const requireFull = reason => { full = true; reasons.push(reason); };
  if (trigger !== 'pull_request_target' && trigger !== 'benchmark') requireFull(`Full validation for ${trigger}`);
  if (full) reasons.push('Explicit full validation requested');
  const unsupported = [...(baseGraph.unsupported || []), ...(candidateGraph.unsupported || [])];
  for (const reason of unsupported) requireFull(reason);
  const inputs = [];
  for (const change of changes) {
    if (!/^(A|M|D|T|R\d*|C\d*)$/.test(change.status || '') || !change.path || (change.status.startsWith('R') && !change.previousPath)) throw new Error('Malformed changed-file record');
    for (const path of sorted([change.path, change.previousPath].filter(Boolean))) {
      if (path.startsWith('/') || path.split('/').includes('..') || /[\u0000-\u001f]/.test(path)) throw new Error('Unsafe changed-file path');
      inputs.push(path);
      if (globalInput(path)) { requireFull(`Global input: ${path}`); continue; }
      const owners = new Set();
      for (const value of allProjects) {
        if ((value.inputs || []).some(pattern => matches(pattern, path))) owners.add(value.path);
      }
      if (path.startsWith('src/HVO.Database/')) owners.add(WEBSITE_TESTS);
      if (path.startsWith('deploy/home-assistant/') || path.startsWith('tests/HomeAssistant.IntegrationEnvironment/')) {
        for (const owner of [...HA_TESTS, WEBSITE_TESTS, BROWSER_TESTS]) owners.add(owner);
      }
      // Only known prose can bypass project ownership, and copied/read inputs
      // above always take precedence over a documentation-looking extension.
      const localReadme = allProjects.some(value => path === `${posix.dirname(value.path)}/README.md` || path === `${posix.dirname(value.path)}/CHANGELOG.md`);
      if (!owners.size && (documentation(path) || localReadme)) { reasons.push(`Non-executable documentation: ${path}`); continue; }
      for (const value of allProjects) if (path.startsWith(`${posix.dirname(value.path)}/`)) owners.add(value.path);
      if (/^(deploy\/(hvo-docker|pi-gateways)\/|tools\/(verify-local-log-budget|verify-log-outage-recovery)|scripts\/(publish-image|deploy-))/.test(path)) {
        operations = true;
        // Deployment inputs can affect image construction and configuration
        // outside ProjectReference; keep this boundary deliberately broad.
        requireFull(`Deployment/operational input: ${path}`);
      }
      if (!owners.size) requireFull(`Unclassified input: ${path}`);
      else for (const owner of owners) { seeds.add(owner); reasons.push(`${path} is owned by ${owner}`); }
      if (path.endsWith('.csproj') && !allProjects.some(value => value.path === path)) requireFull(`New/unlisted project: ${path}`);
    }
  }
  if (!changes.length && trigger === 'pull_request_target') requireFull('No changed inputs; refusing an unexplained empty PR plan');
  let affected = closure(seeds, reverse);
  if ([...affected].some(path => browserTargets.includes(path))) affected.add(BROWSER_TESTS);
  affected = closure(affected, reverse);
  if (full) affected = new Set(current.keys());
  const affectedProjects = sorted([...affected].filter(path => current.has(path)));
  const tests = affectedProjects.filter(path => current.get(path).test);
  const lanes = Object.fromEntries(LANES.map(lane => [lane, []]));
  for (const path of tests) {
    const kinds = current.get(path).lanes;
    if (!Array.isArray(kinds) || !kinds.length || kinds.some(lane => !LANES.includes(lane))) throw new Error(`Missing/unsupported test lane ownership: ${path}`);
    for (const lane of kinds) lanes[lane].push(path);
  }
  const buildDependencies = sorted(closure(affectedProjects, forward));
  // A conditional reference may not exist in the actual build. Its conservative
  // graph edge cannot prove another root will build that project; build every
  // active project when metadata requires fallback.
  const roots = candidates => unsupported.length ? candidates : candidates.filter(path => !candidates.some(other => other !== path && closure([other], forward).has(path)));
  const debugRoots = roots(affectedProjects);
  const releaseProjects = affectedProjects.filter(path => !current.get(path).test);
  const releaseRoots = roots(releaseProjects);
  for (const [selected, required] of [[debugRoots, affectedProjects], [releaseRoots, releaseProjects]]) {
    const covered = closure(selected, forward);
    if (required.some(path => !covered.has(path))) throw new Error('Build roots omit affected projects');
  }
  const plan = {
    version: PLAN_VERSION, source: { ...source }, trigger, mode: full ? 'full' : 'selected',
    changes: changes.map(value => ({ ...value })).sort((a, b) => a.path.localeCompare(b.path)), inputs: sorted(inputs),
    affectedProjects, buildDependencies, debugRoots, releaseRoots,
    tests, lanes,
    images: Object.entries(IMAGES).filter(([, path]) => affected.has(path)).map(([name]) => name).sort(),
    operations: full || operations, reasons: sorted(reasons), emptyReasons: {}
  };
  for (const [lane, projects] of Object.entries(lanes)) if (!projects.length) plan.emptyReasons[lane] = `No affected test assembly owns the ${lane} lane`;
  if (!plan.images.length) plan.emptyReasons.images = 'No affected application image in the dependency/input closure';
  if (!plan.debugRoots.length) plan.emptyReasons.build = 'All changed inputs are explicitly known non-executable documentation';
  if (!plan.operations) plan.emptyReasons.operations = 'No affected operational input; inexpensive policies still run';
  return { ...plan, digest: createHash('sha256').update(JSON.stringify(plan)).digest('hex') };
}

export function verifyPlan(plan, source) {
  validSource(source);
  if (plan?.version !== PLAN_VERSION || ['head', 'base', 'merge'].some(key => plan.source?.[key] !== source[key])) throw new Error('Stale or unsupported CI plan');
  const { digest, ...content } = plan;
  if (createHash('sha256').update(JSON.stringify(content)).digest('hex') !== digest) throw new Error('CI plan content/digest mismatch');
  return plan;
}

export function aggregate(plan, source, results) {
  verifyPlan(plan, source);
  const required = {
    validation: true,
    'home-assistant': plan.lanes['home-assistant'].length > 0,
    'sql-server': plan.lanes['sql-server'].length > 0,
    browser: plan.lanes.browser.length > 0,
    operations: plan.operations
  };
  for (const [job, needed] of Object.entries(required)) {
    const expected = needed ? 'success' : 'skipped';
    if (results[job] !== expected) throw new Error(`Planned job ${job}: expected ${expected}, received ${results[job] ?? 'missing'}`);
    if (!needed && !plan.emptyReasons[job]) throw new Error(`Empty ${job} lane lacks a verified reason`);
  }
  return true;
}
