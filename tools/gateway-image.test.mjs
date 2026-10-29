import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { execFileSync, spawnSync } from 'node:child_process';
import { COLLECTORS, imageFor, requireUnusedVersion, versionFor } from './gateway-image.mjs';

const projects = { jkbms: 'JkBms', smartshunt: 'VictronSmartShunt', eg4: 'Eg4' };
const digest = `sha256:${'a'.repeat(64)}`;
const read = path => readFileSync(new URL(`../${path}`, import.meta.url), 'utf8');
const yaml = source => JSON.parse(execFileSync('ruby', ['-ryaml', '-rjson', '-e', 'puts JSON.generate(YAML.load(STDIN.read))'], { input: source, encoding: 'utf8' }));

for (const collector of COLLECTORS) {
  test(`${collector}: matches Davis runner, source admission, actions, publish and readback`, () => {
    const source = read(`.github/workflows/publish-${collector}.yml`);
    const workflow = yaml(source);
    assert.deepEqual(Object.keys(workflow.true).sort(), ['push', 'workflow_dispatch']);
    assert.deepEqual(workflow.true.push.tags, [`${collector}-v*`]);
    assert.deepEqual(workflow.permissions, { contents: 'read' });
    assert.deepEqual(workflow.jobs.publish['runs-on'], ['self-hosted', 'Linux', 'X64', 'hvo-website', 'docker']);
    assert.equal(workflow.concurrency.group, `publish-${collector}`);
    assert.equal(workflow.concurrency['cancel-in-progress'], false);
    assert.equal(workflow.jobs.publish.env.IMAGE, imageFor(collector));
    const steps = workflow.jobs.publish.steps;
    const build = steps.find(step => step.id === 'build');
    assert.equal(build.with.file, `src/HVO.Hardware.${projects[collector]}/Dockerfile`);
    assert.equal(build.with.platforms, 'linux/amd64,linux/arm64');
    assert.equal(build.with.tags, '${{ env.IMAGE }}:${{ steps.release.outputs.version }}');
    assert.equal(build.with.push, true);
    assert.equal(build.with.provenance, false);
    assert.equal(build.with.sbom, false);
    for (const step of steps.filter(step => step.uses)) assert.match(step.uses, /@[a-f0-9]{40}$/);
    const admission = steps.findIndex(step => step.id === 'release');
    assert.match(steps[admission].run, /test "\$\(uname -m\)" = x86_64/);
    assert.match(steps[admission].run, /git merge-base --is-ancestor "\$GITHUB_SHA" origin\/main/);
    assert.ok(steps[admission].run.includes(`"$GITHUB_EVENT_NAME" != push || "$GITHUB_REF_NAME" != ${collector}-v*-*`));
    assert.match(steps[admission].run, /\*hvo-docker\*\|\*home-docker\*/);
    assert.ok(admission < steps.findIndex(step => step.uses?.startsWith('docker/login-action')));
    assert.equal(steps.find(step => step.uses?.startsWith('docker/setup-qemu-action')).with.platforms, 'arm64');
    assert.ok(steps.find(step => step.uses?.startsWith('docker/setup-buildx-action')));
    assert.match(steps.at(-2).run, /imagetools inspect "\$IMAGE@\$DIGEST" --raw/);
    assert.match(steps.at(-2).run, /test "\$\(docker buildx imagetools inspect "\$IMAGE:\$VERSION" --format '\{\{.Manifest.Digest\}\}'\)" = "\$DIGEST"/);
    assert.ok(steps.at(-2).run.includes(`node tools/gateway-image.mjs ${collector} manifest`));
    assert.equal(steps.at(-1).if, 'always()');
    assert.match(steps.at(-1).run, /rm -rf -- "\$RUNNER_TEMP\//);
  });

  test(`${collector}: admits only its own semver tag or main dispatch`, () => {
    for (const version of ['1.0.0', '0.0.0-ci.466.1', '10.20.30-rc.1']) {
      assert.equal(versionFor(collector, { GITHUB_EVENT_NAME: 'push', GITHUB_REF: `refs/tags/${collector}-v${version}` }), version);
      assert.equal(versionFor(collector, { GITHUB_EVENT_NAME: 'workflow_dispatch', GITHUB_REF: 'refs/heads/main', REQUESTED_VERSION: version }), version);
    }
    for (const version of ['latest', '01.0.0', '1.0', '1.0.0-01', '1.0.0+x', '1.0.0\nevil', '1.0.0\n', '$(false)', '1.0.0-' + 'a'.repeat(128)]) {
      assert.throws(() => versionFor(collector, { GITHUB_EVENT_NAME: 'push', GITHUB_REF: `refs/tags/${collector}-v${version}` }));
    }
    for (const [event, ref] of [['pull_request', 'refs/pull/1/merge'], ['push', 'refs/heads/main'], ['push', 'refs/tags/davis-v1.0.0'], ['workflow_dispatch', 'refs/heads/feature']]) {
      assert.throws(() => versionFor(collector, { GITHUB_EVENT_NAME: event, GITHUB_REF: ref, REQUESTED_VERSION: '1.0.0' }));
    }
    for (const other of COLLECTORS.filter(name => name !== collector)) {
      assert.throws(() => versionFor(collector, { GITHUB_EVENT_NAME: 'push', GITHUB_REF: `refs/tags/${other}-v1.0.0` }));
    }
    const cli = spawnSync(process.execPath, ['tools/gateway-image.mjs', collector, 'version'], {
      env: { GITHUB_EVENT_NAME: 'push', GITHUB_REF: `refs/tags/${collector}-v0.0.0-ci.466.1` }, encoding: 'utf8'
    });
    assert.equal(cli.status, 0, cli.stderr);
    assert.equal(cli.stdout, 'version=0.0.0-ci.466.1\n');
  });

  test(`${collector}: refuses overwrite, missing credentials, redirects and registry errors`, async () => {
    await requireUnusedVersion(collector, '1.0.0', 'test', 'fixture-password', async (url, options) => {
      assert.equal(url, `https://registry.hualapaivalleyobservatory.org/v2/hvo-${collector}/manifests/1.0.0`);
      assert.equal(options.method, 'HEAD');
      assert.equal(options.redirect, 'error');
      assert.ok(options.signal instanceof AbortSignal);
      return { status: 404 };
    });
    for (const status of [200, 301, 401, 403, 429, 500]) {
      await assert.rejects(requireUnusedVersion(collector, '1.0.0', 'test', 'fixture-password', async () => ({ status })));
    }
    await assert.rejects(requireUnusedVersion(collector, '1.0.0', '', '', () => assert.fail('Must not call registry')));
    await assert.rejects(requireUnusedVersion(collector, '../other', 'test', 'fixture-password', () => assert.fail('Must not call registry')));
    await assert.rejects(requireUnusedVersion(collector, '1.0.0', 'test', 'fixture-password', () => { throw new Error('fixture-password'); }), error => !error.message.includes('fixture-password'));
  });

  test(`${collector}: Compose requires both immutable pins and retains its runtime contract`, () => {
    const prefix = collector.toUpperCase();
    const env = {
      PATH: process.env.PATH, HOME: process.env.HOME,
      [`HVO_${prefix}_IMAGE_VERSION`]: '0.0.0-ci.466.1', [`HVO_${prefix}_IMAGE_DIGEST`]: digest,
      [`${prefix}_REMOTE_CONFIG_FILE`]: `/tmp/hvo-fixture/${collector}/gateway.json`,
      [`${prefix}_REMOTE_SECRETS_DIRECTORY`]: `/tmp/hvo-fixture/${collector}/secrets`
    };
    const args = ['compose', '--env-file', '/dev/null', '-f', `deploy/pi-gateways/${collector}/docker-compose.yml`, 'config', '--format', 'json'];
    const service = JSON.parse(execFileSync('docker', args, { env, encoding: 'utf8' })).services[`hvo-${collector}`];
    assert.equal(service.image, `${imageFor(collector)}:0.0.0-ci.466.1@${digest}`);
    assert.equal(service.build, undefined);
    assert.equal(service.platform, undefined);
    assert.equal(service.restart, 'unless-stopped');
    assert.ok(service.volumes.some(volume => volume.source === `${collector}-outbox` && volume.target === '/app/data'));
    for (const target of ['/app/config/gateway.json', '/run/secrets']) {
      assert.ok(service.volumes.some(volume => volume.target === target && volume.read_only));
    }
    assert.equal(service.healthcheck.test.at(-1), `http://localhost:8080/health${collector === 'eg4' ? '' : '/live'}`);
    if (collector !== 'eg4') {
      assert.equal(service.privileged, true);
      assert.ok(service.volumes.some(volume => volume.target === '/run/dbus/system_bus_socket'));
    } else {
      assert.equal(service.privileged, undefined);
      assert.equal(service.devices[0].target, '/dev/hvo/eg4-6500ex-a');
    }
    for (const key of [`HVO_${prefix}_IMAGE_VERSION`, `HVO_${prefix}_IMAGE_DIGEST`]) {
      const missing = { ...env }; delete missing[key];
      const result = spawnSync('docker', args, { env: missing, encoding: 'utf8' });
      assert.notEqual(result.status, 0);
      assert.ok(result.stderr.includes(key));
    }
  });
}

test('unknown collector names cannot select a tag scope or registry repository', async () => {
  for (const collector of ['davis', 'website', '../other', '', undefined]) {
    assert.throws(() => imageFor(collector));
    assert.throws(() => versionFor(collector, {}));
    await assert.rejects(requireUnusedVersion(collector, '1.0.0', 'test', 'fixture-password', () => assert.fail('Must not call registry')));
  }
});

test('JK deployment defaults to no-build without touching a daemon or device', () => {
  const directory = mkdtempSync(join(tmpdir(), 'hvo-jkbms-publish-'));
  try {
    const config = JSON.parse(read('deploy/pi-gateways/jkbms/gateway.json.example'));
    config.JkBms.Devices = [{ Address: 'AA:BB:CC:DD:EE:FF', DeviceId: 'fixture', Alias: 'fixture', Enabled: true }];
    config.HomeAssistant.Mqtt.Enabled = false;
    writeFileSync(join(directory, 'gateway.json'), JSON.stringify(config));
    mkdirSync(join(directory, 'secrets'));
    for (const name of ['diagnostics-api-key', 'central-ingest-api-key']) writeFileSync(join(directory, 'secrets', name), 'fixture-secret');
    const envFile = join(directory, 'fixture.env');
    writeFileSync(envFile, [
      'HVO_JKBMS_IMAGE_VERSION=0.0.0-ci.466.1', `HVO_JKBMS_IMAGE_DIGEST=${digest}`,
      `JKBMS_CONFIG_FILE=${join(directory, 'gateway.json')}`, `JKBMS_SECRETS_DIRECTORY=${join(directory, 'secrets')}`,
      'JKBMS_REMOTE_CONFIG_FILE=/tmp/hvo-fixture/jkbms/gateway.json', 'JKBMS_REMOTE_SECRETS_DIRECTORY=/tmp/hvo-fixture/jkbms/secrets'
    ].join('\n') + '\n');
    const output = execFileSync('bash', ['scripts/deploy-pi-gateway.sh', '--dry-run', '--context', 'fixture-no-daemon', 'jkbms'], {
      env: { PATH: process.env.PATH, HOME: process.env.HOME, HVO_PI_GATEWAY_ENV_FILE: envFile }, encoding: 'utf8'
    });
    assert.match(output, /up -d --wait[^\n]* --no-build/);
    assert.doesNotMatch(output, /--build|fixture-secret/);
  } finally { rmSync(directory, { recursive: true, force: true }); }
});
