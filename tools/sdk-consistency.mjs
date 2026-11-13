// Offline policy: inspect tracked configuration as data; never execute candidate tooling.
import { execFileSync } from 'node:child_process';
import { readFileSync, realpathSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const required = ['global.json', '.devcontainer/devcontainer.json', 'tools/pr-process.mjs', 'tools/pr-process-fixture.json'];
const dockerfile = path => !/\.dockerignore$/i.test(path) && /(?:^|\/)(?:Dockerfile(?:\.[^/]+)?|[^/]+\.Dockerfile)$/i.test(path);

// Protect quoted strings before removing comments (URLs and escaped quotes are data).
const quoted = /("(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|`(?:\\.|[^`\\])*`)|\/\/[^\r\n]*|\/\*[\s\S]*?\*\//g;
const withoutComments = source => source.replace(quoted, (match, string) => string ?? match.replace(/[^\r\n]/g, ' '));
function jsonc(source) {
  return JSON.parse(withoutComments(source).replace(/("(?:\\.|[^"\\])*")|,\s*(?=[}\]])/g, (match, string) => string ?? ''));
}

// Docker ignores full-line comments even inside continued instructions.
function instructions(source) {
  const lines = source.split(/\r?\n/);
  let escape = '\\';
  for (const line of lines) {
    const directive = /^#\s*(syntax|escape|check)\s*=\s*(.+?)\s*$/i.exec(line);
    if (!directive) break;
    if (directive[1].toLowerCase() === 'escape' && /^[\\`]$/.test(directive[2])) escape = directive[2];
  }
  const result = [];
  const heredocs = [];
  let text = '';
  let start = 0;
  for (const [index, line] of lines.entries()) {
    if (heredocs.length) {
      const marker = heredocs[0];
      if ((marker.tabs ? line.replace(/^\t+/, '') : line) === marker.name) heredocs.shift();
      continue;
    }
    if (!line.trim() || /^\s*#/.test(line)) continue;
    if (!text) start = index + 1;
    const part = line.trim();
    // BuildKit treats doubled terminal escape characters as literal text.
    const continued = part.endsWith(escape) && part.at(-2) !== escape;
    text += (continued ? part.slice(0, -1) : part) + ' ';
    if (!continued) {
      result.push({ text: text.trim(), line: start });
      if (/^(RUN|COPY|ADD)\s/i.test(text)) {
        // Quoted shell strings can contain << without declaring a heredoc.
        const markers = /("(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|`(?:\\.|[^`\\])*`)|(?:^|\s)\d*<<(-?)(?!<)\s*(?:"([^"]+)"|'([^']+)'|([a-zA-Z0-9_.-]+))(?=\s|$)/g;
        for (const match of text.matchAll(markers)) {
          if (!match[1]) heredocs.push({ name: match[3] ?? match[4] ?? match[5], tabs: match[2] === '-', line: start });
        }
      }
      text = '';
    }
  }
  if (text) result.push({ text: text.trim(), line: start, incomplete: true });
  if (heredocs.length) result.push({ line: heredocs[0].line, error: `unterminated Docker heredoc; add its ${heredocs[0].name} terminator before the next instruction.` });
  return result;
}

export function checkSources(files) {
  const errors = [];
  for (const path of required) if (typeof files[path] !== 'string') errors.push(`${path}: required tracked configuration is missing; restore it before running the gate.`);
  if (errors.length) return { errors };
  const readJson = (path, parser = JSON.parse) => {
    try { return parser(files[path]); }
    catch { errors.push(`${path}: invalid JSON${parser === jsonc ? 'C' : ''}; correct the configuration syntax.`); }
  };
  const sdk = readJson('global.json')?.sdk?.version;
  if (typeof sdk !== 'string' || !/^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$/.test(sdk)) {
    errors.push('global.json: sdk.version must be an explicit SDK version (for example 10.0.402).');
    return { errors };
  }
  const compare = (location, actual) => {
    if (actual !== sdk) errors.push(`${location}: found ${JSON.stringify(actual) ?? 'no version'}; expected ${sdk} from global.json. Update all SDK references together.`);
  };
  const container = readJson('.devcontainer/devcontainer.json', jsonc);
  const features = Object.entries(container?.features ?? {}).filter(([name]) => /^ghcr\.io\/devcontainers\/features\/dotnet:/.test(name));
  if (features.length !== 1) errors.push('.devcontainer/devcontainer.json: require exactly one dotnet feature with a version matching global.json.');
  else compare(`.devcontainer/devcontainer.json: features[${features[0][0]}].version`, features[0][1]?.version);

  // Require the existing literal declaration rather than importing/executing candidate JS.
  const pins = [...withoutComments(files['tools/pr-process.mjs']).matchAll(/^export\s+const\s+PINNED_SDK\s*=\s*(['"])([^'"\r\n]+)\1\s*;\s*$/gm)];
  if (pins.length !== 1) errors.push('tools/pr-process.mjs: require exactly one literal export const PINNED_SDK = \'<global.json SDK>\'; declaration.');
  else compare('tools/pr-process.mjs: PINNED_SDK', pins[0][2]);
  const evidence = readJson('tools/pr-process-fixture.json')?.author?.validation;
  const builds = Array.isArray(evidence) ? evidence.filter(item => item?.id === 'local:pinned-sdk-build-zero-warnings') : [];
  if (builds.length !== 1) errors.push('tools/pr-process-fixture.json: require exactly one local:pinned-sdk-build-zero-warnings entry with sdk matching global.json.');
  else compare('tools/pr-process-fixture.json: build validation sdk', builds[0].sdk);

  const digests = new Map();
  let sdkImages = 0;
  for (const path of Object.keys(files).filter(dockerfile).sort()) {
    const stages = new Set();
    for (const instruction of instructions(files[path])) {
      const location = `${path}:${instruction.line}`;
      if (instruction.error) { errors.push(`${location}: ${instruction.error}`); continue; }
      if (!/^FROM(?:\s|$)/i.test(instruction.text)) continue;
      const match = /^FROM\s+(?:--platform=\S+\s+)?([^\s]+)(?:\s+AS\s+([a-zA-Z0-9_][a-zA-Z0-9_.-]*))?$/i.exec(instruction.text);
      if (!match || instruction.incomplete || match[1].startsWith('--')) {
        errors.push(`${location}: unsupported or incomplete FROM; use FROM [--platform=<platform>] <literal-image>@sha256:<64 hex digits> [AS <stage>].`);
        continue;
      }
      const [, image, stage] = match;
      if (!stages.has(image.toLowerCase()) && image.toLowerCase() !== 'scratch') {
        if (image.includes('$')) errors.push(`${location}: FROM image must be literal; replace build-argument image/version/digest expressions with a pinned reference matching global.json.`);
        const [taggedImage, pin, extra] = image.split('@');
        if (!pin || !/^sha256:[a-f0-9]{64}$/.test(pin) || extra !== undefined) {
          errors.push(`${location}: FROM ${taggedImage} must be pinned as <image>[:<tag>]@sha256:<64 lowercase hex digits>; retain the tag and add its verified digest.`);
        } else {
          const previous = digests.get(taggedImage);
          if (previous && previous.pin !== pin) errors.push(`${location}: ${taggedImage} digest ${pin} differs from ${previous.pin} at ${previous.location}; use the same verified digest for this image reference everywhere.`);
          else if (!previous) digests.set(taggedImage, { pin, location });
        }
        const sdkTag = /^mcr\.microsoft\.com\/dotnet\/sdk(?::(.*))?$/.exec(taggedImage);
        if (sdkTag) {
          sdkImages++;
          compare(`${location}: SDK FROM tag`, sdkTag[1]);
        }
      }
      if (stage) stages.add(stage.toLowerCase());
    }
  }
  if (!sdkImages) errors.push('Dockerfiles: no .NET SDK FROM found; restore an explicit mcr.microsoft.com/dotnet/sdk:<global.json SDK>@sha256:<digest> build stage.');
  return { sdk, errors };
}

export function checkRepository(root) {
  const paths = execFileSync('git', ['ls-files', '-z'], { cwd: root, encoding: 'utf8' }).split('\0').filter(Boolean);
  const files = Object.fromEntries(paths.filter(path => required.includes(path) || dockerfile(path))
    .map(path => [path, readFileSync(resolve(root, path), 'utf8')]));
  return checkSources(files);
}

if (process.argv[1] && realpathSync(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const result = checkRepository(resolve(process.argv[2] ?? fileURLToPath(new URL('..', import.meta.url))));
    if (result.errors.length) {
      console.error(`SDK consistency check failed:\n${result.errors.map(error => `- ${error}`).join('\n')}`);
      process.exitCode = 1;
    } else console.log('SDK and base-image consistency check passed.');
  } catch (error) {
    console.error(`SDK consistency check failed: ${error.message}`);
    process.exitCode = 1;
  }
}
