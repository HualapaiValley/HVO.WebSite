// This controller runs only from trusted main/base code. Never download or execute PR code here.
import { readFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { GitHub, evaluate, phase, PHASES, DEPTHS, CI_JOBS, ciTitle } from './pr-process.mjs';

export function runBinding(run) {
  const value = /^HVO-PR-CI v1 pr=([1-9]\d*) head=([a-f0-9]{40}) base=([a-f0-9]{40}) merge=([a-f0-9]{40})$/.exec(run.display_title ?? '');
  return value ? { number: Number(value[1]), headSha: value[2], baseSha: value[3], mergeSha: value[4] } : null;
}

async function firstAttemptJobs(api, run) {
  const jobs = [];
  for (let page = 1; page <= 5; page++) {
    const data = await api.request(`/repos/${api.repository}/actions/runs/${run.id}/attempts/1/jobs?per_page=100&page=${page}`);
    if (!Array.isArray(data.jobs)) throw new Error('CI job metadata is unavailable');
    jobs.push(...data.jobs); if (data.jobs.length < 100) return jobs;
  }
  throw new Error('CI job pagination exceeds the bounded admission limit');
}

export async function currentCI(api, pr, reviews = []) {
  const data = await api.request(`/repos/${api.repository}/actions/workflows/ci.yml/runs?event=pull_request_target&head_sha=${pr.head.sha}&per_page=100`);
  const after = Math.max(0, ...reviews.map(x => Date.parse(x.publishedAt) || 0));
  // Actions run/job head_sha associates this target-event run with the raw PR
  // head; it does not identify the trusted workflow checkout. The immutable
  // GitHub-origin title separately binds head/base/merge, and the target workflow
  // admits that tuple using base code before checking out the verified merge.
  const run = data.workflow_runs
    .filter(x => x.event === 'pull_request_target' && x.path === '.github/workflows/ci.yml' && x.head_sha === pr.head.sha && x.display_title === ciTitle(pr) && Date.parse(x.created_at) > after && (!x.pull_requests?.length || x.pull_requests.some(p => p.number === pr.number)))
    .sort((a, b) => Date.parse(b.created_at) - Date.parse(a.created_at))[0];
  if (!run) return null;
  const result = { ...run, qualified: false };
  if (run.run_attempt !== 1) return { ...result, invalid: true, bindingError: 'Only a fresh complete first attempt can qualify; create a new ready/full run' };
  if (run.status !== 'completed' || run.conclusion !== 'success') return result;
  const jobs = await firstAttemptJobs(api, run);
  const complete = CI_JOBS.every(name => {
    const matches = jobs.filter(x => x.name === name);
    return matches.length === 1 && matches[0].run_id === run.id && matches[0].run_attempt === 1 && matches[0].head_sha === pr.head.sha && matches[0].status === 'completed' && matches[0].conclusion === 'success';
  });
  if (!complete) return { ...result, invalid: true, bindingError: 'Current tuple lacks all successful first-attempt admission/build/smoke jobs' };
  // Job pagination may overlap a newly requested rerun. Re-read server metadata
  // before publishing a green candidate status; attempt-one job history alone
  // cannot authorize a run that has since advanced to another attempt.
  const fresh = await api.request(`/repos/${api.repository}/actions/runs/${run.id}`);
  if (fresh.id !== run.id || fresh.run_attempt !== 1 || fresh.status !== 'completed' || fresh.conclusion !== 'success' || fresh.event !== run.event || fresh.path !== run.path || fresh.head_sha !== run.head_sha || fresh.display_title !== run.display_title || fresh.created_at !== run.created_at) return { ...result, invalid: true, bindingError: 'CI run changed while verifying its immutable first-attempt evidence; create a new full run' };
  return { ...result, qualified: true, invalid: false, bindingError: null };
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
  const data = await api.request(`/repos/${api.repository}/actions/workflows/ci.yml/runs?event=pull_request_target&per_page=100`);
  for (const run of data.workflow_runs) {
    if (!['queued', 'in_progress', 'waiting', 'pending', 'requested'].includes(run.status) || (runBinding(run)?.number !== pr.number && !run.pull_requests?.some(x => x.number === pr.number))) continue;
    try {
      await api.request(`/repos/${api.repository}/actions/runs/${run.id}/cancel`, 'POST');
    } catch (error) {
      // A queued draft run can finish between listing and cancellation. Only
      // this conflict plus a fresh, matching terminal record satisfies cleanup.
      if (error.status !== 409) throw error;
      const fresh = await api.request(`/repos/${api.repository}/actions/runs/${run.id}`);
      if (fresh.id !== run.id || fresh.status !== 'completed') throw error;
    }
  }
}

export async function reconcile(api, number, action = '') {
  const snapshot = await api.snapshot(number);
  const { pr } = snapshot;
  const evidence = evaluate(snapshot);
  if (pr.state === 'open' && !snapshot.mergeBinding?.verified) { evidence.errors.push('Current GitHub head/base/tested-merge binding is unavailable'); evidence.eligible = false; }
  const ci = pr.state === 'open' ? await currentCI(api, pr, evidence.reviews) : null;
  let next = phase({ pr, evidence, ci, action });
  // Source synchronization always returns to draft, even if someone pre-published matching JSON.
  const ciFailure = ci?.invalid || (ci?.status === 'completed' && (ci.conclusion !== 'success' || !ci.qualified));
  const invalid = pr.state === 'open' && (action === 'synchronize' || action === 'converted_to_draft' || (action === 'opened' && !pr.draft) || !evidence.eligible || (ciFailure && action !== 'ready_for_review'));
  const fresh = await api.pull(number);
  if (fresh.head.sha !== pr.head.sha || fresh.base.sha !== pr.base.sha || fresh.merge_commit_sha !== pr.merge_commit_sha || fresh.state !== pr.state || fresh.draft !== pr.draft || fresh.body !== pr.body) throw new Error('Candidate changed during metadata reconciliation; retry the current event');
  if (invalid) await returnDraft(api, pr);
  if (action === 'opened') next = 'workflow:draft';
  await replacePhase(api, pr, next, evidence.depth);
  if (pr.state === 'open') {
    await api.request(`/repos/${api.repository}/statuses/${pr.head.sha}`, 'POST', {
      context: 'Review Evidence', state: evidence.eligible && !invalid && !pr.draft ? 'success' : 'failure',
      description: evidence.eligible && !invalid && !pr.draft ? 'Current-source independent review verified' : 'Draft, stale, incomplete or failed candidate; inspect PR process evidence',
      target_url: pr.html_url,
    });
    for (const context of ['build-and-test', 'docker-smoke']) await api.request(`/repos/${api.repository}/statuses/${pr.head.sha}`, 'POST', {
      context,
      state: invalid || pr.draft ? 'failure' : (ci?.qualified && action !== 'ready_for_review' ? 'success' : 'pending'),
      description: invalid || pr.draft ? 'Candidate is draft/stale/incomplete or CI is invalid' : (ci?.qualified && action !== 'ready_for_review' ? 'Exact head/base/merge passed a fresh complete CI attempt' : 'Awaiting current reviewed head/base/merge CI'),
      target_url: ci?.html_url ?? pr.html_url,
    });
  }
  if (invalid && !pr.draft) {
    const run = ci ? `CI: ${ci.html_url} (${ci.status}/${ci.conclusion ?? 'pending'}).` : '';
    const detail = action === 'synchronize' ? 'Source changed; request current-source correction/synchronization review.' : evidence.errors.join('\n- ') || ci?.bindingError || 'Standard CI did not succeed.';
    const body = `<!-- hvo-pr-state:${pr.head.sha}:${pr.base.sha}:${next}:${ci?.id ?? 'none'} -->\nReturned to draft and ${next}.\n\n${run}\n\n- ${detail}\n\nCorrect/diagnose, update local evidence, obtain independent verification, then mark ready using your normal GitHub account. This controller never grants merge authority.`;
    if (!snapshot.comments.some(x => x.user.login === 'github-actions[bot]' && x.body === body)) await api.request(`/repos/${api.repository}/issues/${number}/comments`, 'POST', { body });
  }
  return { number, phase: next, eligible: evidence.eligible, errors: evidence.errors, rejectedPublications: evidence.rejectedPublications ?? [], ci: ci?.id ?? null };
}

async function numbersForEvent(api, event, name) {
  if (event.pull_request?.number) return [event.pull_request.number];
  if (event.issue?.pull_request) return [event.issue.number];
  if (name === 'workflow_run') {
    if (!['pull_request', 'pull_request_target'].includes(event.workflow_run.event)) return [];
    if (event.workflow_run.event === 'pull_request_target' && runBinding(event.workflow_run)) return [runBinding(event.workflow_run).number];
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
