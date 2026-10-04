import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { runInNewContext } from 'node:vm';
import { evaluate, record, phase, check, preflight, GitHub, PHASES, LOCAL_IDS, CI_JOBS, ciTitle } from './pr-process.mjs';
import { reconcile, replacePhase, currentCI, runBinding, returnDraft } from './pr-process-control.mjs';

const examples = JSON.parse(await readFile(new URL('./pr-process-fixture.json', import.meta.url), 'utf8'));
const body = value => `Readable evidence.\n<!-- hvo-pr-process\n${JSON.stringify(value)}\n-->`;
const authorized = publisher => ({ login: publisher, verified: true, permission: 'write' });
function fixture() {
  const values = structuredClone(examples);
  const snapshot = {
    pr: { number: 1, node_id: 'PR_1', body: '', user: { login: values.author.publisher }, head: { sha: values.author.headSha, ref: 'feature/pilot' }, base: { sha: values.author.baseSha, ref: 'main' }, merge_commit_sha: '3'.repeat(40), draft: false, state: 'open', labels: [], html_url: 'https://example.test/pull/1' },
    comments: [], threads: [], files: [{ filename: 'tools/pr-process.mjs' }], mergeBinding: { verified: true, headSha: values.author.headSha, baseSha: values.author.baseSha, mergeSha: '3'.repeat(40) },
  };
  function sync() {
    snapshot.pr.body = body(values.author);
    snapshot.comments = [{ id: 10, body: body(values.review), user: { login: values.review.publisher }, publisherPermission: authorized(values.review.publisher), created_at: '2026-01-01T00:00:00Z' }];
    for (const thread of snapshot.threads) for (const comment of thread.comments) { comment.author ??= { login: 'example-owner' }; comment.publisherPermission ??= authorized(comment.author.login); }
    return snapshot;
  }
  return { values, snapshot, sync };
}
function rejected(state, pattern) { const result = evaluate(state); assert.equal(result.eligible, false); assert.match(result.errors.join('\n'), pattern); }
test('obsolete run cancellation tolerates only a verified terminal conflict', async () => {
  const pr = { number: 1, draft: true };
  const run = { id: 99, status: 'in_progress', pull_requests: [{ number: 1 }] };
  const api = (status, fresh) => ({ repository: 'owner/repo', request: async path => {
    if (path.includes('/runs?')) return { workflow_runs: [run] };
    if (path.endsWith('/cancel')) throw Object.assign(new Error('cancel rejected'), { status });
    if (fresh instanceof Error) throw fresh;
    return fresh;
  } });
  await returnDraft(api(409, { id: 99, status: 'completed' }), pr);
  for (const [status, fresh] of [[409, { id: 99, status: 'in_progress' }], [409, { id: 100, status: 'completed' }], [403, { id: 99, status: 'completed' }], [409, new Error('unavailable')]]) {
    await assert.rejects(returnDraft(api(status, fresh), pr));
  }
});
function withFinding(f) {
  f.values.review.findings = [{ id: 'F1', severity: 'P1', threadId: 'PRRT_1', disposition: 'CORRECTED' }];
  f.snapshot.threads = [{ id: 'PRRT_1', isResolved: true, comments: [{ body: body(f.values.finding), author: { login: 'example-owner' } }, { body: body(f.values.verification), author: { login: 'example-owner' } }] }];
  return f.sync();
}
function draftPreflightFixture() {
  const f = fixture(); f.sync(); f.snapshot.pr.draft = true; f.snapshot.pr.title = 'chore(workflow): standardize issue and PR handling (#394)';
  function sync() {
    f.snapshot.pr.body = `${['Purpose', 'Work completed', 'Authorship and provenance', 'Acceptance evidence', 'Local validation', 'Review requirements', 'Risks and follow-up'].map(x => `## ${x}\nConcrete pilot preparation.\n`).join('\n')}\nCloses #394\n${body(f.values.author)}`;
    return f.snapshot;
  }
  const calls = [];
  const api = { repository: 'test/repo', pull: async number => { calls.push(['pull', number]); return f.snapshot.pr; }, request: async path => { calls.push(['request', path]); return { number: 394 }; }, snapshot: async () => { throw new Error('Preflight must not fetch reviews or threads'); } };
  return { ...f, sync, api, calls };
}

test('bounded preflight accepts a realistic draft with blocked/failing preparation evidence', async () => {
  const f = draftPreflightFixture(); f.values.author.validation[0].result = 'blocked'; f.values.author.validation[0].reason = 'Pinned SDK is unavailable locally; validation route being prepared'; f.sync(); assert.equal((await preflight(f.api, 1)).eligible, true); assert.equal(f.calls.length, 2); f.values.author.validation[0].result = 'fail'; f.sync(); assert.equal((await preflight(f.api, 1)).eligible, true);
});
test('preflight reports missing sections and wrong linked issue without requesting full review', async () => {
  const f = draftPreflightFixture(); f.sync(); f.snapshot.pr.body = f.snapshot.pr.body.replace('## Acceptance evidence', '## Miscellaneous').replace('Closes #394', 'Closes #395'); const result = await preflight(f.api, 1); assert.equal(result.eligible, false); assert.match(result.errors.join('\n'), /Acceptance evidence/); assert.match(result.errors.join('\n'), /#394/); assert.equal(f.calls.length, 2);
});
test('preflight rejects a nonexistent issue or PR masquerading as its tracking issue', async () => {
  for (const mode of ['missing', 'pr']) { const f = draftPreflightFixture(); f.sync(); f.api.request = async () => { if (mode === 'missing') throw new Error('404'); return { number: 394, pull_request: {} }; }; const result = await preflight(f.api, 1); assert.equal(result.eligible, false); assert.match(result.errors.join('\n'), /could not be verified|not a PR/); }
});
test('preflight rejects stale raw head/base and forged author publication', async () => {
  for (const patch of [{ headSha: '3'.repeat(40) }, { baseSha: '3'.repeat(40) }, { publisher: 'forged-publisher' }]) { const f = draftPreflightFixture(); Object.assign(f.values.author, patch); f.sync(); const result = await preflight(f.api, 1); assert.equal(result.eligible, false); assert.match(result.errors.join('\n'), /current raw head|actual PR creator/); }
});
test('preflight rejects unsupported title and malformed provenance types with concrete diagnostics', async () => {
  const f = draftPreflightFixture(); f.sync(); f.snapshot.pr.title = 'Update workflow'; const invalidTitle = await preflight(f.api, 1); assert.match(invalidTitle.errors.join('\n'), /Title must be/); assert.equal(f.calls.length, 1);
  const g = draftPreflightFixture(); g.values.author.contributors = { some: 'not-a-function' }; g.sync(); assert.match((await preflight(g.api, 1)).errors.join('\n'), /contributor roster/);
});

test('same authenticated account, distinct session and underlying model passes', () => assert.equal(evaluate(fixture().sync()).eligible, true));
test('untrusted public approval cannot authorize CI or veto an authorized review', () => {
  const f = fixture(); f.sync(); f.snapshot.comments[0].publisherPermission = { login: 'example-owner', verified: true, permission: 'read' }; const rejected = evaluate(f.snapshot); assert.equal(rejected.eligible, false); assert.match(rejected.rejectedPublications.join('\n'), /not verified with repository write/);
  f.snapshot.comments.push({ ...f.snapshot.comments[0], id: 11, publisherPermission: authorized('example-owner') }); assert.equal(evaluate(f.snapshot).eligible, true);
  f.snapshot.comments[0].body = body({ ...f.values.review, verdict: 'CHANGES_REQUIRED', publisherPermission: { verified: true, permission: 'admin' } }); assert.equal(evaluate(f.snapshot).eligible, true);
});
test('publisher eligibility must match actual authenticated account; unknown and bot type are not trust', () => {
  for (const metadata of [undefined, { login: 'example-owner', verified: false, permission: 'unknown' }, { login: 'different-account', verified: true, permission: 'write' }, { login: 'example-owner', verified: true, permission: 'read', role: 'triage' }]) { const f = fixture(); f.sync(); f.snapshot.comments[0].publisherPermission = metadata; f.snapshot.comments[0].user.type = 'Bot'; assert.equal(evaluate(f.snapshot).eligible, false); }
  const f = fixture(); f.sync(); f.snapshot.comments[0].publisherPermission = { login: 'example-owner', verified: true, permission: 'admin' }; assert.equal(evaluate(f.snapshot).eligible, true);
});
test('verification trust comes from API metadata and cannot be forged inside its record', () => {
  const f = fixture(); withFinding(f); const reply = f.snapshot.threads[0].comments[1]; reply.publisherPermission = { login: 'example-owner', verified: true, permission: 'read' }; reply.body = body({ ...f.values.verification, publisherPermission: { verified: true, permission: 'write' } }); rejected(f.snapshot, /terminal verification/); reply.publisherPermission = authorized('example-owner'); assert.equal(evaluate(f.snapshot).eligible, true);
  f.snapshot.threads[0].comments.push({ ...reply, publisherPermission: { login: 'example-owner', verified: false, permission: 'unknown' }, body: body({ ...f.values.verification, disposition: 'STILL_OPEN' }) }); assert.equal(evaluate(f.snapshot).eligible, true);
});
test('resolved external threads do not create schema vetoes; unresolved conversations still block', () => {
  const f = fixture(); f.sync(); f.snapshot.threads = [{ id: 'outside', isResolved: true, comments: [{ body: 'An external concern', author: { login: 'outside-user' }, publisherPermission: { login: 'outside-user', verified: true, permission: 'read' } }] }]; assert.equal(evaluate(f.snapshot).eligible, true); f.snapshot.threads[0].isResolved = false; rejected(f.snapshot, /unresolved/);
});
test('permission metadata lookups are cached and fail closed on inaccessible or malformed endpoint', async () => {
  let calls = 0;
  const api = new GitHub('test/repo', 'test-token', async () => { calls++; return { ok: true, status: 200, json: async () => ({ permission: 'write', role_name: 'maintain', user: { login: 'example-owner' } }) }; });
  assert.equal((await api.publisherPermission('example-owner')).permission, 'write'); assert.equal((await api.publisherPermission('EXAMPLE-OWNER')).verified, true); assert.equal(calls, 1);
  const denied = new GitHub('test/repo', 'test-token', async () => ({ ok: false, status: 403, text: async () => 'Resource not accessible by integration' })); const inaccessible = await denied.publisherPermission('example-owner'); assert.equal(inaccessible.verified, false); assert.match(inaccessible.reason, /403/);
  const malformed = new GitHub('test/repo', 'test-token', async () => ({ ok: true, status: 200, json: async () => ({ permission: 'write', role_name: 'maintain' }) })); assert.equal((await malformed.publisherPermission('example-owner')).verified, false);
});
test('native COMMENT review from the same account uses independent session evidence', () => {
  const f = fixture(); f.sync(); Object.assign(f.snapshot.comments[0], { publication: { type: 'native-review', id: 100, state: 'COMMENTED', commitSha: f.snapshot.pr.head.sha } }); const result = evaluate(f.snapshot); assert.equal(result.eligible, true); assert.equal(result.reviews[0].publication.type, 'native-review'); assert.equal(result.reviews[0].publishedAt, '2026-01-01T00:00:00Z');
});
test('stale native commit, pending/dismissed state and publisher mismatch cannot approve', () => {
  for (const mode of ['stale', 'pending', 'dismissed', 'publisher']) {
    const f = fixture(); f.sync(); f.snapshot.comments[0].publication = { type: 'native-review', id: 100, state: mode === 'pending' ? 'PENDING' : mode === 'dismissed' ? 'DISMISSED' : 'COMMENTED', commitSha: mode === 'stale' ? '3'.repeat(40) : f.snapshot.pr.head.sha }; if (mode === 'publisher') f.snapshot.comments[0].user.login = 'other'; rejected(f.snapshot, /stale commit|No current-source|publisher mismatch/);
  }
});
test('native requested-changes state cannot contradict an APPROVE record', () => {
  const f = fixture(); f.sync(); f.snapshot.comments[0].publication = { type: 'native-review', id: 100, state: 'CHANGES_REQUESTED', commitSha: f.snapshot.pr.head.sha }; rejected(f.snapshot, /requests changes/);
});
test('native metadata requires matching head/base and typed coverage evidence', () => {
  for (const patch of [{ headSha: '3'.repeat(40) }, { baseSha: '3'.repeat(40) }, { coverage: {} }, { findings: {} }]) {
    const f = fixture(); Object.assign(f.values.review, patch); f.sync(); f.snapshot.comments[0].publication = { type: 'native-review', id: 100, state: 'COMMENTED', commitSha: f.snapshot.pr.head.sha }; rejected(f.snapshot, /No current-source|Completed coverage/);
  }
});
test('snapshot paginates native reviews and normalizes authenticated source/submission metadata', async () => {
  const f = fixture(); f.sync(); const urls = []; const api = new GitHub('test/repo', 'test-token', async url => {
    urls.push(url); let data;
    if (url.includes('/graphql')) data = { data: { repository: { pullRequest: { reviewThreads: { nodes: [], pageInfo: { hasNextPage: false } } } } } };
    else if (url.includes('/reviews?')) data = url.includes('page=2') ? [{ id: 200, body: body(f.values.review), user: { login: 'example-owner' }, submitted_at: '2026-01-02T00:00:00Z', commit_id: f.snapshot.pr.head.sha, state: 'COMMENTED', html_url: 'https://example.test/pull/1#pullrequestreview-200' }] : Array(100).fill({ id: 1, body: '', state: 'COMMENTED' });
    else if (url.includes('/comments?')) data = [{ id: 200, body: 'General discussion', user: { login: 'example-owner' }, created_at: '2026-01-01T00:00:00Z' }];
    else if (url.includes('/files?')) data = f.snapshot.files;
    else if (url.includes('/git/commits/')) data = { sha: f.snapshot.pr.merge_commit_sha, parents: [{ sha: f.snapshot.pr.base.sha }, { sha: f.snapshot.pr.head.sha }] };
    else if (url.includes('/collaborators/')) data = { permission: 'write', role_name: 'maintain', user: { login: 'example-owner' } };
    else data = f.snapshot.pr;
    return { ok: true, status: 200, json: async () => data };
  });
  const snapshot = await api.snapshot(1); assert.equal(urls.filter(x => x.includes('/reviews?')).length, 2); const native = snapshot.comments.find(x => x.id === 'review:200'); assert.equal(native.created_at, '2026-01-02T00:00:00Z'); assert.equal(native.updated_at, undefined); assert.equal(native.publication.commitSha, f.snapshot.pr.head.sha); assert.equal(native.user.login, 'example-owner'); assert.equal(native.publisherPermission.permission, 'write'); assert.equal(urls.filter(x => x.includes('/collaborators/')).length, 1); assert.ok(snapshot.comments.some(x => x.id === 'comment:200')); assert.equal(evaluate(snapshot).eligible, true);
});
test('missing author record cannot approve', () => rejected({ ...fixture().sync(), pr: { ...fixture().snapshot.pr, body: 'Just prose' } }, /missing/));
test('head and base changes invalidate source evidence independently', () => {
  for (const key of ['head', 'base']) { const f = fixture(); f.sync(); f.snapshot.pr[key].sha = '3'.repeat(40); rejected(f.snapshot, /current head\/base|current-source/); }
});
test('same implementation session cannot self-review with another model', () => { const f = fixture(); f.values.review.identity.session = f.values.author.identity.session; rejected(f.sync(), /not independent/); });
test('host, instance and workspace metadata are required but unknown origin is allowed', () => {
  for (const key of ['host', 'instance', 'workspace']) { const f = fixture(); delete f.values.review.identity[key]; rejected(f.sync(), new RegExp(`review.identity.${key}`)); }
  const f = fixture(); f.values.review.identity.host = f.values.review.identity.instance = 'unknown'; assert.equal(evaluate(f.sync()).eligible, true);
});
test('known host/instance origin distinguishes reused session IDs; unknown origin is conservative', () => {
  const f = fixture(); f.values.review.identity.session = f.values.author.identity.session; f.values.review.identity.host = 'other-host'; assert.equal(evaluate(f.sync()).eligible, true);
  f.values.review.identity.host = f.values.author.identity.host; f.values.review.identity.instance = 'other-instance'; assert.equal(evaluate(f.sync()).eligible, true);
  f.values.review.identity.instance = 'unknown'; rejected(f.sync(), /not independent/);
});
test('two known reviewer origins can reuse a session ID in an explicit panel', () => {
  const f = fixture(); f.values.author.reviewPolicy.minReviewers = 2; f.sync(); f.snapshot.comments.push({ ...f.snapshot.comments[0], id: 11, body: body({ ...f.values.review, identity: { ...f.values.review.identity, host: 'other-review-host' } }) }); assert.equal(evaluate(f.snapshot).eligible, true);
  f.snapshot.comments[1].body = body({ ...f.values.review, identity: { ...f.values.review.identity, instance: 'unknown' } }); rejected(f.snapshot, /Need 2/);
});
test('model separation considers every implementation contributor', () => { const f = fixture(); f.values.author.contributors.push({ ...f.values.review.identity, session: 'second-implementation' }); rejected(f.sync(), /not independent/); });
test('model switches within an implementation session remain in the contributor roster', () => {
  const f = fixture(); f.values.author.contributors.push({ ...f.values.author.identity, model: 'second-implementation-model' }); assert.equal(evaluate(f.sync()).eligible, true); f.values.author.contributors.push({ ...f.values.author.identity }); rejected(f.sync(), /tuples must be unique/);
});
test('optional provider separation considers all implementation providers and rejects unknown', () => {
  const f = fixture(); f.values.author.reviewPolicy.differentProvider = true; rejected(f.sync(), /not independent/); f.values.review.identity.provider = 'different-provider'; assert.equal(evaluate(f.sync()).eligible, true); f.values.author.contributors.push({ ...f.values.author.identity, model: 'second-model', provider: 'unknown' }); rejected(f.sync(), /not independent/);
});
test('explicit primary-only model comparison is honored', () => { const f = fixture(); f.values.author.contributors.push({ ...f.values.review.identity, session: 'second-implementation' }); f.values.author.reviewPolicy.modelComparison = 'primary'; assert.equal(evaluate(f.sync()).eligible, true); });
test('different account is optional, strict direct publication is authenticated', () => {
  const f = fixture(); f.values.author.reviewPolicy.differentAccount = true; rejected(f.sync(), /not independent/);
  f.values.review.publisher = 'other-reviewer'; f.values.review.identity.account = 'other-reviewer'; assert.equal(evaluate(f.sync()).eligible, true);
  f.values.review.publisher = 'relay-account'; rejected(f.sync(), /direct, authenticated/);
});
test('unknown implementation or reviewer model cannot satisfy strict separation', () => {
  for (const identity of ['identity', 'author']) { const f = fixture(); if (identity === 'identity') f.values.review.identity.model = 'unknown'; else f.values.author.identity.model = f.values.author.contributors[0].model = 'unknown'; rejected(f.sync(), /not independent/); }
});
test('unknown model remains acceptable for auto selection without model separation', () => { const f = fixture(); f.values.author.reviewPolicy.differentModel = false; f.values.review.identity.model = 'unknown'; assert.equal(evaluate(f.sync()).eligible, true); });
test('required selectors enforce requested effort; preferred fallback needs recorded substitution', () => {
  const f = fixture(); const p = f.values.author.reviewPolicy; p.selection = 'required'; p.models = [{ provider: 'example-provider', model: 'review-model', effort: 'high' }]; assert.equal(evaluate(f.sync()).eligible, true);
  p.models[0].effort = 'xhigh'; rejected(f.sync(), /Required review model/); p.selection = 'preferred'; rejected(f.sync(), /substitution/); f.values.review.selectionNote = 'Preferred effort unavailable; permitted different-model fallback selected.'; assert.equal(evaluate(f.sync()).eligible, true);
  p.selection = 'required'; p.models[0].model = 'unknown'; rejected(f.sync(), /known provider\/model/);
});
test('unknown required effort fails strict selector', () => { const f = fixture(); f.values.author.reviewPolicy.selection = 'required'; f.values.author.reviewPolicy.models = [{ provider: 'example-provider', model: 'review-model', effort: 'high' }]; f.values.review.identity.effort = 'provider-managed'; rejected(f.sync(), /Required review model/); });
test('model selectors are one-of by default; explicit all-of requests a panel', () => {
  const f = fixture(); f.values.author.reviewPolicy.selection = 'required'; f.values.author.reviewPolicy.models = [{ provider: 'example-provider', model: 'review-model' }, { provider: 'other-provider', model: 'other-model' }]; delete f.values.author.reviewPolicy.modelMode; assert.equal(evaluate(f.sync()).eligible, true); f.values.author.reviewPolicy.modelMode = 'all-of'; rejected(f.sync(), /Required review model/);
});
test('preferred none and listed-only fallback enforce their explicit choice', () => {
  const f = fixture(); const p = f.values.author.reviewPolicy; p.selection = 'preferred'; p.models = [{ provider: 'preferred', model: 'unavailable-model' }]; p.fallback = 'none'; rejected(f.sync(), /fallback is not authorized/); p.fallback = 'listed-only'; p.fallbackModels = [{ provider: 'permitted', model: 'another-model' }]; f.values.review.selectionNote = 'Preferred route unavailable'; rejected(f.sync(), /outside the explicitly permitted/); p.fallbackModels = [{ provider: 'example-provider', model: 'review-model' }]; assert.equal(evaluate(f.sync()).eligible, true);
});
test('human N/A review is eligible unless an agent model is required', () => {
  const f = fixture(); Object.assign(f.values.review.identity, { provider: 'human', model: 'n/a', effort: 'n/a' }); assert.equal(evaluate(f.sync()).eligible, true); f.values.author.reviewPolicy.selection = 'required'; f.values.author.reviewPolicy.models = [{ provider: 'example-provider', model: 'review-model' }]; rejected(f.sync(), /Required review model/);
});
test('insufficient depth or incomplete coverage cannot approve', () => { const f = fixture(); f.values.review.depth = 'standard'; rejected(f.sync(), /required review depth/); f.values.review.depth = 'deep'; f.values.review.coverageComplete = false; rejected(f.sync(), /Completed coverage/); });
test('author contributor roster and each validation performer are mandatory', () => {
  const f = fixture(); f.values.author.contributors = []; rejected(f.sync(), /contributor roster/);
  const g = fixture(); delete g.values.author.validation[0].performer; rejected(g.sync(), /performer.account/);
});
test('malformed/duplicated metadata and fabricated publisher fail', () => {
  const f = fixture(); f.sync(); f.snapshot.comments[0].body += body(f.values.review); rejected(f.snapshot, /Exactly one/);
  const g = fixture(); g.sync(); g.snapshot.comments[0].user.login = 'unrelated'; g.snapshot.comments[0].publisherPermission = authorized('unrelated'); rejected(g.snapshot, /publisher mismatch/);
  assert.throws(() => record('<!-- hvo-pr-process\n{}\n-->', 'author'), /version 1/);
});
test('malformed record field types return negative evidence without a controller crash', () => {
  for (const patch of [{ coverage: {} }, { findings: {} }, { findings: [null] }, { limitations: [null] }]) { const f = fixture(); Object.assign(f.values.review, patch); assert.equal(evaluate(f.sync()).eligible, false); }
  for (const patch of [{ contributors: [null] }, { validation: [null] }, { reviewPolicy: { ...examples.author.reviewPolicy, models: [null] } }]) { const f = fixture(); Object.assign(f.values.author, patch); assert.equal(evaluate(f.sync()).eligible, false); }
});
test('a later changes-required decision invalidates old approval from that session', () => {
  const f = fixture(); f.sync(); f.snapshot.comments.push({ ...f.snapshot.comments[0], id: 11, created_at: '2026-01-01T00:00:01Z', body: body({ ...f.values.review, verdict: 'CHANGES_REQUIRED' }) }); rejected(f.snapshot, /outstanding changes/);
});
test('editing an older comment to changes-required supersedes a newer-created approval', () => {
  const f = fixture(); f.sync(); f.snapshot.comments[0].body = body({ ...f.values.review, verdict: 'CHANGES_REQUIRED' }); f.snapshot.comments[0].updated_at = '2026-01-03T00:00:00Z'; f.snapshot.comments.push({ ...f.snapshot.comments[0], id: 11, body: body(f.values.review), updated_at: '2026-01-02T00:00:00Z' }); rejected(f.snapshot, /outstanding changes/);
});
test('former-source review does not satisfy or contaminate current review constraints', () => {
  const f = fixture(); f.sync(); f.snapshot.comments.unshift({ id: 9, body: body({ ...f.values.review, headSha: '3'.repeat(40), verdict: 'CHANGES_REQUIRED', identity: { ...f.values.review.identity, session: f.values.author.identity.session, model: 'unknown' } }), user: { login: 'unrelated-old-publisher' }, created_at: '2025-01-01T00:00:00Z' }); assert.equal(evaluate(f.snapshot).eligible, true);
  f.snapshot.comments.pop(); rejected(f.snapshot, /No current-source/);
});
test('distinct reviewers are sessions, not comment count', () => { const f = fixture(); f.values.author.reviewPolicy.minReviewers = 2; f.sync(); f.snapshot.comments.push({ ...f.snapshot.comments[0], id: 11 }); rejected(f.snapshot, /Need 2/); });
test('resolved finding with current independent verification passes', () => assert.equal(evaluate(withFinding(fixture())).eligible, true));
test('resolution alone, stale verification and author verification do not pass', () => {
  for (const mode of ['missing', 'stale', 'author']) {
    const f = fixture(); withFinding(f);
    if (mode === 'missing') f.snapshot.threads[0].comments.pop();
    if (mode === 'stale') { f.values.verification.headSha = '3'.repeat(40); f.snapshot.threads[0].comments[1].body = body(f.values.verification); }
    if (mode === 'author') { f.values.verification.identity = f.values.author.identity; f.snapshot.threads[0].comments[1].body = body(f.values.verification); }
    rejected(f.snapshot, /terminal verification/);
  }
});
test('later independent STILL_OPEN retracts an earlier terminal verification', () => {
  const f = fixture(); withFinding(f); f.snapshot.threads[0].comments.push({ body: body({ ...f.values.verification, disposition: 'STILL_OPEN' }), author: { login: 'example-owner' }, publisherPermission: authorized('example-owner') }); rejected(f.snapshot, /terminal verification/);
});
test('cyclic supersession cannot resolve findings', () => {
  const f = fixture(); f.values.review.findings = ['F1', 'F2'].map(id => ({ id, severity: 'P1', threadId: `PRRT_${id}`, disposition: 'SUPERSEDED' })); f.snapshot.threads = ['F1', 'F2'].map(id => ({ id: `PRRT_${id}`, isResolved: true, comments: [{ body: body({ ...f.values.finding, id }) }, { body: body({ ...f.values.verification, findingId: id, disposition: 'SUPERSEDED', supersededBy: id === 'F1' ? 'F2' : 'F1' }), author: { login: 'example-owner' } }] })); rejected(f.sync(), /finite verified superseding/);
});
test('unresolved, omitted, conflicting and unstructured threads block', () => {
  const f = fixture(); withFinding(f); f.snapshot.threads[0].isResolved = false; rejected(f.snapshot, /unresolved/);
  const g = fixture(); withFinding(g); g.values.review.findings = []; g.sync(); rejected(g.snapshot, /missing or inconsistent/);
  const h = fixture(); h.sync(); h.snapshot.threads = [{ id: 'legacy', isResolved: true, comments: [{ body: 'Possible bug', author: { login: 'reviewer' }, publisherPermission: authorized('reviewer') }] }]; rejected(h.snapshot, /no structured finding/);
});
test('missing or tampered thread roots cannot satisfy a listed finding', () => {
  const f = fixture(); withFinding(f); f.snapshot.threads[0].comments[0].body = 'Removed process marker'; rejected(f.snapshot, /no structured finding|no matching code thread/);
  const g = fixture(); withFinding(g); g.snapshot.threads[0].comments[0].body = body({ ...g.values.finding, severity: 'P2' }); rejected(g.snapshot, /inconsistent/);
});
test('deferral requires explicit author authorization and follow-up', () => {
  const f = fixture(); f.values.finding.severity = 'P2'; f.values.review.findings = [{ id: 'F1', severity: 'P2', threadId: 'PRRT_1', disposition: 'DEFERRED' }]; f.values.verification.disposition = 'DEFERRED'; f.values.verification.followUp = 'https://example.test/issues/2';
  f.snapshot.threads = [{ id: 'PRRT_1', isResolved: true, comments: [{ body: body(f.values.finding) }, { body: body(f.values.verification), author: { login: 'example-owner' } }] }];
  rejected(f.sync(), /terminal verification/); f.values.author.allowedDeferrals = ['F1']; assert.equal(evaluate(f.sync()).eligible, true);
});
test('P0/P1 and marked acceptance/security blockers are not deferrable', () => {
  for (const severity of ['P0', 'P1', 'P2']) {
    const f = fixture(); f.values.finding.severity = severity; f.values.finding.nonDeferrable = true; f.values.review.findings = [{ id: 'F1', severity, threadId: 'PRRT_1', disposition: 'DEFERRED' }]; f.values.verification.disposition = 'DEFERRED'; f.values.verification.followUp = 'https://example.test/issues/2'; f.values.author.allowedDeferrals = ['F1']; f.snapshot.threads = [{ id: 'PRRT_1', isResolved: true, comments: [{ body: body(f.values.finding) }, { body: body(f.values.verification), author: { login: 'example-owner' } }] }]; rejected(f.sync(), /cannot be deferred|terminal verification/);
  }
});
test('draft and mid-check source races reject pipeline admission', async () => {
  const f = fixture(); f.sync(); const api = { snapshot: async () => structuredClone(f.snapshot), pull: async () => ({ ...f.snapshot.pr, head: { sha: '3'.repeat(40) } }) };
  assert.equal((await check(api, 1)).eligible, false);
  api.pull = async () => ({ ...f.snapshot.pr, draft: true }); assert.equal((await check(api, 1)).eligible, false);
  api.pull = async () => f.snapshot.pr; assert.equal((await check(api, 1, { head: 'wrong' })).eligible, false);
});
test('phase selection rejects unsuccessful CI and distinguishes ready/closed outcomes', () => {
  const f = fixture(); const pr = f.sync().pr; const evidence = evaluate(f.snapshot);
  assert.equal(phase({ pr, evidence }), 'workflow:ci');
  assert.equal(phase({ pr, evidence, ci: { status: 'completed', conclusion: 'success', qualified: true } }), 'workflow:ready-to-merge');
  for (const conclusion of ['failure', 'cancelled', 'timed_out', 'skipped']) assert.equal(phase({ pr, evidence, ci: { status: 'completed', conclusion } }), 'workflow:changes-required');
  assert.equal(phase({ pr, evidence, action: 'synchronize' }), 'workflow:review');
  assert.equal(phase({ pr: { ...pr, state: 'closed', merged: true }, evidence }), 'workflow:complete');
  assert.equal(phase({ pr: { ...pr, state: 'closed', merged: false }, evidence }), 'workflow:cancelled');
});
test('label replacement preserves owner/blocked/component overlays', async () => {
  const calls = []; const f = fixture(); const pr = f.sync().pr; pr.labels = ['workflow:draft', 'workflow:review', 'workflow:blocked', 'workflow:in-progress', 'component:web'].map(name => ({ name }));
  await replacePhase({ repository: 'test/repo', request: async (...args) => calls.push(args) }, pr, 'workflow:ci', 'deep');
  assert.equal(calls.filter(x => x[1] === 'DELETE').length, 2); assert.ok(calls.every(x => !/blocked|in-progress|component/.test(x[0]))); assert.ok(PHASES.includes(calls.find(x => x[1] === 'POST')[2].labels[0]));
});
test('metadata controller source invalidation converts draft and cancels active PR run', async () => {
  const f = fixture(); f.sync(); const calls = [];
  const api = { repository: 'test/repo', snapshot: async () => f.snapshot, pull: async () => f.snapshot.pr, graphql: async (...args) => calls.push(['graphql', ...args]), request: async (...args) => { calls.push(args); return args[0].includes('/runs?') ? { workflow_runs: [{ id: 99, status: 'in_progress', head_sha: f.snapshot.pr.head.sha, pull_requests: [{ number: 1 }], created_at: '2026-01-02T00:00:00Z' }] } : {}; } };
  const result = await reconcile(api, 1, 'synchronize'); assert.equal(result.phase, 'workflow:review'); assert.ok(calls.some(x => x[0] === 'graphql')); assert.ok(calls.some(x => x[0].endsWith('/99/cancel'))); assert.equal(calls.find(x => x[0].includes('/statuses/'))[2].state, 'failure');
});
test('metadata controller does not mutate if candidate moves during reconciliation', async () => {
  const f = fixture(); f.sync(); const calls = []; const api = { repository: 'test/repo', snapshot: async () => f.snapshot, pull: async () => ({ ...f.snapshot.pr, base: { sha: '3'.repeat(40) } }), request: async (...args) => { calls.push(args); return { workflow_runs: [] }; } };
  await assert.rejects(() => reconcile(api, 1), /changed during/); assert.equal(calls.length, 1);
});
test('manual draft transition cancels active standard CI even if review remains valid', async () => {
  const f = fixture(); f.sync(); f.snapshot.pr.draft = true; const calls = [];
  const api = { repository: 'test/repo', snapshot: async () => f.snapshot, pull: async () => f.snapshot.pr, request: async (...args) => { calls.push(args); return args[0].includes('/runs?') ? { workflow_runs: [{ id: 99, status: 'in_progress', head_sha: f.snapshot.pr.head.sha, pull_requests: [{ number: 1 }], created_at: '2026-01-02T00:00:00Z' }] } : {}; } };
  const result = await reconcile(api, 1, 'converted_to_draft'); assert.equal(result.phase, 'workflow:review'); assert.ok(calls.some(x => x[0].endsWith('/99/cancel')));
});
test('API pagination fails closed instead of silently omitting evidence', async () => {
  const api = new GitHub('test/repo', 'test-token', async () => ({ ok: true, status: 200, json: async () => Array(100).fill({}) })); await assert.rejects(() => api.list('/comments'), /Pagination limit/);
});
function ciFixture() {
  const f = fixture(); f.sync();
  // Observed pull_request_target REST metadata associates both runs and jobs
  // with the PR head, independently of GITHUB_SHA and the trusted base checkout.
  const run = { id: 99, event: 'pull_request_target', path: '.github/workflows/ci.yml', display_title: ciTitle(f.snapshot.pr), head_sha: f.snapshot.pr.head.sha, pull_requests: [{ number: 1 }], created_at: '2026-01-01T00:00:01Z', run_attempt: 1, status: 'completed', conclusion: 'success', html_url: 'https://example.test/actions/runs/99' };
  const jobs = CI_JOBS.map(name => ({ name, run_id: 99, run_attempt: 1, head_sha: f.snapshot.pr.head.sha, status: 'completed', conclusion: 'success' }));
  const calls = []; const api = { repository: 'test/repo', request: async path => {
    calls.push(path);
    if (path.includes('/attempts/1/jobs')) return { jobs };
    if (path.endsWith(`/actions/runs/${run.id}`)) return run;
    const head = new URL(`https://api.github.com${path}`).searchParams.get('head_sha');
    return { workflow_runs: !head || head === run.head_sha ? [run] : [] };
  } };
  return { ...f, run, jobs, api, calls };
}
test('CI accepts an immutable GitHub-origin current tuple with all first-attempt jobs', async () => {
  const f = ciFixture(); const result = await currentCI(f.api, f.snapshot.pr, [{ publishedAt: '2026-01-01T00:00:00Z' }]); assert.equal(result.qualified, true); assert.ok(f.calls[0].includes(`event=pull_request_target&head_sha=${f.snapshot.pr.head.sha}`)); assert.equal(runBinding(f.run).mergeSha, f.snapshot.pr.merge_commit_sha);
});
test('PR396 F1 rejects target/merge associations even if the server returns them', async () => {
  for (const sha of ['base', 'merge']) {
    const f = ciFixture(); f.run.head_sha = sha === 'base' ? f.snapshot.pr.base.sha : f.snapshot.pr.merge_commit_sha;
    const api = { ...f.api, request: async path => path.includes('/runs?') ? { workflow_runs: [f.run] } : f.api.request(path) };
    assert.equal(await currentCI(api, f.snapshot.pr), null, sha);
    f.run.head_sha = f.snapshot.pr.head.sha; f.jobs[0].head_sha = sha === 'base' ? f.snapshot.pr.base.sha : f.snapshot.pr.merge_commit_sha;
    assert.equal((await currentCI(f.api, f.snapshot.pr)).invalid, true, sha);
  }
});
test('PR396 F1 current failed/cancelled CI returns an eligible ready PR to draft', async () => {
  for (const conclusion of ['failure', 'cancelled', 'timed_out', 'skipped']) {
    const f = ciFixture(); f.run.conclusion = conclusion; const calls = [];
    const api = { ...f.api, snapshot: async () => f.snapshot, pull: async () => f.snapshot.pr, graphql: async (...args) => calls.push(['graphql', ...args]), request: async (...args) => { if (args[1]) { calls.push(args); return {}; } return f.api.request(args[0]); } };
    const result = await reconcile(api, 1);
    assert.equal(result.eligible, true); assert.equal(result.ci, f.run.id); assert.equal(result.phase, 'workflow:changes-required', conclusion);
    assert.ok(calls.some(x => x[0] === 'graphql' && x[1].includes('convertPullRequestToDraft')));
    const statuses = calls.filter(x => x[0].includes(`/statuses/${f.snapshot.pr.head.sha}`));
    for (const name of ['Review Evidence', 'build-and-test', 'docker-smoke']) assert.equal(statuses.find(x => x[2].context === name)[2].state, 'failure');
    assert.ok(calls.some(x => x[0].endsWith('/comments') && x[2].body.includes(f.run.html_url)));
  }
});
test('F1/F2 trusted workflow admission cannot use candidate code and predicates stop on cancellation', () => {
  const workflow = JSON.parse(execFileSync('ruby', ['-rjson', '-ryaml', '-e', 'v=YAML.load_file(".github/workflows/ci.yml"); v["on"]=v.delete(true) if v.key?(true); puts JSON.generate(v)'], { encoding: 'utf8' }));
  assert.ok(workflow.on.pull_request_target); assert.equal(workflow.on.pull_request, undefined);
  const gate = workflow.jobs['review-evidence']; assert.equal(gate.steps[0].with.ref, '${{ github.event.pull_request.base.sha }}'); assert.equal(gate.steps[0].with['persist-credentials'], false); assert.ok(Object.values(workflow.permissions).every(x => x === 'read'));
  for (const name of ['plan']) {
    const job = workflow.jobs[name];
    const candidate = job.steps.find(step => step.with?.path === 'candidate');
    assert.ok(candidate.with.ref.includes('needs.review-evidence.outputs.merge'));
    assert.equal(candidate.with['persist-credentials'], false);
    assert.ok(job.steps[0].with.ref.includes('github.event.pull_request.base.sha'));
    assert.equal(job.steps[0].with['persist-credentials'], false);
    assert.equal(job.steps.find(step => step.id === 'plan').run, 'node ../trusted/tools/ci-source.mjs');
    const expr = job.if.slice(3, -2).replaceAll('needs.review-evidence', 'needs.review_evidence');
    const context = { github: { event_name: 'pull_request_target', event: { pull_request: { draft: false } }, run_attempt: 1 }, needs: { review_evidence: { result: 'success', outputs: { eligible: 'true' } } }, cancelled: () => false };
    assert.equal(runInNewContext(expr, context), true); context.cancelled = () => true; assert.equal(runInNewContext(expr, context), false);
    context.cancelled = () => false; context.github.event_name = 'push'; context.needs.review_evidence.result = 'skipped'; assert.equal(runInNewContext(expr, context), true);
    context.github.event_name = 'schedule'; assert.equal(runInNewContext(expr, context), true);
    context.github.event_name = 'pull_request_target'; context.needs.review_evidence.result = 'success'; context.github.run_attempt = 2; assert.equal(runInNewContext(expr, context), false);
    context.github.run_attempt = 1; context.github.event.pull_request.draft = true; assert.equal(runInNewContext(expr, context), false);
  }
  for (const name of ['validation', 'home-assistant', 'sql-server', 'browser', 'operations', 'docker-smoke']) {
    const job = workflow.jobs[name];
    assert.equal(job.needs, 'plan');
    assert.ok(job.if.includes("needs.plan.result == 'success'"));
    assert.ok(job.if.includes('!cancelled()'));
    const candidate = job.steps.find(step => step.with?.path === 'candidate');
    assert.equal(candidate.with.ref, '${{ fromJSON(needs.plan.outputs.plan).source.merge }}');
    assert.equal(candidate.with['persist-credentials'], false);
    assert.ok(job.steps.find(step => step.run)?.run.startsWith('node ../trusted/tools/ci-run.mjs '));
  }
  const aggregate = workflow.jobs['build-and-test'];
  assert.deepEqual(aggregate.needs, ['plan', 'validation', 'home-assistant', 'sql-server', 'browser', 'operations']);
  assert.ok(!aggregate.steps.some(step => step.with?.path === 'candidate'));
  assert.ok(aggregate.steps[0].with.ref.includes('github.event.pull_request.base.sha'));
  assert.equal(aggregate.steps.at(-1).run, 'node trusted/tools/ci-run.mjs aggregate');
  for (const status of ['failure', 'cancelled', 'skipped']) {
    const context = { needs: { plan: { result: status } }, cancelled: () => false };
    assert.equal(runInNewContext(aggregate.if.slice(3, -2), context), false);
  }
});
test('F3 old target/merge and re-run original creation cannot qualify the current candidate', async () => {
  for (const change of ['base', 'merge', 'original-time', 'candidate-workflow', 'wrong-path']) {
    const f = ciFixture();
    if (change === 'base') f.run.display_title = ciTitle({ ...f.snapshot.pr, base: { sha: '4'.repeat(40) } });
    if (change === 'merge') f.run.display_title = ciTitle({ ...f.snapshot.pr, merge_commit_sha: '4'.repeat(40) });
    if (change === 'original-time') { f.run.created_at = '2025-01-01T00:00:00Z'; f.run.run_started_at = '2026-01-02T00:00:00Z'; f.run.run_attempt = 2; }
    if (change === 'candidate-workflow') { f.run.event = 'pull_request'; f.run.head_sha = f.snapshot.pr.head.sha; }
    if (change === 'wrong-path') f.run.path = '.github/workflows/untrusted.yml';
    assert.equal(await currentCI(f.api, f.snapshot.pr, [{ publishedAt: '2026-01-01T00:00:00Z' }]), null, change);
  }
});
test('F3 partial/full reruns and incomplete/skipped required jobs cannot qualify', async () => {
  for (const change of ['attempt', 'missing-gate', 'skipped-smoke', 'wrong-attempt', 'wrong-job-head']) {
    const f = ciFixture(); if (change === 'attempt') f.run.run_attempt = 2; if (change === 'missing-gate') f.jobs.shift(); if (change === 'skipped-smoke') f.jobs[2].conclusion = 'skipped'; if (change === 'wrong-attempt') f.jobs[0].run_attempt = 2; if (change === 'wrong-job-head') f.jobs[0].head_sha = '4'.repeat(40);
    const result = await currentCI(f.api, f.snapshot.pr, [{ publishedAt: '2026-01-01T00:00:00Z' }]); assert.equal(result.qualified, false, change); assert.equal(result.invalid, true, change);
  }
});
test('F3 verified merge parents and immutable expected merge are required for admission', async () => {
  const f = fixture(); f.sync(); const api = { snapshot: async () => f.snapshot, pull: async () => f.snapshot.pr };
  assert.equal((await check(api, 1, { head: f.snapshot.pr.head.sha, base: f.snapshot.pr.base.sha, merge: f.snapshot.pr.merge_commit_sha })).eligible, true);
  assert.equal((await check(api, 1, { merge: '' })).eligible, false); f.snapshot.mergeBinding.verified = false; assert.equal((await check(api, 1)).eligible, false);
  const malformed = new GitHub('test/repo', 'test-token', async () => ({ ok: true, status: 200, json: async () => ({ sha: f.snapshot.pr.merge_commit_sha, parents: [{ sha: '4'.repeat(40) }, { sha: f.snapshot.pr.head.sha }] }) })); assert.equal((await malformed.mergeBinding(f.snapshot.pr)).verified, false);
});
test('F3 a rerun or metadata change during job verification cannot publish green', async () => {
  for (const change of ['attempt', 'status', 'tuple', 'creation']) {
    const f = ciFixture(); const fresh = { ...f.run };
    if (change === 'attempt') fresh.run_attempt = 2;
    if (change === 'status') fresh.status = 'in_progress';
    if (change === 'tuple') fresh.display_title = ciTitle({ ...f.snapshot.pr, base: { sha: '4'.repeat(40) } });
    if (change === 'creation') fresh.created_at = '2025-01-01T00:00:00Z';
    const api = { ...f.api, request: async path => path.endsWith('/actions/runs/99') ? fresh : f.api.request(path) };
    const result = await currentCI(api, f.snapshot.pr, [{ publishedAt: '2026-01-01T00:00:00Z' }]); assert.equal(result.qualified, false, change); assert.equal(result.invalid, true, change);
  }
});
test('F3 controller publishes head statuses only after current tuple jobs qualify', async () => {
  for (const qualified of [true, false]) {
    const f = ciFixture(); if (!qualified) f.run.display_title = ciTitle({ ...f.snapshot.pr, base: { sha: '4'.repeat(40) } }); const calls = [];
    const api = { ...f.api, snapshot: async () => f.snapshot, pull: async () => f.snapshot.pr, request: async (...args) => { if (args[1]) { calls.push(args); return {}; } return f.api.request(args[0]); } };
    const result = await reconcile(api, 1); assert.equal(result.phase, qualified ? 'workflow:ready-to-merge' : 'workflow:ci');
    const statuses = calls.filter(x => x[0].includes(`/statuses/${f.snapshot.pr.head.sha}`)); for (const name of ['build-and-test', 'docker-smoke']) assert.equal(statuses.find(x => x[2].context === name)[2].state, qualified ? 'success' : 'pending');
  }
});
test('F4 same-second native/conversation conflicting decisions fail closed in either order', () => {
  for (const reverse of [false, true]) for (const nativeVerdict of ['APPROVE', 'CHANGES_REQUIRED']) {
    const f = fixture(); f.sync(); const native = { ...f.snapshot.comments[0], id: 'review:100', body: body({ ...f.values.review, verdict: nativeVerdict }), publication: { type: 'native-review', id: 100, state: 'COMMENTED', commitSha: f.snapshot.pr.head.sha } }; const conversation = { ...f.snapshot.comments[0], id: 'comment:200', body: body({ ...f.values.review, verdict: nativeVerdict === 'APPROVE' ? 'CHANGES_REQUIRED' : 'APPROVE' }) }; f.snapshot.comments = reverse ? [conversation, native] : [native, conversation]; rejected(f.snapshot, /Ambiguous same-time/);
    f.snapshot.comments.push({ ...conversation, id: 'comment:300', created_at: '2026-01-01T00:00:01Z', body: body(f.values.review) }); assert.equal(evaluate(f.snapshot).eligible, true);
  }
});
test('F5 mandatory local profile evidence cannot be waived or replaced with post-review checks', () => {
  for (const change of ['missing', 'blocked', 'fail', 'stale', 'ci-stage', 'warning-baseline', 'wrong-sdk']) {
    const f = fixture(); const build = f.values.author.validation[0]; if (change === 'missing') f.values.author.validation.shift(); if (change === 'blocked') { build.result = 'blocked'; build.reason = 'Required zero-warning build unresolved'; build.required = false; } if (change === 'fail') build.result = 'fail'; if (change === 'stale') build.headSha = '4'.repeat(40); if (change === 'ci-stage') build.stage = 'ci'; if (change === 'warning-baseline') build.metrics.warnings = 778; if (change === 'wrong-sdk') build.sdk = '10.0.401'; rejected(f.sync(), /Mandatory local prerequisite/);
  }
});
test('F5 required local checks pass while explicitly pending CI checks remain post-gate', () => {
  const f = fixture(); assert.equal(evaluate(f.sync()).eligible, true); f.values.author.validation = f.values.author.validation.filter(x => x.id !== LOCAL_IDS.process); rejected(f.sync(), /local:pr-process-tests/); f.snapshot.files = [{ filename: 'src/HVO.WebSite.v9/Example.cs' }]; assert.equal(evaluate(f.sync()).eligible, true);
});
test('F6 every preferred reviewer needs an independently permitted model or recorded fallback', () => {
  for (const fallback of ['none', 'listed-only', 'any-eligible']) {
    const f = fixture(); const policy = f.values.author.reviewPolicy; policy.minReviewers = 2; policy.selection = 'preferred'; policy.models = [{ provider: 'example-provider', model: 'review-model' }]; policy.fallback = fallback; policy.fallbackModels = [{ provider: 'example-provider', model: 'permitted-fallback' }]; f.sync(); const other = { ...f.values.review, identity: { ...f.values.review.identity, session: 'second-review', model: 'outside-list' } }; const comment = { ...f.snapshot.comments[0], id: 12, body: body(other) }; f.snapshot.comments.push(comment); assert.equal(evaluate(f.snapshot).eligible, false, fallback);
    other.identity.model = 'permitted-fallback'; other.selectionNote = 'Preferred route unavailable; selected the explicitly permitted fallback'; comment.body = body(other); assert.equal(evaluate(f.snapshot).eligible, fallback !== 'none', fallback);
    other.identity.model = 'review-model'; delete other.selectionNote; comment.body = body(other); assert.equal(evaluate(f.snapshot).eligible, true, fallback);
  }
});

function preferredPanel(fallback, reviewers, models = ['review-model', 'panel-model']) {
  const f = fixture();
  Object.assign(f.values.author.reviewPolicy, { selection: 'preferred', modelMode: 'all-of', minReviewers: 2, fallback, models: models.map(model => ({ provider: 'example-provider', model })), fallbackModels: [{ provider: 'example-provider', model: 'permitted-fallback' }] });
  f.sync();
  f.snapshot.comments = reviewers.map((reviewer, index) => ({ ...f.snapshot.comments[0], id: 10 + index, body: body({ ...f.values.review, ...reviewer, identity: { ...f.values.review.identity, ...reviewer.identity, session: `panel-review-${index}` } }) }));
  return f;
}
test('F7 prohibited panel fallback rejects duplicate A reviewers even with substitution notes', () => {
  for (const selectionNote of [undefined, 'B unavailable; using another A reviewer']) {
    const f = preferredPanel('none', [{ selectionNote }, { selectionNote }]);
    rejected(f.snapshot, /fallback is not authorized|Incomplete preferred panel/);
  }
  for (const fallback of ['none', 'listed-only', 'any-eligible']) {
    const f = preferredPanel(fallback, [{}, { identity: { model: 'panel-model' } }]);
    assert.equal(evaluate(f.snapshot).eligible, true, fallback);
    f.values.author.reviewPolicy.selection = 'required'; f.snapshot.pr.body = body(f.values.author);
    assert.equal(evaluate(f.snapshot).eligible, true, fallback);
  }
});
test('F7 every missing panel slot needs its own authorized and recorded substitution', () => {
  for (const fallback of ['any-eligible', 'listed-only']) for (const model of ['review-model', 'permitted-fallback', 'outside-list']) for (const noted of [false, true]) {
    const f = preferredPanel(fallback, [{}, { identity: { model }, ...(noted ? { selectionNote: 'B unavailable; this session substitutes for its panel slot' } : {}) }]);
    assert.equal(evaluate(f.snapshot).eligible, noted && (fallback === 'any-eligible' || model === 'permitted-fallback'), `${fallback}: ${model}, noted=${noted}`);
  }
  const f = preferredPanel('listed-only', [{}, { selectionNote: 'B unavailable; explicitly permitted A substitution' }]);
  f.values.author.reviewPolicy.fallbackModels = [{ provider: 'example-provider', model: 'review-model' }]; f.snapshot.pr.body = body(f.values.author);
  assert.equal(evaluate(f.snapshot).eligible, true);
});
test('F7 one review cannot fill multiple panel slots, including overlapping selectors', () => {
  const f = preferredPanel('any-eligible', [{}, { selectionNote: 'Missing panel routes unavailable' }], ['review-model', 'panel-model', 'third-model']);
  rejected(f.snapshot, /Incomplete preferred panel/);
  f.values.author.reviewPolicy.selection = 'required'; f.values.author.reviewPolicy.minReviewers = 1; f.values.author.reviewPolicy.models = [{ provider: 'example-provider', model: 'review-model' }, { provider: 'example-provider', model: 'review-model', effort: 'high' }];
  f.snapshot.pr.body = body(f.values.author); f.snapshot.comments.length = 1;
  rejected(f.snapshot, /Required review model/);
});
test('F7 panel matching preserves complete overlapping panels independent of order', () => {
  for (const reverse of [false, true]) {
    const reviewers = [{ identity: { effort: 'high' } }, { identity: { effort: 'xhigh' } }];
    const f = preferredPanel('none', reverse ? reviewers.reverse() : reviewers);
    f.values.author.reviewPolicy.models = [{ provider: 'example-provider', model: 'review-model' }, { provider: 'example-provider', model: 'review-model', effort: 'high' }];
    f.snapshot.pr.body = body(f.values.author);
    assert.equal(evaluate(f.snapshot).eligible, true);
  }
});
test('F7 required panels never substitute, even when the preferred fallback list permits it', () => {
  const f = preferredPanel('any-eligible', [{}, { selectionNote: 'B unavailable' }]);
  f.values.author.reviewPolicy.selection = 'required'; f.snapshot.pr.body = body(f.values.author);
  rejected(f.snapshot, /Required review model/);
});
