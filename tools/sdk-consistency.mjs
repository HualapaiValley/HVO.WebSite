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

function heredocMarkers(text) {
  if (!/^(?:ONBUILD\s+)?(?:RUN|COPY|ADD)\s/i.test(text) || !text.includes('<<')) return [];

  // BuildKit looks for a heredoc operator at the start of a raw shell word.
  // Quotes and backslash escapes keep whitespace inside that word.
  const tokens = /(\p{White_Space}+)|(?:\\.?|'[^']*'|"(?:\\.|[^"\\])*"|<<[ \t\r]*|<(?!<)|[^\p{White_Space}'"\\<])+/gsuy;
  const markers = [];
  let consumed = 0;
  for (const [word, whitespace] of text.matchAll(tokens)) {
    consumed += word.length;
    if (whitespace) continue;
    const match = /^\d*<<(-?)([^<]*)$/.exec(word);
    if (!match) continue;
    const name = match[2].trimStart().replace(/'([^']*)'|"((?:\\.|[^"\\])*)"|\\(.)/gsu,
      (_, single, double, escaped) => single ?? double?.replace(/\\([$"\x60\\])/g, '$1') ?? escaped);
    if (name) markers.push({ name, tabs: match[1] === '-' });
  }
  return consumed === text.length ? markers : [];
}

function parseDockerfile(source) {
  const lines = source.replace(/^\uFEFF/, '').split('\n');
  const leadingWhitespace = /^\p{White_Space}+/u;
  const result = [];
  const hasShebang = lines[0].startsWith('#!');
  let escape = '\\';
  for (const [index, line] of lines.entries()) {
    // Frontend detection skips an initial shebang. Escape parsing does not.
    if (hasShebang && index === 0) continue;
    const directive = /^#\s*(syntax|escape|check)\s*=\s*(.+?)\s*$/i.exec(line.replace(leadingWhitespace, ''));
    if (!directive) break;
    const [, name, value] = directive;
    if (!hasShebang && name.toLowerCase() === 'escape' && /^[\\`]$/.test(value)) escape = value;
    if (name.toLowerCase() === 'syntax') result.push({ frontend: value.split(' ', 1)[0], line: index + 1 });
  }
  let heredocs = [];
  let heredocStart = 0;
  let text = '';
  let start = 0;
  for (const [index, rawLine] of lines.entries()) {
    const line = rawLine.replace(/\r+$/, '');
    if (heredocs.length) {
      const marker = heredocs[0];
      if ((marker.tabs ? line.replace(/^\t+/, '') : line) === marker.name) heredocs.shift();
      continue;
    }
    const trimmed = line.replace(leadingWhitespace, '');
    // Docker ignores full-line comments even inside continued instructions.
    if (!trimmed || trimmed.startsWith('#')) continue;
    const part = start ? line : trimmed;
    if (!start) start = index + 1;
    const tail = part.replace(/[ \t]+$/, '');
    const continued = tail.endsWith(escape) && tail.at(-2) !== escape;
    // Keep the continuation line's indentation and concatenate without a separator.
    text += continued ? tail.slice(0, -1) : part;
    if (!continued) {
      result.push({ text: text.trim(), line: start });
      heredocs = heredocMarkers(text.trimStart());
      heredocStart = start;
      text = '';
      start = 0;
    }
  }
  if (start) result.push({ text: text.trim(), line: start, incomplete: true });
  if (heredocs.length) result.push({ line: heredocStart, error: `unterminated Docker heredoc; add its ${heredocs[0].name} terminator before the next instruction.` });
  return { instructions: result, escape };
}

// Builder flags precede operands and use the Dockerfile's escape token.
function builderFlags(args, escape) {
  const flags = [];
  const whitespace = /\p{White_Space}/u;
  let offset = 0;
  while (args.startsWith('--', offset)) {
    let flag = '';
    let quote = '';
    for (; offset < args.length; offset++) {
      const char = args[offset];
      if (!quote && whitespace.test(char)) break;
      if (char === escape) flag += args[++offset] ?? '';
      else if (char === quote) quote = '';
      else if (!quote && (char === '"' || char === "'")) quote = char;
      else flag += char;
    }
    if (flag === '--') break;
    flags.push(flag);
    while (offset < args.length && whitespace.test(args[offset])) offset++;
  }
  return flags;
}

function mountSource(value) {
  // Mount options are CSV after builder flag quotes and escapes are processed.
  const fields = /(?:"((?:[^"]|"")*)"|([^",]*))(?:,|$)/gy;
  let from = '';
  let consumed = 0;
  for (const [field, quotedField, plainField] of value.matchAll(fields)) {
    consumed += field.length;
    const option = plainField ?? quotedField.replaceAll('""', '"');
    const equals = option.indexOf('=');
    if (equals !== -1 && option.slice(0, equals).toLowerCase() === 'from') from = option.slice(equals + 1);
  }
  return consumed === value.length ? from : undefined;
}

function normalizeImage(taggedImage) {
  const slash = taggedImage.indexOf('/');
  const first = slash === -1 ? '' : taggedImage.slice(0, slash);
  // Docker treats the first path component as a registry when it looks like a
  // host or port, is localhost, or contains uppercase letters.
  const hasRegistry = first === 'localhost' || /[.:A-Z]/.test(first);
  let registry = hasRegistry ? first.toLowerCase().replace(/:443$/, '') : 'docker.io';
  if (registry === 'index.docker.io') registry = 'docker.io';
  let repository = hasRegistry ? taggedImage.slice(slash + 1) : taggedImage;
  if (registry === 'docker.io' && !repository.includes('/')) repository = `library/${repository}`;
  return `${registry}/${repository}`;
}

export function checkSources(files) {
  const errors = [];
  for (const path of required) if (typeof files[path] !== 'string') errors.push(`${path}: required tracked configuration is missing; restore it before running the gate.`);
  if (errors.length) return { errors };
  const readJson = (path, parser = JSON.parse) => {
    try { return parser(files[path]); }
    catch { errors.push(`${path}: invalid JSON${parser === jsonc ? 'C' : ''}; correct the configuration syntax.`); }
  };
  const sdk = readJson('global.json', jsonc)?.sdk?.version;
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
  let sdkFroms = 0;
  const checkImage = (location, source, image) => {
    const [taggedImage, pin, extra] = image.split('@');
    // BuildKit can rewrite quoted or escaped FROM names before resolving them.
    if (!taggedImage || /[\s$'"`\\]/.test(image)) errors.push(`${location}: ${source} image must be literal; use a pinned reference matching global.json.`);
    const normalized = normalizeImage(taggedImage);
    if (!pin || !/^sha256:[a-f0-9]{64}$/.test(pin) || extra !== undefined) {
      errors.push(`${location}: ${source} ${taggedImage} must be pinned as <image>[:<tag>]@sha256:<64 lowercase hex digits>; retain the tag and add its verified digest.`);
    } else {
      const previous = digests.get(normalized);
      if (previous && previous.pin !== pin) errors.push(`${location}: ${taggedImage} digest ${pin} differs from ${previous.pin} at ${previous.location}; use the same verified digest for ${normalized}.`);
      else if (!previous) digests.set(normalized, { pin, location });
    }
    const sdkTag = /^mcr\.microsoft\.com\/dotnet\/sdk(?::(.*))?$/.exec(normalized);
    if (sdkTag) {
      if (source === 'FROM') sdkFroms++;
      compare(`${location}: SDK ${source} tag`, sdkTag[1]);
    }
  };
  for (const path of Object.keys(files).filter(dockerfile).sort()) {
    const stages = new Set();
    const isLocalSource = image => image.toLowerCase() === 'scratch' || stages.has(image.toLowerCase());
    let stageCount = 0;
    const { instructions, escape } = parseDockerfile(files[path]);
    for (const instruction of instructions) {
      const location = `${path}:${instruction.line}`;
      if (instruction.error) { errors.push(`${location}: ${instruction.error}`); continue; }
      if (instruction.frontend !== undefined) {
        checkImage(location, '# syntax', instruction.frontend);
        continue;
      }
      if (/^FROM(?:\s|$)/i.test(instruction.text)) {
        const match = /^FROM\s+(?:--platform=\S+\s+)?([^\s]+)(?:\s+AS\s+([a-zA-Z0-9_][a-zA-Z0-9_.-]*))?$/i.exec(instruction.text);
        if (!match || instruction.incomplete || match[1].startsWith('--')) {
          errors.push(`${location}: unsupported or incomplete FROM; use FROM [--platform=<platform>] <literal-image>@sha256:<64 hex digits> [AS <stage>].`);
          continue;
        }
        const [, image, stage] = match;
        if (!isLocalSource(image)) checkImage(location, 'FROM', image);
        if (stage) stages.add(stage.toLowerCase());
        stageCount++;
        continue;
      }
      const command = /^(COPY|RUN)\s+([\s\S]+)/i.exec(instruction.text);
      if (!command) continue;
      const copy = command[1].toUpperCase() === 'COPY';
      const prefix = copy ? '--from=' : '--mount=';
      for (const flag of builderFlags(command[2], escape)) {
        if (!flag.startsWith(prefix)) continue;
        const value = flag.slice(prefix.length);
        const image = copy ? value : mountSource(value);
        if (image === undefined) {
          errors.push(`${location}: invalid RUN --mount options; use comma-separated mount fields.`);
          continue;
        }
        if (!image || isLocalSource(image)) continue;
        if (copy && /^[+-]?\d+$/.test(image) && Number(image) >= 0 && Number(image) < stageCount) continue;
        checkImage(location, copy ? 'COPY --from' : 'RUN --mount from', image);
      }
    }
  }
  if (!sdkFroms) errors.push('Dockerfiles: no .NET SDK FROM found; restore an explicit mcr.microsoft.com/dotnet/sdk:<global.json SDK>@sha256:<digest> build stage.');
  return { sdk, errors };
}

export function checkRepository(root) {
  const paths = execFileSync('git', ['ls-files', '-z'], { cwd: root, encoding: 'utf8' }).split('\0').filter(Boolean);
  const files = Object.fromEntries(paths.filter(path => required.includes(path) || dockerfile(path))
    .map(path => [path, readFileSync(resolve(root, path), 'utf8')]));
  return checkSources(files);
}

function invokedByPath() {
  const entryPoint = process.argv[1];
  if (!entryPoint || entryPoint === '-') return false;
  try {
    return realpathSync(entryPoint) === fileURLToPath(import.meta.url);
  } catch (error) {
    if (error.code !== 'ENOENT') throw error;
    return false;
  }
}

// Early Node 24 releases do not expose import.meta.main.
if (import.meta.main ?? invokedByPath()) {
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
