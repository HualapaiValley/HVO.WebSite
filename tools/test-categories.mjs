// Literal MSTest conventions, not a C# compiler. CI may use the declared category
// groups conservatively; unsupported syntax never establishes an empty test lane.
import { readdir, readFile } from 'node:fs/promises';
import { resolve, relative, basename } from 'node:path';
import { pathToFileURL } from 'node:url';

const categories = new Set(['Integration', 'HomeAssistantIntegration', 'SqlServerIntegration', 'Browser', 'Live']);
const tokenPattern = /\/\/[^\n]*|\/\*[\s\S]*?\*\/|"""[\s\S]*?"""|@"(?:[^"]|"")*"|"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|[A-Za-z_]\w*|[^\s]/g;

export function validateSource(source, file) {
  const tokens = [...source.matchAll(tokenPattern)].filter(x => !x[0].startsWith('//') && !x[0].startsWith('/*')).map(x => x[0]);
  const errors = [];
  const scopes = [{ categories: [], testClass: false }];
  let attributes = [], header = [];
  let methods = 0;
  const declarations = [], testClasses = [];
  const parsedCategories = () => {
    const values = [];
    for (let i = 0; i < attributes.length; i++) {
      if (attributes[i] !== 'TestCategory') continue;
      const value = attributes[i + 2];
      if (attributes[i + 1] !== '(' || !/^"[A-Za-z]+"$/.test(value ?? '') || attributes[i + 3] !== ')') {
        errors.push(`${file}: TestCategory must use a literal supported name`);
      } else {
        const name = value.slice(1, -1);
        if (!categories.has(name)) errors.push(`${file}: unsupported TestCategory("${name}")`);
        values.push(name);
      }
    }
    return values;
  };
  const checkMethod = () => {
    if (!attributes.includes('TestMethod') && !attributes.includes('DataTestMethod')) return;
    methods++;
    const scope = scopes.at(-1);
    const names = new Set([...scope.categories, ...parsedCategories()]);
    const method = header[header.indexOf('(') - 1] ?? '<test>';
    declarations.push({ method, categories: [...names].sort() });
    const report = message => errors.push(`${file}:${method}: ${message}`);
    if (!scope.testClass) report('test method requires an owning [TestClass]');
    if (/(^|\/)Integration\//.test(file) || /IntegrationTests\.cs$/.test(file)) {
      if (!names.has('Integration')) report('requires TestCategory("Integration")');
    }
    if (/(^|\/)Live\//.test(file) || /Live[^/]*Tests\.cs$/.test(basename(file))) {
      if (!names.has('Live')) report('requires TestCategory("Live")');
    }
    for (const required of ['HomeAssistantIntegration', 'SqlServerIntegration']) {
      if (names.has(required) && !names.has('Integration')) report(`${required} requires Integration`);
    }
    if (names.has('Live') && [...names].some(x => x !== 'Live')) report('Live cannot also belong to a required non-live lane');
  };
  for (let i = 0; i < tokens.length; i++) {
    const token = tokens[i];
    if (token === '[') {
      let depth = 1;
      while (depth && ++i < tokens.length) {
        if (tokens[i] === '[') depth++;
        if (tokens[i] === ']') depth--;
        if (depth) attributes.push(tokens[i]);
      }
      continue;
    }
    if (token === '{') {
      const parent = scopes.at(-1);
      const isClass = header.includes('class');
      if (isClass && attributes.includes('TestClass')) {
        const inheritance = header.indexOf(':');
        testClasses.push({
          name: header[header.indexOf('class') + 1],
          partial: header.includes('partial'),
          bases: inheritance < 0 ? [] : header.slice(inheritance + 1).join('').split(',')
        });
      }
      checkMethod();
      scopes.push(isClass ? { categories: parsedCategories(), testClass: attributes.includes('TestClass') } : parent);
      attributes = []; header = [];
    } else if (token === '}') {
      if (scopes.length > 1) scopes.pop();
      attributes = []; header = [];
    } else if (token === ';') {
      checkMethod(); // Expression-bodied tests also have category obligations.
      attributes = []; header = [];
    } else {
      header.push(token);
    }
  }
  return { methods, errors, declarations, testClasses };
}

async function files(root) {
  const found = [];
  for (const entry of await readdir(root, { withFileTypes: true })) {
    if (['bin', 'obj'].includes(entry.name)) continue;
    const path = resolve(root, entry.name);
    if (entry.isDirectory()) found.push(...await files(path));
    else if (entry.name.endsWith('.cs')) found.push(path);
  }
  return found.sort();
}

async function main() {
  const root = resolve(process.argv[2] ?? 'tests');
  let methods = 0, errors = [];
  for (const path of await files(root)) {
    const result = validateSource(await readFile(path, 'utf8'), relative(root, path).replaceAll('\\', '/'));
    methods += result.methods; errors.push(...result.errors);
  }
  if (!methods) errors.push('No MSTest methods found; refusing an empty category check');
  if (errors.length) { console.error(errors.join('\n')); process.exitCode = 1; }
  else console.log(`Validated categories for ${methods} MSTest methods`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) main().catch(error => { console.error(error.message); process.exitCode = 1; });
