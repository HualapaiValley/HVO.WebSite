// Release input and registry readback checks. Never print credentials or HTTP bodies.
import { readFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';

export const IMAGE = 'registry.hualapaivalleyobservatory.org/hvo-davis';
const numeric = '(?:0|[1-9][0-9]*)';
const identifier = `(?:${numeric}|[0-9]*[A-Za-z-][0-9A-Za-z-]*)`;
const semver = new RegExp(`^${numeric}\\.${numeric}\\.${numeric}(?:-${identifier}(?:\\.${identifier})*)?$`);

export function versionFor({ GITHUB_EVENT_NAME: event, GITHUB_REF: ref, REQUESTED_VERSION: requested }) {
  const version = event === 'push' && ref?.startsWith('refs/tags/davis-v')
    ? ref.slice('refs/tags/davis-v'.length)
    : event === 'workflow_dispatch' && ref === 'refs/heads/main' ? requested : undefined;
  if (!version || /\s/.test(version) || !semver.test(version) || version.length > 128) {
    throw new Error('Publish requires a davis-v<semver> tag or a main dispatch with semver (no build metadata).');
  }
  return version;
}

export async function requireUnusedVersion(version, username, password, fetcher = fetch) {
  if (!username || !password) throw new Error('Required CI registry credentials are missing.');
  if (!version || /\s/.test(version) || !semver.test(version) || version.length > 128) throw new Error('Invalid image version.');
  let response;
  try {
    response = await fetcher(`https://${IMAGE.split('/')[0]}/v2/hvo-davis/manifests/${version}`, {
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

export function verifyManifest(manifest, digest) {
  if (!/^sha256:[a-f0-9]{64}$/.test(digest ?? '')) throw new Error('Missing or invalid published index digest.');
  if (manifest.schemaVersion !== 2 || ![
    'application/vnd.oci.image.index.v1+json', 'application/vnd.docker.distribution.manifest.list.v2+json'
  ].includes(manifest.mediaType) || manifest.manifests?.length !== 2) throw new Error('Expected a two-platform image index.');
  const platforms = manifest.manifests.map(entry => {
    if (!/^sha256:[a-f0-9]{64}$/.test(entry.digest ?? '')) throw new Error('Invalid platform digest.');
    return `${entry.platform?.os}/${entry.platform?.architecture}`;
  }).sort();
  if (platforms.join(',') !== 'linux/amd64,linux/arm64') throw new Error('Expected linux/amd64 and linux/arm64.');
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try {
    switch (process.argv[2]) {
      case 'version': console.log(`version=${versionFor(process.env)}`); break;
      case 'available': await requireUnusedVersion(process.env.VERSION, process.env.HVO_CONTAINER_REGISTRY_USERNAME, process.env.HVO_CONTAINER_REGISTRY_PASSWORD); break;
      case 'manifest': verifyManifest(JSON.parse(await readFile(process.argv[3], 'utf8')), process.env.DIGEST); break;
      default: throw new Error('Expected version, available or manifest.');
    }
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}
