import test from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { checkRepository, checkSources } from './sdk-consistency.mjs';

const sdk = '10.0.402';
const digest = `sha256:${'a'.repeat(64)}`;
const otherDigest = `sha256:${'b'.repeat(64)}`;
const sdkImage = version => `mcr.microsoft.com/dotnet/sdk:${version}@${digest}`;
const toolUrl = new URL('./sdk-consistency.mjs', import.meta.url);
const tool = fileURLToPath(toolUrl);
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
  assert.deepEqual(checkSources(fixture('99.0.100-preview.1')).errors, []);
});

test('global.json is authoritative when every other SDK reference is left behind', () => {
  const files = fixture();
  files['global.json'] = JSON.stringify({ sdk: { version: '10.0.403' } });
  rejects(files, /src\/App\/Dockerfile:1.*10\.0\.402.*10\.0\.403/, /devcontainer.*10\.0\.403/, /PINNED_SDK.*10\.0\.403/, /pr-process-fixture.*10\.0\.403/);
});

test('global.json with comments stays authoritative', () => {
  const files = fixture();
  files['global.json'] = `{
    // single SDK source
    "sdk": { "version": "${sdk}" /* exact */, "rollForward": "disable" }
  }`;
  assert.deepEqual(checkSources(files).errors, []);

  files['global.json'] = files['global.json'].replace(sdk, '10.0.403');
  rejects(files, /src\/App\/Dockerfile:1.*10\.0\.402.*10\.0\.403/, /PINNED_SDK.*10\.0\.403/);
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

test('Docker Hub short names and registry aliases share a digest pin', () => {
  for (const alias of ['docker.io/library/eclipse-mosquitto', 'index.docker.io/eclipse-mosquitto']) {
    const files = fixture();
    files['Dockerfile.one'] = `FROM eclipse-mosquitto:2.0.22@${digest}\n`;
    files['Dockerfile.two'] = `FROM ${alias}:2.0.22@${otherDigest}\n`;
    rejects(files, /Dockerfile.two:1.*digest.*Dockerfile.one:1/);
    files['Dockerfile.two'] = files['Dockerfile.two'].replace(otherDigest, digest);
    assert.deepEqual(checkSources(files).errors, []);
  }
});

test('SDK pins agree across registry host case and image source kinds', () => {
  const files = fixture();
  files['z/Dockerfile'] = `FROM MCR.microsoft.com/dotnet/sdk:${sdk}@${otherDigest}\n`;
  rejects(files, /z\/Dockerfile:1.*digest.*src\/App\/Dockerfile:1/);
  files['z/Dockerfile'] = `FROM scratch\nCOPY --from=MCR.microsoft.com/dotnet/sdk:${sdk}@${otherDigest} /sdk /sdk\n`;
  rejects(files, /z\/Dockerfile:2.*digest.*src\/App\/Dockerfile:1/);
  files['z/Dockerfile'] = files['z/Dockerfile'].replace(otherDigest, digest);
  assert.deepEqual(checkSources(files).errors, []);
});

for (const host of ['MCR.Microsoft.com', 'mcr.microsoft.com:443']) {
  test(`SDK version checks use the canonical registry for ${host}`, () => {
    const files = fixture();
    files['Dockerfile'] = `FROM ${host}/dotnet/sdk:9.0.100@${digest}\n`;
    rejects(files, /Dockerfile:1.*SDK FROM tag.*9\.0\.100.*10\.0\.402/);
    files['Dockerfile'] = files['Dockerfile'].replace('9.0.100', sdk);
    assert.deepEqual(checkSources(files).errors, []);
  });
}

test('normalization preserves separate namespaces, non-default ports, and tag case', () => {
  const files = fixture();
  files['Dockerfile'] = [
    `FROM busybox:Edge@${digest}`, `FROM docker.io/library/busybox:edge@${otherDigest}`,
    `FROM custom/busybox:Edge@${otherDigest}`,
    `FROM registry.example.com/tiny:stable@${digest}`, `FROM registry.example.com:5000/tiny:stable@${otherDigest}`
  ].join('\n');
  assert.deepEqual(checkSources(files).errors, []);
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

for (const [prefix, description] of [['\uFEFF', 'a UTF-8 BOM'], [' \t', 'leading whitespace']]) {
  test(`an escape directive after ${description} keeps a later FROM visible`, () => {
    const files = fixture();
    files['Dockerfile'] = [
      `${prefix}# escape=\x60`, `FROM alpine:3.20@${digest} AS tools`, 'RUN echo C:\\',
      'FROM mcr.microsoft.com/dotnet/sdk:9.0.100 AS old'
    ].join('\r\n');
    rejects(files, /Dockerfile:4.*FROM mcr.*@sha256/, /Dockerfile:4.*SDK FROM tag.*9\.0\.100.*10\.0\.402/);
  });
}

for (const [command, description] of [
  [['RUN cat\\ \t', '<<EOF'], 'a joined continuation'],
  [['RUN echo a\\ <<EOF'], 'an escaped space']
]) {
  test(`a heredoc-like word after ${description} keeps a later FROM visible`, () => {
    const files = fixture();
    files['Dockerfile'] = [
      `FROM alpine:3.20@${digest} AS tools`, ...command, 'FROM node:latest AS web',
      'RUN <<EOF', 'echo hi', 'EOF'
    ].join('\n');
    rejects(files, new RegExp(`Dockerfile:${command.length + 2}.*FROM node:latest.*@sha256`));
  });
}

test('an indented continuation can start a heredoc with a partially quoted marker', () => {
  const files = fixture();
  files['Dockerfile'] = [
    `FROM ${sdkImage(sdk)}`, 'RUN cat\\', '  <<E"O"F',
    'FROM literal-content', 'EOF'
  ].join('\n');
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

test('doubled Docker escape characters do not hide a later unpinned wrong-SDK FROM', () => {
  for (const escape of ['\\', '`']) {
    const files = fixture();
    const directive = escape === '`' ? ['# escape=`'] : [];
    files['Dockerfile'] = [
      ...directive, `FROM alpine:3.20@${digest} AS tools`, `RUN echo C:${escape.repeat(2)}`,
      'FROM mcr.microsoft.com/dotnet/sdk:9.0.100 AS old'
    ].join('\n');
    const location = `Dockerfile:${3 + directive.length}`;
    rejects(files, new RegExp(`${location}.*FROM mcr.*@sha256`), new RegExp(`${location}.*SDK FROM tag.*9\\.0\\.100.*10\\.0\\.402`));
  }
});

for (const operator of ['<<<EOF', 'embedded<<EOF']) {
  test(`${operator} is not a heredoc and cannot hide a later unpinned FROM`, () => {
    const files = fixture();
    files['Dockerfile'] = [
      `FROM alpine:3.20@${digest} AS tools`, `RUN tr a-z A-Z ${operator}`,
      'FROM node:latest AS web', 'RUN <<EOF', 'echo hi', 'EOF'
    ].join('\n');
    rejects(files, /Dockerfile:3.*FROM node:latest.*@sha256/);
  });
}

test('a quoted here-string does not create an unterminated Docker heredoc', () => {
  const files = fixture();
  files['Dockerfile'] = `FROM ${sdkImage(sdk)}\nRUN grep -q x <<<"$VAR"\n`;
  assert.deepEqual(checkSources(files).errors, []);
});

test('ADD heredoc contents beginning with FROM are data', () => {
  const files = fixture();
  files['Dockerfile'] = `FROM ${sdkImage(sdk)}\nADD <<EOF /app/message\nFROM not-an-image:latest\nEOF\n`;
  assert.deepEqual(checkSources(files).errors, []);
});

test('Docker heredoc words allow file descriptors and whitespace before quoted markers', () => {
  const files = fixture();
  files['Dockerfile'] = `FROM ${sdkImage(sdk)}\nRUN cat 3<< "EOF"\nFROM literal-content\nEOF\n`;
  assert.deepEqual(checkSources(files).errors, []);
});

test('dynamic image expressions fail instead of bypassing SDK or digest checks', () => {
  const files = fixture();
  files['Dockerfile'] = `ARG SDK=${sdk}\nFROM mcr.microsoft.com/dotnet/sdk:\${SDK}@${digest}\n`;
  rejects(files, /Dockerfile:2.*literal/);
});

test('quoted or escaped image fragments cannot hide the SDK tag', () => {
  for (const image of ['"mcr.microsoft.com/dotnet/sdk:9.0.100"', 'mcr.microsoft.com/dotnet/\\sdk:9.0.100']) {
    const files = fixture();
    files['Dockerfile'] = `FROM ${image}@${digest}\n`;
    rejects(files, /Dockerfile:1.*FROM image must be literal/);
  }
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

for (const [label, instruction] of [
  ['COPY --from', image => `COPY --chown=1:1 --from="${image}" ["/sdk", "/sdk"]`],
  ['RUN --mount from', image => `RUN --mount=type=tmpfs,target=/tmp \\\n  --mount='type=bind,"FrOm=${image}",target=/sdk' true`]
]) {
  test(`${label} checks literal SDK versions and digest pins for external images`, () => {
    const files = fixture();
    const setImage = image => { files['z/Dockerfile'] = `FROM scratch\n${instruction(image)}\n`; };
    const diagnostic = suffix => new RegExp(`z/Dockerfile:2.*${label}.*${suffix}`);

    setImage('mcr.microsoft.com/dotnet/sdk:9.0.100');
    rejects(files, diagnostic('@sha256'), diagnostic('9\\.0\\.100.*10\\.0\\.402'));
    setImage('mcr.microsoft.com/dotnet/sdk:${SDK}@' + digest);
    rejects(files, diagnostic('literal'));
    setImage(`mcr.microsoft.com/dotnet/sdk:${sdk}@${otherDigest}`);
    rejects(files, /z\/Dockerfile:2.*digest.*src\/App\/Dockerfile:1/);
    setImage(sdkImage(sdk));
    assert.deepEqual(checkSources(files).errors, []);
  });
}

test('the syntax frontend requires a pin at the directive line', () => {
  const files = fixture();
  const setFrontend = frontend => {
    files['Dockerfile'] = `\uFEFF#!/usr/bin/env docker\n# SyNtAx = ${frontend} option=value\nFROM scratch\n`;
  };
  setFrontend('docker/dockerfile:1');
  rejects(files, /Dockerfile:2.*# syntax.*@sha256/);
  setFrontend(`docker/dockerfile:1@${digest}`);
  assert.deepEqual(checkSources(files).errors, []);
});

test('COPY indexes and prior stage names are exempt; only real builder flags are checked', () => {
  const files = fixture();
  files['Dockerfile'] = [
    `FROM ${sdkImage(sdk)} AS build`, 'FROM scratch',
    'COPY --from="BUILD" /src /src', 'COPY --from=0 /src /src',
    `RUN --mount='type=bind,"source=/data,from=unrelated:latest",from=build,target=/src' true`,
    'RUN --mount=type=cache,target=/cache true', 'RUN echo --mount=from=unrelated:latest',
    '# syntax=docker/dockerfile:1'
  ].join('\n');
  assert.deepEqual(checkSources(files).errors, []);
  files['Dockerfile'] += '\nCOPY --from=undeclared /src /src\nRUN --mount=from=0,target=/src true\n';
  rejects(files, /Dockerfile:9.*COPY --from.*@sha256/, /Dockerfile:10.*RUN --mount from.*@sha256/);
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

test('a symlinked CLI invocation rejects a mismatching tracked repository', () => {
  const root = mkdtempSync(join(tmpdir(), 'ws-452-sdk-link-'));
  try {
    const files = fixture();
    files['src/App/Dockerfile'] = `FROM ${sdkImage('10.0.400')} AS build\n`;
    for (const [path, source] of Object.entries(files)) {
      mkdirSync(dirname(join(root, path)), { recursive: true });
      writeFileSync(join(root, path), source);
    }
    execFileSync('git', ['init', '-q', root]);
    execFileSync('git', ['-C', root, 'add', '.']);
    const linkedTool = join(root, 'gate.mjs');
    symlinkSync(tool, linkedTool);
    const result = spawnSync(process.execPath, [linkedTool, root], { encoding: 'utf8' });
    assert.equal(result.status, 1, 'A symlinked entry point must execute the gate');
    assert.match(result.stderr, /src\/App\/Dockerfile:1.*10\.0\.400.*10\.0\.402/);
  } finally { rmSync(root, { recursive: true, force: true }); }
});

test('a stdin module can import the gate without running its CLI', () => {
  const result = spawnSync(process.execPath, ['--input-type=module', '-'], {
    input: `import ${JSON.stringify(toolUrl.href)};`,
    encoding: 'utf8'
  });
  assert.equal(result.status, 0, result.stderr);
  assert.equal(result.stdout, '');
});

test('the current tracked repository satisfies the SDK and base-image policy', () => {
  assert.deepEqual(checkRepository(repository).errors, []);
});
