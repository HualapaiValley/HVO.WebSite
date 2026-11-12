import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { requireUnusedVersion, verifyManifest, versionFor } from './davis-image.mjs';

test('publishing stays outside PR CI with source admission, pinned actions and multi-arch readback', () => {
  const source = readFileSync(new URL('../.github/workflows/publish-davis.yml', import.meta.url), 'utf8');
  const workflow = JSON.parse(execFileSync('ruby', ['-ryaml', '-rjson', '-e', 'puts JSON.generate(YAML.load(STDIN.read))'], { input: source, encoding: 'utf8' }));
  assert.deepEqual(Object.keys(workflow.true).sort(), ['push', 'workflow_dispatch']);
  assert.deepEqual(workflow.true.push.tags, ['davis-v*']);
  assert.deepEqual(workflow.permissions, { contents: 'read' });
  assert.deepEqual(workflow.jobs.publish['runs-on'], ['self-hosted', 'Linux', 'X64', 'hvo-website', 'docker']);
  const steps = workflow.jobs.publish.steps;
  const build = steps.find(step => step.id === 'build');
  assert.equal(build.with.platforms, 'linux/amd64,linux/arm64');
  assert.equal(build.with.push, true);
  assert.equal(build.with.tags, '${{ env.IMAGE }}:${{ steps.release.outputs.version }}');
  for (const step of steps.filter(step => step.uses)) assert.match(step.uses, /@[a-f0-9]{40}$/);
  const admission = steps.findIndex(step => step.id === 'release');
  assert.match(steps[admission].run, /git merge-base --is-ancestor "\$GITHUB_SHA" origin\/main/);
  assert.match(steps[admission].run, /"\$GITHUB_EVENT_NAME" != push \|\| "\$GITHUB_REF_NAME" != davis-v\*-\*/);
  assert.match(steps[admission].run, /\*hvo-docker\*\|\*home-docker\*/);
  assert.ok(admission < steps.findIndex(step => step.uses?.startsWith('docker/login-action')));
  assert.match(source, /node tools\/davis-image\.mjs manifest/);
  assert.equal(steps.at(-1).if, 'always()');
});

test('release admission accepts scoped semver tags and main dispatches only', () => {
  for (const version of ['1.0.0', '0.1.0-rc.1', '10.20.30-dev.0']) {
    assert.equal(versionFor({ GITHUB_EVENT_NAME: 'push', GITHUB_REF: `refs/tags/davis-v${version}` }), version);
    assert.equal(versionFor({ GITHUB_EVENT_NAME: 'workflow_dispatch', GITHUB_REF: 'refs/heads/main', REQUESTED_VERSION: version }), version);
  }
  for (const version of ['latest', '01.0.0', '1.0', '1.0.0-01', '1.0.0+x', '1.0.0\nevil', '1.0.0\n', '$(false)', '1.0.0-' + 'a'.repeat(128)]) {
    assert.throws(() => versionFor({ GITHUB_EVENT_NAME: 'push', GITHUB_REF: `refs/tags/davis-v${version}` }));
  }
  for (const [event, ref] of [['pull_request', 'refs/pull/1/merge'], ['push', 'refs/heads/main'], ['push', 'refs/tags/v1.0.0'], ['workflow_dispatch', 'refs/heads/feature']]) {
    assert.throws(() => versionFor({ GITHUB_EVENT_NAME: event, GITHUB_REF: ref, REQUESTED_VERSION: '1.0.0' }));
  }
});

test('version availability fails closed on duplicates, credentials, transport and registry failures', async () => {
  const calls = [];
  await requireUnusedVersion('1.0.0', 'test', 'fixture-password', async (url, options) => {
    calls.push(url); assert.equal(options.method, 'HEAD'); assert.equal(options.redirect, 'error');
    return { status: 404 };
  });
  assert.equal(calls[0], 'https://registry.hualapaivalleyobservatory.org/v2/hvo-davis/manifests/1.0.0');
  for (const status of [200, 301, 401, 403, 429, 500]) {
    await assert.rejects(requireUnusedVersion('1.0.0', 'test', 'fixture-password', async () => ({ status })));
  }
  await assert.rejects(requireUnusedVersion('1.0.0', '', '', () => assert.fail('Must not call registry')));
  await assert.rejects(requireUnusedVersion('../other', 'test', 'fixture-password', () => assert.fail('Must not call registry')));
  await assert.rejects(requireUnusedVersion('1.0.0', 'test', 'fixture-password', () => { throw new Error('fixture-password'); }), error => !error.message.includes('fixture-password'));
});

const digest = `sha256:${'a'.repeat(64)}`;
const index = () => ({ schemaVersion: 2, mediaType: 'application/vnd.oci.image.index.v1+json', manifests: ['amd64', 'arm64'].map(architecture => ({ digest, platform: { os: 'linux', architecture } })) });
test('registry readback requires exactly both Linux architectures and valid digests', () => {
  verifyManifest(index(), digest);
  for (const mutate of [value => value.manifests.pop(), value => value.manifests.push(value.manifests[0]), value => value.manifests[1].platform.architecture = 'amd64', value => value.manifests[1].platform.os = 'windows', value => value.manifests[1].digest = 'bad', value => value.mediaType = 'application/vnd.oci.image.manifest.v1+json']) {
    const value = index(); mutate(value); assert.throws(() => verifyManifest(value, digest));
  }
  assert.throws(() => verifyManifest(index(), ''));
});
