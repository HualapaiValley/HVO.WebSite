// This controller runs only from trusted main/base code. Never download or execute PR code here.
import { readFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { GitHub, evaluate, phase, PHASES, DEPTHS } from './pr-process.mjs';

export async function currentCI(api, pr, reviews = []) {
  const data = await api.request(`/repos/${api.repository}/actions/workflows/ci.yml/runs?event=pull_request&head_sha=${pr.head.sha}&per_page=100`);
  const after = Math.max(0, ...reviews.map(x => Date.parse(x.publishedAt) || 0));
  // The runner reports PR head SHA, while the gate summary records the base and tested merge SHA.
  return data.workflow_runs
    .filter(x => x.head_sha === pr.head.sha && x.pull_requests?.some(p => p.number === pr.number) && Date.parse(x.run_started_at ?? x.created_at) >= after)
    .sort((a, b) => Date.parse(b.run_started_at ?? b.created_at) - Date.parse(a.run_started_at ?? a.created_at))[0] ?? null;
}

export async function replacePhase(api, pr, next, depth) {
  const present = new Set(pr.labels.map(x => x.name));
  for (const label of PHASES) if (label !== next && present.has(label)) await api.request(`/repos/${api.repository}/issues/${pr.number}/labels/${encodeURIComponent(label)}`, 'DELETE');
  if (!present.has(next)) await api.request(`/repos/${api.repository}/issues/${pr.number}/labels`, 'POST', { labels: [next] });
  if (DEPTHS.includes(depth)) {
    for (const value of DEPTHS) if (value !== depth && present.has(`review:${value}`)) await api.request(`/repos/${api.repository}/issues/${pr.number}/labels/${encodeURIComponent(`review:${value}`)}`, 'DELETE');
    if (!present.has(`review:${depth}`)) await api.request(`/repos/${api.repository}/issues/${pr.number}/labels`, 'POST', { labels: [`review:${depth}`] });
  }
}

export async function returnDraft(api, pr) {
  if (!pr.draft) await api.graphql('mutation($id:ID!){convertPullRequestToDraft(input:{pullRequestId:$id}){pullRequest{isDraft}}}', { id: pr.node_id });
  const data = await api.request(`/repos/${api.repository}/actions/workflows/ci.yml/runs?event=pull_request&branch=${encodeURIComponent(pr.head.ref)}&per_page=100`);
  for (const run of data.workflow_runs) {
    if (!['queued', 'in_progress', 'waiting', 'pending', 'requested'].includes(run.status) || !run.pull_requests?.some(x => x.number === pr.number)) continue;
    await api.request(`/repos/${api.repository}/actions/runs/${run.id}/cancel`, 'POST');
  }
}

export async function reconcile(api, number, action = '') {
  const snapshot = await api.snapshot(number);
  const { pr } = snapshot;
  const evidence = evaluate(snapshot);
  const ci = pr.state === 'open' ? await currentCI(api, pr, evidence.reviews) : null;
  let next = phase({ pr, evidence, ci, action });
  // Source synchronization always returns to draft, even if someone pre-published matching JSON.
  const invalid = pr.state === 'open' && (action === 'synchronize' || action === 'converted_to_draft' || (action === 'opened' && !pr.draft) || !evidence.eligible || (ci?.status === 'completed' && ci.conclusion !== 'success' && action !== 'ready_for_review'));
  const fresh = await api.pull(number);
  if (fresh.head.sha !== pr.head.sha || fresh.base.sha !== pr.base.sha || fresh.state !== pr.state || fresh.draft !== pr.draft || fresh.body !== pr.body) throw new Error('Candidate changed during metadata reconciliation; retry the current event');
  if (invalid) await returnDraft(api, pr);
  if (action === 'opened') next = 'workflow:draft';
  await replacePhase(api, pr, next, evidence.depth);
  if (pr.state === 'open') {
    await api.request(`/repos/${api.repository}/statuses/${pr.head.sha}`, 'POST', {
      context: 'Review Evidence', state: evidence.eligible && !invalid && !pr.draft ? 'success' : 'failure',
      description: evidence.eligible && !invalid && !pr.draft ? 'Current-source independent review verified' : 'Draft, stale, incomplete or failed candidate; inspect PR process evidence',
      target_url: pr.html_url,
    });
  }
  if (invalid && !pr.draft) {
    const run = ci ? `CI: ${ci.html_url} (${ci.status}/${ci.conclusion ?? 'pending'}).` : '';
    const detail = action === 'synchronize' ? 'Source changed; request current-source correction/synchronization review.' : evidence.errors.join('\n- ') || 'Standard CI did not succeed.';
    const body = `<!-- hvo-pr-state:${pr.head.sha}:${pr.base.sha}:${next}:${ci?.id ?? 'none'} -->\nReturned to draft and ${next}.\n\n${run}\n\n- ${detail}\n\nCorrect/diagnose, update local evidence, obtain independent verification, then mark ready using your normal GitHub account. This controller never grants merge authority.`;
    if (!snapshot.comments.some(x => x.user.login === 'github-actions[bot]' && x.body === body)) await api.request(`/repos/${api.repository}/issues/${number}/comments`, 'POST', { body });
  }
  return { number, phase: next, eligible: evidence.eligible, errors: evidence.errors, rejectedPublications: evidence.rejectedPublications ?? [], ci: ci?.id ?? null };
}

async function numbersForEvent(api, event, name) {
  if (event.pull_request?.number) return [event.pull_request.number];
  if (event.issue?.pull_request) return [event.issue.number];
  if (name === 'workflow_run') {
    if (event.workflow_run.event !== 'pull_request') return [];
    if (event.workflow_run.pull_requests?.length) return event.workflow_run.pull_requests.map(x => x.number);
    // Some fork runs omit associations; match immutable head against open PRs, never artifacts.
    return (await api.list(`/repos/${api.repository}/pulls?state=open&base=main`)).filter(x => x.head.sha === event.workflow_run.head_sha).map(x => x.number);
  }
  if (name === 'workflow_dispatch' && event.inputs?.pr_number) return [Number(event.inputs.pr_number)];
  if (name === 'push') return (await api.list(`/repos/${api.repository}/pulls?state=open&base=main`)).map(x => x.number);
  return [];
}

async function main() {
  const api = new GitHub(process.env.GITHUB_REPOSITORY, process.env.GH_TOKEN);
  const event = JSON.parse(await readFile(process.env.GITHUB_EVENT_PATH, 'utf8'));
  for (const number of await numbersForEvent(api, event, process.env.GITHUB_EVENT_NAME)) {
    if (!Number.isInteger(number) || number < 1) throw new Error('Invalid PR number');
    console.log(JSON.stringify(await reconcile(api, number, event.action), null, 2));
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) main().catch(error => { console.error(error.message); process.exitCode = 1; });
