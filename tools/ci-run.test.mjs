import test from 'node:test';
import assert from 'node:assert/strict';
import { buildCommands, testFilter } from './ci-run.mjs';
import { BROWSER_TESTS } from './ci-plan.mjs';

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
