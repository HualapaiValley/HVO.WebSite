// The Davis publication contract for the remaining Pi collectors.
// Never print credentials or registry HTTP bodies.
import { readFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { verifyManifest } from './davis-image.mjs';

export { verifyManifest };
export const COLLECTORS = ['jkbms', 'smartshunt', 'eg4'];
const registry = 'registry.hualapaivalleyobservatory.org';
const numeric = '(?:0|[1-9][0-9]*)';
const identifier = `(?:${numeric}|[0-9]*[A-Za-z-][0-9A-Za-z-]*)`;
const semver = new RegExp(`^${numeric}\\.${numeric}\\.${numeric}(?:-${identifier}(?:\\.${identifier})*)?$`);

export function imageFor(collector) {
  if (!COLLECTORS.includes(collector)) throw new Error('Expected jkbms, smartshunt or eg4.');
  return `${registry}/hvo-${collector}`;
}

export function versionFor(collector, { GITHUB_EVENT_NAME: event, GITHUB_REF: ref, REQUESTED_VERSION: requested }) {
  imageFor(collector);
  const prefix = `refs/tags/${collector}-v`;
  const version = event === 'push' && ref?.startsWith(prefix)
    ? ref.slice(prefix.length)
    : event === 'workflow_dispatch' && ref === 'refs/heads/main' ? requested : undefined;
  if (!version || /\s/.test(version) || !semver.test(version) || version.length > 128) {
    throw new Error(`Publish requires a ${collector}-v<semver> tag or a main dispatch with semver (no build metadata).`);
  }
  return version;
}

export async function requireUnusedVersion(collector, version, username, password, fetcher = fetch) {
  imageFor(collector);
  if (!username || !password) throw new Error('Required CI registry credentials are missing.');
  if (!version || /\s/.test(version) || !semver.test(version) || version.length > 128) throw new Error('Invalid image version.');
  let response;
  try {
    response = await fetcher(`https://${registry}/v2/hvo-${collector}/manifests/${version}`, {
      method: 'HEAD', redirect: 'error', signal: AbortSignal.timeout(30000),
      headers: {
        Authorization: `Basic ${Buffer.from(`${username}:${password}`).toString('base64')}`,
        Accept: 'application/vnd.oci.image.index.v1+json, application/vnd.docker.distribution.manifest.list.v2+json, application/vnd.oci.image.manifest.v1+json, application/vnd.docker.distribution.manifest.v2+json'
      }
    });
  } catch { throw new Error('Registry version check failed; no publication allowed.'); }
  if (response.status === 200) throw new Error('Image version already exists; choose a new version.');
  if (response.status !== 404) throw new Error(`Registry version check returned HTTP ${response.status}; no publication allowed.`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try {
    const collector = process.argv[2];
    imageFor(collector);
    switch (process.argv[3]) {
      case 'version': console.log(`version=${versionFor(collector, process.env)}`); break;
      case 'available': await requireUnusedVersion(collector, process.env.VERSION, process.env.HVO_CONTAINER_REGISTRY_USERNAME, process.env.HVO_CONTAINER_REGISTRY_PASSWORD); break;
      case 'manifest': verifyManifest(JSON.parse(await readFile(process.argv[4], 'utf8')), process.env.DIGEST); break;
      default: throw new Error('Expected collector followed by version, available or manifest.');
    }
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}
