import test from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { checkRepository, checkSources } from './sdk-consistency.mjs';

const sdk = '10.0.402';
const digest = `sha256:${'a'.repeat(64)}`;
const otherDigest = `sha256:${'b'.repeat(64)}`;
const sdkImage = version => `mcr.microsoft.com/dotnet/sdk:${version}@${digest}`;
const tool = fileURLToPath(new URL('./sdk-consistency.mjs', import.meta.url));
const repository = fileURLToPath(new URL('..', import.meta.url));

function fixture(version = sdk) {
  return {
    'global.json': JSON.stringify({ sdk: { version, rollForward: 'disable' } }),
    '.devcontainer/devcontainer.json': JSON.stringify({ features: { 'ghcr.io/devcontainers/features/dotnet:2': { version } } }),
    'tools/pr-process.mjs': `export const PINNED_SDK = '${version}';\n`,
    'tools/pr-process-fixture.json': JSON.stringify({ author: { validation: [{ id: 'local:pinned-sdk-build-zero-warnings', sdk: version }] } }),
    'src/App/Dockerfile': `FROM ${sdkImage(version)} AS build\nFROM mcr.microsoft.com/dotnet/aspnet:10.0.13@${digest} AS base\nFROM build AS publish\nFROM base AS final\n`,
    '.devcontainer/Dockerfile': `FROM example.test/base:stable@${digest}\n`,
    'tests/fixture.Dockerfile': `FROM ${sdkImage(version)} AS build\nFROM scratch\nCOPY --from=build /app /app\n`
  };
}

function rejects(files, ...patterns) {
  const errors = checkSources(files).errors.join('\n');
  assert.ok(errors, 'The deliberately inconsistent fixture must fail');
  for (const pattern of patterns) assert.match(errors, pattern);
}

test('aligned SDKs, runtime pins, repeated digests, stage aliases and scratch pass', () => {
  assert.deepEqual(checkSources(fixture()).errors, []);
});

test('a coordinated future SDK update passes without changing the gate', () => {
  assert.deepEqual(checkSources(fixture('11.0.100-preview.1')).errors, []);
});

test('global.json is authoritative when every other SDK reference is left behind', () => {
  const files = fixture();
  files['global.json'] = JSON.stringify({ sdk: { version: '10.0.403' } });
  rejects(files, /src\/App\/Dockerfile:1.*10\.0\.402.*10\.0\.403/, /devcontainer.*10\.0\.403/, /PINNED_SDK.*10\.0\.403/, /pr-process-fixture.*10\.0\.403/);
});

test('a mismatched Docker SDK tag reports the file, line and expected version', () => {
  const files = fixture();
  files['src/App/Dockerfile'] = `# build stage\nFROM ${sdkImage('10.0.400')} AS build\n`;
  rejects(files, /src\/App\/Dockerfile:2.*10\.0\.400.*10\.0\.402/);
});

test('a mismatched devcontainer feature version fails', () => {
  const files = fixture();
  files['.devcontainer/devcontainer.json'] = files['.devcontainer/devcontainer.json'].replace(sdk, '10.0.400');
  rejects(files, /devcontainer.json.*10\.0\.400.*10\.0\.402/);
});

test('a mismatched trusted PINNED_SDK literal fails without importing it', () => {
  const files = fixture();
  files['tools/pr-process.mjs'] = "throw new Error('must never execute');\nexport const PINNED_SDK = '10.0.400';\n";
  rejects(files, /pr-process.mjs.*PINNED_SDK.*10\.0\.400.*10\.0\.402/);
});

test('a mismatched SDK in the PR-process build-evidence fixture fails', () => {
  const files = fixture();
  files['tools/pr-process-fixture.json'] = files['tools/pr-process-fixture.json'].replace(sdk, '10.0.400');
  rejects(files, /pr-process-fixture.json.*10\.0\.400.*10\.0\.402/);
});

test('the same SDK tag cannot have different digests across Dockerfiles', () => {
  const files = fixture();
  files['tests/fixture.Dockerfile'] = files['tests/fixture.Dockerfile'].replace(digest, otherDigest);
  rejects(files, /tests\/fixture.Dockerfile:1.*digest.*src\/App\/Dockerfile:1/);
});

test('digest consistency also covers runtime and non-.NET base images', () => {
  for (const image of ['mcr.microsoft.com/dotnet/aspnet:10.0.13', 'example.test/other-base:stable']) {
    const files = fixture();
    files['Dockerfile.one'] = `FROM ${image}@${digest}\n`;
    files['Dockerfile.two'] = `FROM ${image}@${otherDigest}\n`;
    rejects(files, /Dockerfile.two:1.*digest.*Dockerfile.one:1/);
  }
});

for (const image of [`mcr.microsoft.com/dotnet/sdk:${sdk}`, 'mcr.microsoft.com/dotnet/runtime:10.0.13', 'example.test/base:stable']) {
  test(`external FROM requires a digest: ${image}`, () => {
    const files = fixture();
    files['nested/Dockerfile.extra'] = `FROM ${image} AS base\n`;
    rejects(files, /nested\/Dockerfile.extra:1.*@sha256/);
  });
}

test('malformed or unsupported digests fail with pinning guidance', () => {
  for (const pin of ['sha256:short', `sha256:${'z'.repeat(64)}`, `sha512:${'a'.repeat(128)}`]) {
    const files = fixture();
    files['Dockerfile'] = `FROM example.test/base:stable@${pin}\n`;
    rejects(files, /Dockerfile:1.*@sha256/);
  }
});

test('digest-only SDK images still require the exact SDK tag', () => {
  const files = fixture();
  files['Dockerfile'] = `FROM mcr.microsoft.com/dotnet/sdk@${digest}\n`;
  rejects(files, /Dockerfile:1.*10\.0\.402/);
});

test('JSONC comments and trailing commas preserve strings containing comment markers', () => {
  const files = fixture();
  files['.devcontainer/devcontainer.json'] = `{
    // "version": "wrong"
    "features": {
      "ghcr.io/devcontainers/features/dotnet:3": { /* comment */ "version": "${sdk}", },
    },
    "url": "https://example.test/a//b/*c*/,}",
    "escaped": "quote: \\\" // still a string",
  }`;
  assert.deepEqual(checkSources(files).errors, []);
});

test('Docker syntax supports case, CRLF, platform flags and continued instructions', () => {
  const files = fixture();
  files['Dockerfile'] = ['# FROM ignored:latest', 'fRoM --platform=$BUILDPLATFORM \\', '  # continuation comment', `  ${sdkImage(sdk)} aS Builder`, 'FROM builder AS publish', 'FROM scratch'].join('\r\n');
  assert.deepEqual(checkSources(files).errors, []);
});

test('the Docker escape directive supports backtick continuations', () => {
  const files = fixture();
  files['Dockerfile'] = `# escape=\x60\nFROM \x60\n  ${sdkImage(sdk)} AS build\n`;
  assert.deepEqual(checkSources(files).errors, []);
});

test('a continued mismatched FROM reports its starting line', () => {
  const files = fixture();
  files['Dockerfile'] = `# comment\nFROM \\\n  ${sdkImage('10.0.400')} AS build\n`;
  rejects(files, /Dockerfile:2.*10\.0\.400.*10\.0\.402/);
});

test('heredoc contents are data while a later FROM still participates in the policy', () => {
  const files = fixture();
  const source = [
    `FROM ${sdkImage(sdk)} AS build`,
    'RUN <<EOF', 'FROM not-an-image:latest', 'EOF',
    'COPY <<\'ONE\' <<-"TWO" /app/', 'FROM also-not-an-image:latest', 'ONE',
    '\tFROM literal-heredoc-content', '\tTWO',
    'RUN echo "<<IGNORED"', `FROM ${sdkImage(sdk)} AS another`
  ].join('\n');
  files['Dockerfile'] = source;
  assert.deepEqual(checkSources(files).errors, []);
  files['Dockerfile'] = source.replace(/FROM [^\n]+ AS another/, `FROM ${sdkImage('10.0.400')} AS another`);
  rejects(files, /Dockerfile:11.*10\.0\.400.*10\.0\.402/);
  files['Dockerfile'] = `FROM ${sdkImage(sdk)}\nRUN <<EOF\nunterminated\n`;
  rejects(files, /Dockerfile:2.*heredoc/);
});

test('only leading parser directives change the Docker escape character', () => {
  const files = fixture();
  files['Dockerfile'] = `FROM ${sdkImage(sdk)} AS build\n# escape=\x60\nFROM \\\n  ${sdkImage(sdk)} AS another\n`;
  assert.deepEqual(checkSources(files).errors, []);
});

test('dynamic image expressions fail instead of bypassing SDK or digest checks', () => {
  const files = fixture();
  files['Dockerfile'] = `ARG SDK=${sdk}\nFROM mcr.microsoft.com/dotnet/sdk:\${SDK}@${digest}\n`;
  rejects(files, /Dockerfile:2.*literal/);
});

test('unrecognised or incomplete FROM syntax fails with a location', () => {
  for (const instruction of ['FROM', 'FROM --unknown=x image', 'FROM image AS', 'FROM image extra tokens here', 'FROM \\']) {
    const files = fixture();
    files['Dockerfile'] = `${instruction}\n`;
    rejects(files, /Dockerfile:1.*FROM/);
  }
});

test('a stage name is exempt only after its declaration', () => {
  const files = fixture();
  files['Dockerfile'] = `FROM undeclared AS build\nFROM ${sdkImage(sdk)} AS undeclared\n`;
  rejects(files, /Dockerfile:1.*@sha256/);
});

test('global.json rejects missing or invalid SDK versions and malformed JSON', () => {
  for (const source of ['{}', '{', '{"sdk":{"version":null}}', '{"sdk":{"version":"latest"}}']) {
    const files = fixture();
    files['global.json'] = source;
    rejects(files, /global.json/);
  }
});

test('missing required configuration files fail instead of silently reducing coverage', () => {
  for (const path of ['global.json', '.devcontainer/devcontainer.json', 'tools/pr-process.mjs', 'tools/pr-process-fixture.json']) {
    const files = fixture();
    delete files[path];
    rejects(files, new RegExp(path.replaceAll('.', '\\.')));
  }
});

test('devcontainer requires exactly one dotnet feature and an explicit version', () => {
  for (const features of [{}, { 'ghcr.io/devcontainers/features/dotnet:2': {} }, {
    'ghcr.io/devcontainers/features/dotnet:2': { version: sdk }, 'ghcr.io/devcontainers/features/dotnet:3': { version: sdk }
  }]) {
    const files = fixture();
    files['.devcontainer/devcontainer.json'] = JSON.stringify({ features });
    rejects(files, /devcontainer.json/);
  }
});

test('PINNED_SDK must remain one inspectable literal; commented declarations are ignored', () => {
  const files = fixture();
  files['tools/pr-process.mjs'] = `/*\nexport const PINNED_SDK = 'wrong';\n*/\n// export const PINNED_SDK = 'wrong';\nexport const PINNED_SDK = "${sdk}";\n`;
  assert.deepEqual(checkSources(files).errors, []);
  for (const source of ['', `export const PINNED_SDK = readCandidate();`, `export const PINNED_SDK = '${sdk}';\nexport const PINNED_SDK = '${sdk}';`]) {
    files['tools/pr-process.mjs'] = source;
    rejects(files, /pr-process.mjs.*literal/);
  }
});

test('the PR-process fixture requires one build-evidence entry with an SDK', () => {
  for (const validation of [[], [{ id: 'local:pinned-sdk-build-zero-warnings' }], [
    { id: 'local:pinned-sdk-build-zero-warnings', sdk }, { id: 'local:pinned-sdk-build-zero-warnings', sdk }
  ]]) {
    const files = fixture();
    files['tools/pr-process-fixture.json'] = JSON.stringify({ author: { validation } });
    rejects(files, /pr-process-fixture.json/);
  }
});

test('malformed devcontainer and evidence JSON report their paths', () => {
  for (const path of ['.devcontainer/devcontainer.json', 'tools/pr-process-fixture.json']) {
    const files = fixture();
    files[path] = '{';
    rejects(files, new RegExp(path.replaceAll('.', '\\.')));
  }
});

test('no discovered SDK FROM cannot produce a vacuous pass', () => {
  const files = fixture();
  for (const path of Object.keys(files).filter(path => /Dockerfile/.test(path))) delete files[path];
  rejects(files, /SDK.*FROM/);
});

test('CLI discovers tracked Dockerfile names, ignores untracked files and fails read-only', () => {
  const root = mkdtempSync(join(tmpdir(), 'ws-452-sdk-'));
  try {
    const files = fixture();
    files['new/Dockerfile.extra'] = `FROM ${sdkImage(sdk)} AS build\n`;
    files['Dockerfile.dockerignore'] = 'FROM not-a-dockerfile:latest\n';
    for (const [path, source] of Object.entries(files)) {
      mkdirSync(dirname(join(root, path)), { recursive: true });
      writeFileSync(join(root, path), source);
    }
    execFileSync('git', ['init', '-q', root]);
    execFileSync('git', ['-C', root, 'add', '.']);
    writeFileSync(join(root, 'Dockerfile.untracked'), 'FROM untracked:latest\n');
    writeFileSync(join(root, 'ignored.Dockerfile.dockerignore'), 'FROM not-a-dockerfile:latest\n');
    const run = () => spawnSync(process.execPath, [tool, root], { encoding: 'utf8' });
    const aligned = run();
    assert.equal(aligned.status, 0, aligned.stderr);
    assert.match(aligned.stdout, /passed/);
    const path = 'new/Dockerfile.extra';
    writeFileSync(join(root, path), `FROM ${sdkImage('10.0.400')} AS build\n`);
    const status = () => execFileSync('git', ['-C', root, 'status', '--porcelain'], { encoding: 'utf8' });
    const before = status();
    const mismatch = run();
    assert.equal(mismatch.status, 1);
    assert.match(mismatch.stderr, /new\/Dockerfile.extra:1.*10\.0\.400.*10\.0\.402/);
    assert.equal(status(), before, 'The gate must not modify the fixture repository');
    rmSync(join(root, 'global.json'));
    assert.equal(run().status, 1);
  } finally { rmSync(root, { recursive: true, force: true }); }
});

test('Preflight runs both the fixture tests and the repository gate', () => {
  const source = readFileSync(new URL('../.github/workflows/pr-preflight.yml', import.meta.url), 'utf8');
  const workflow = JSON.parse(execFileSync('ruby', ['-ryaml', '-rjson', '-e', 'puts JSON.generate(YAML.load(STDIN.read))'], { input: source, encoding: 'utf8' }));
  const commands = workflow.jobs['simple-checks'].steps.map(step => step.run ?? '').join('\n');
  assert.match(commands, /node --test tools\/sdk-consistency.test.mjs/);
  assert.match(commands, /node tools\/sdk-consistency.mjs/);
});

test('the current tracked repository satisfies the SDK and base-image policy', () => {
  assert.deepEqual(checkRepository(repository).errors, []);
});
