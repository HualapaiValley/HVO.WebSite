import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';
import { validateSource } from './test-categories.mjs';

const source = (classAttributes = '', methodAttributes = '') => `[TestClass] ${classAttributes}
public class Fixture {
  [TestMethod] ${methodAttributes} public void Runs() { }
}`;

test('folder and root integration conventions accept class or method categories', () => {
  for (const file of ['Example/Integration/Fixture.cs', 'Example/FixtureIntegrationTests.cs']) {
    assert.deepEqual(validateSource(source('[TestCategory("Integration")]'), file).errors, []);
    assert.deepEqual(validateSource(source('', '[TestCategory("Integration")]'), file).errors, []);
    assert.match(validateSource(source(), file).errors[0], /requires TestCategory\("Integration"\)/);
  }
});
test('every method must inherit or declare its own category, including expression bodies', () => {
  const result = validateSource('[TestClass] public class F { [TestMethod, TestCategory("Live")] public void A() {} [TestMethod] public void B() => Run(); }', 'Live/F.cs');
  assert.equal(result.methods, 2); assert.equal(result.errors.length, 1); assert.match(result.errors[0], /B: requires/);
});
test('class categories cannot leak into another class', () => {
  const result = validateSource(source('[TestCategory("Live")]') + source(), 'Live/F.cs');
  assert.equal(result.errors.length, 1);
});
test('comments, strings and helper files cannot provide fake categories', () => {
  assert.equal(validateSource('/* [TestClass][TestCategory("Live")] */ class Helper { string Text = "[TestMethod]"; }', 'Live/Helper.cs').methods, 0);
  assert.equal(validateSource(source('// [TestCategory("Integration")]\n'), 'FIntegrationTests.cs').errors.length, 1);
  assert.equal(validateSource(source('', '/* [TestCategory("Integration")] */'), 'FIntegrationTests.cs').errors.length, 1);
});
test('HA and SQL fixture lanes require Integration and cannot hide under Live', () => {
  for (const lane of ['HomeAssistantIntegration', 'SqlServerIntegration']) {
    assert.match(validateSource(source(`[TestCategory("${lane}")]`), 'F.cs').errors[0], /requires Integration/);
    assert.deepEqual(validateSource(source(`[TestCategory("${lane}"), TestCategory("Integration")]`), 'F.cs').errors, []);
    assert.match(validateSource(source(`[TestCategory("${lane}"), TestCategory("Integration"), TestCategory("Live")]`), 'F.cs').errors.at(-1), /Live cannot/);
  }
});
test('typos and dynamic categories fail, Browser stays a non-live category', () => {
  assert.match(validateSource(source('[TestCategory("Integraton")]'), 'F.cs').errors[0], /unsupported/);
  assert.match(validateSource(source('[TestCategory(Category)]'), 'F.cs').errors[0], /literal/);
  assert.deepEqual(validateSource(source('[TestCategory("Browser")]'), 'F.cs').errors, []);
  assert.match(validateSource(source('[TestCategory("Browser"), TestCategory("Live")]'), 'F.cs').errors[0], /Live cannot/);
});
test('CLI ignores build outputs and fails visibly when no tests are checked', async () => {
  const root = await mkdtemp(join(tmpdir(), 'hvo-category-fixture-'));
  try {
    await mkdir(join(root, 'obj'));
    await writeFile(join(root, 'obj', 'GeneratedIntegrationTests.cs'), source());
    const command = new URL('./test-categories.mjs', import.meta.url);
    const empty = spawnSync(process.execPath, [command.pathname, root], { encoding: 'utf8' });
    assert.equal(empty.status, 1); assert.match(empty.stderr, /No MSTest methods found/);
    await writeFile(join(root, 'Fixture.cs'), source());
    const valid = spawnSync(process.execPath, [command.pathname, root], { encoding: 'utf8' });
    assert.equal(valid.status, 0); assert.match(valid.stdout, /1 MSTest methods/);
  } finally { await rm(root, { recursive: true, force: true }); }
});
