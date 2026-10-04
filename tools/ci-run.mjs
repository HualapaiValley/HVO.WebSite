// Execution only: this helper never admits a PR or derives its obligations.
import { execFileSync } from 'node:child_process';
import { mkdirSync, rmSync } from 'node:fs';
import { basename, dirname, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { aggregate, BROWSER_TESTS, verifyPlan } from './ci-plan.mjs';

const directory = dirname(fileURLToPath(import.meta.url));
const unique = values => [...new Set(values)].sort();
const projectPath = value => {
  if (!/^(src|tests|tools)\/[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+\.csproj$/.test(value)) throw new Error(`Unsupported project argument: ${value}`);
  return value;
};
const run = (command, args, options = {}) => execFileSync(command, args, { stdio: 'inherit', ...options });
export function testFilter(lane, project) {
  const filters = {
    fast: 'TestCategory!=Integration&TestCategory!=Browser&TestCategory!=Live',
    simulator: 'TestCategory=Integration&TestCategory!=HomeAssistantIntegration&TestCategory!=SqlServerIntegration&TestCategory!=Browser&TestCategory!=Live',
    browser: project === BROWSER_TESTS ? 'TestCategory!=Integration&TestCategory!=Live' : 'TestCategory=Browser&TestCategory!=Live'
  };
  if (!filters[lane]) throw new Error(`Unsupported direct test lane: ${lane}`);
  return filters[lane];
}

export function buildCommands(projects, configuration = 'Debug') {
  return unique(projects).flatMap(path => {
    projectPath(path);
    return [
      ['restore', path, '--locked-mode', '--nologo', '-v', 'minimal'],
      ['build', path, '--no-restore', '-c', configuration, '--nologo', '-v', 'minimal', '/p:ContinuousIntegrationBuild=true', '-warnaserror']
    ];
  });
}

function build(projects, configuration) {
  for (const args of buildCommands(projects, configuration)) run('dotnet', args);
}

function testProjects(plan, lane) {
  for (const project of plan.lanes[lane]) {
    projectPath(project);
    const results = resolve('TestResults', lane, basename(project, '.csproj'));
    // Each invocation owns this one directory; stale TRX cannot prove that a
    // new filter actually ran tests. Other projects/lanes retain their reports.
    rmSync(results, { recursive: true, force: true });
    mkdirSync(results, { recursive: true });
    const args = ['test', project, '--no-build', '--no-restore', '-c', 'Debug', '--nologo', '-v', 'minimal',
      '--settings', lane === 'fast' ? 'test.runsettings' : 'integration.runsettings',
      '--filter', testFilter(lane, project), '--logger', 'trx', '--results-directory', results];
    if (process.env.CI_COVERAGE === 'true') args.push('--collect:XPlat Code Coverage');
    run('dotnet', args);
    // Existing ignored scaffold is owned by #408. Moving this assembly to its
    // browser lane must preserve that visible baseline, not permit new skips.
    const baselineIgnore = project === BROWSER_TESTS ? ['--allow-ignored-test', 'PlaywrightSuite_IsConfiguredButDisabledByDefault'] : [];
    run('python3', [resolve(directory, 'ci-results.py'), results, ...baselineIgnore]);
  }
}

function policies() {
  run(process.execPath, ['--test', 'tools/ci-plan.test.mjs', 'tools/ci-run.test.mjs', 'tools/pr-process.test.mjs', 'tools/test-categories.test.mjs']);
  run('python3', ['-m', 'unittest', 'discover', '-s', 'tools', '-p', 'ci_*_test.py']);
  run('python3', ['tools/verify-home-assistant-runner.py']);
  for (const name of ['validate-test-categories', 'validate-observability-policy', 'validate-davis-weather-underground-deployment', 'validate-eg4-deployment', 'validate-smartshunt-deployment', 'verify-alert-rules']) run('bash', [`tools/${name}.sh`]);
}

function main() {
  const source = { head: process.env.CI_HEAD, base: process.env.CI_BASE, merge: process.env.CI_MERGE };
  const plan = verifyPlan(JSON.parse(process.env.CI_PLAN), source);
  const job = process.argv[2];
  if (job === 'aggregate') {
    aggregate(plan, source, JSON.parse(process.env.CI_JOB_RESULTS));
    console.log(`All planned build/test work passed for ${plan.digest}`);
    return;
  }
  // GitHub's immutable checkout and the plan must refer to the same candidate.
  const checkedOut = execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
  if (checkedOut !== source.merge) throw new Error('Execution checkout does not match the admitted plan');
  if (job === 'validation') {
    build(plan.debugRoots);
    testProjects(plan, 'fast');
    testProjects(plan, 'simulator');
    policies();
    build(plan.releaseRoots, 'Release');
  } else if (job === 'home-assistant') {
    if (!plan.lanes[job].length) throw new Error('Unexpected empty HA execution');
    build(plan.lanes[job]);
    run('bash', ['tools/run-home-assistant-integration-tests.sh', '--prebuilt', '--projects', ...plan.lanes[job].map(projectPath), ...(process.env.CI_COVERAGE === 'true' ? ['--coverage'] : [])]);
  } else if (job === 'sql-server') {
    if (!plan.lanes[job].length) throw new Error('Unexpected empty SQL execution');
    // The owned SQL runner provisions one isolated provider for the API suite.
    if (plan.lanes[job].some(path => path !== 'tests/HVO.WebSite.ApiTests/HVO.WebSite.ApiTests.csproj')) throw new Error('SQL runner needs explicit ownership for a new assembly');
    build(plan.lanes[job]);
    run('bash', ['tools/run-sql-server-integration-tests.sh']);
  } else if (job === 'browser') {
    if (!plan.lanes[job].length) throw new Error('Unexpected empty browser execution');
    // Baseline browser fixtures launch apps without ProjectReference. Building
    // both targets is explicit ownership until those references are introduced.
    build([...plan.lanes[job], 'src/HVO.WebSite.v9/HVO.WebSite.v9.csproj', 'src/HVO.ThemeSandbox/HVO.ThemeSandbox.csproj']);
    for (const project of plan.lanes[job]) run('pwsh', [resolve(dirname(projectPath(project)), 'bin/Debug/net10.0/playwright.ps1'), 'install', '--with-deps', 'chromium']);
    testProjects(plan, job);
  } else if (job === 'operations') {
    if (!plan.operations) throw new Error('Unexpected empty operations execution');
    for (const name of ['verify-local-log-budget', 'verify-log-outage-recovery', 'validate-publish-dry-run']) run('bash', [`tools/${name}.sh`]);
  } else if (job === 'docker-smoke') {
    if (!plan.images.length && !plan.emptyReasons.images) throw new Error('Empty image lane lacks a verified reason');
    run('bash', ['tools/verify-docker-build-smoke.sh']);
    run('bash', ['tools/docker-build-smoke.sh', '--images', ...plan.images]);
  } else throw new Error(`Unknown CI execution job: ${job}`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try { main(); } catch (error) { console.error(error.message); process.exitCode = 1; }
}
