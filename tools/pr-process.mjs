import { appendFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';

export const MARKER = 'hvo-pr-process';
export const PHASES = ['draft', 'review', 'changes-required', 'ci', 'ready-to-merge', 'complete', 'cancelled'].map(x => `workflow:${x}`);
export const DEPTHS = ['mechanical', 'standard', 'deep'];
export const TERMINAL = ['CORRECTED', 'DEFERRED', 'NON_ACTIONABLE', 'SUPERSEDED'];
const unknown = value => !value || /^(unknown|unavailable|provider-managed|auto|n\/a)$/i.test(value);
const login = value => String(value ?? '').toLowerCase();
const human = identity => login(identity.provider) === 'human' && login(identity.model) === 'n/a' && login(identity.effort) === 'n/a';
const knownModel = identity => human(identity) || (!unknown(identity.provider) && !unknown(identity.model));
const sameModel = (a, b) => knownModel(a) && knownModel(b) && login(a.provider) === login(b.provider) && a.model === b.model;
const fields = ['account', 'session', 'system', 'provider', 'model', 'effort', 'host', 'instance', 'workspace'];
const nonempty = value => typeof value === 'string' && Boolean(value.trim());
const selectorValid = value => nonempty(value?.provider) && !unknown(value.provider) && nonempty(value?.model) && !unknown(value.model) && (value.effort === undefined || (nonempty(value.effort) && !unknown(value.effort)));
const knownOrigin = identity => !unknown(identity.host) && !unknown(identity.instance);
export function sameSession(a, b) {
  if (a.session !== b.session) return false;
  // Unknown origin cannot establish that a reused session ID is independent.
  return !knownOrigin(a) || !knownOrigin(b) || (login(a.host) === login(b.host) && a.instance === b.instance);
}

// One record per body. Malformed or duplicated evidence is not silently ignored.
export function record(body, kind) {
  const matches = [...String(body ?? '').matchAll(/<!--\s*hvo-pr-process\s*\n([\s\S]*?)-->/g)];
  if (!matches.length) return null;
  if (matches.length !== 1) throw new Error('Exactly one process record is allowed per body');
  const value = JSON.parse(matches[0][1]);
  if (value.version !== 1 || value.kind !== kind) throw new Error(`Expected version 1 ${kind} record`);
  return value;
}

export function identityErrors(identity, prefix) {
  return fields.filter(key => typeof identity?.[key] !== 'string' || !identity[key].trim()).map(key => `${prefix}.${key} is required`);
}

function sourceMatches(value, pr) { return value.headSha === pr.head.sha && value.baseSha === pr.base.sha; }
function publisherMatches(value, publisher) { return login(value.publisher) === login(publisher); }
function publisherAuthorized(comment, publisher) {
  const permission = comment.publisherPermission;
  return permission?.verified === true && login(permission.login) === login(publisher) && ['admin', 'write'].includes(permission.permission);
}
function independent(identity, author, policy) {
  const contributors = author.contributors;
  if (contributors.some(x => sameSession(x, identity))) return false;
  if (policy.differentAccount && contributors.some(x => login(x.account) === login(identity.account))) return false;
  if (policy.differentProvider && (unknown(identity.provider) || contributors.some(x => unknown(x.provider) || login(x.provider) === login(identity.provider)))) return false;
  const comparison = policy.modelComparison === 'primary' ? [author.identity] : contributors;
  if (policy.differentModel && (!knownModel(identity) || comparison.some(x => !knownModel(x) || sameModel(x, identity)))) return false;
  return true;
}

function selected(identity, selector) {
  return !unknown(identity.provider) && !unknown(identity.model) && login(identity.provider) === login(selector.provider) && identity.model === selector.model && (!selector.effort || (!unknown(identity.effort) && identity.effort === selector.effort));
}

export function evaluate({ pr, comments = [], threads = [] }) {
  const errors = [];
  const rejectedPublications = [];
  let author;
  try { author = record(pr.body, 'author'); } catch (error) { errors.push(error.message); }
  if (!author) return { eligible: false, errors: [...errors, 'Author process record is missing'], depth: null, reviews: [] };
  errors.push(...identityErrors(author.identity, 'author.identity'));
  if (!publisherMatches(author, pr.user.login)) errors.push('Author publisher must match the PR creator');
  if (!sourceMatches(author, pr)) errors.push('Author evidence does not match current head/base');
  if (!Array.isArray(author.contributors) || !author.contributors.length) errors.push('Implementation contributor roster is required');
  const contributors = Array.isArray(author.contributors) ? author.contributors : [];
  contributors.forEach((x, i) => errors.push(...identityErrors(x, `contributors[${i}]`)));
  if (!contributors.some(x => fields.every(key => x?.[key] === author.identity?.[key]))) errors.push('Primary author must be in contributor roster');
  if (contributors.some((x, i) => x && contributors.slice(0, i).some(y => y && sameSession(x, y) && ['provider', 'model', 'effort'].every(key => x[key] === y[key])))) errors.push('Contributor origin/session/provider/model/effort tuples must be unique');
  if (!Array.isArray(author.validation) || !author.validation.length) errors.push('Local validation and performer provenance are required');
  for (const [i, check] of (Array.isArray(author.validation) ? author.validation : []).entries()) {
    errors.push(...identityErrors(check?.performer, `validation[${i}].performer`));
    if (!nonempty(check?.command) || !['pass', 'blocked'].includes(check?.result) || (check?.result === 'blocked' && !nonempty(check?.reason))) errors.push(`validation[${i}] requires command, pass/blocked result and blocker explanation`);
  }
  const policy = author.reviewPolicy ?? {};
  if (!DEPTHS.includes(policy.depth)) errors.push('Review depth must be mechanical, standard or deep');
  if (!['auto', 'preferred', 'required'].includes(policy.selection)) errors.push('Reviewer selection must be auto, preferred or required');
  if (!Number.isInteger(policy.minReviewers) || policy.minReviewers < 1 || policy.minReviewers > 5) errors.push('minReviewers must be between 1 and 5');
  if (typeof policy.differentAccount !== 'boolean' || typeof policy.differentModel !== 'boolean' || !['all-contributors', 'primary'].includes(policy.modelComparison)) errors.push('Explicit account/model separation and comparison scope are required');
  if (policy.differentProvider !== undefined && typeof policy.differentProvider !== 'boolean') errors.push('differentProvider must be a boolean');
  if (!Array.isArray(policy.models) || policy.models.some(x => !selectorValid(x))) errors.push('models must be an array of known provider/model selectors');
  if (policy.selection === 'required' && !policy.models?.length) errors.push('Required selection needs at least one model selector');
  if (policy.selection === 'preferred' && !policy.models?.length) errors.push('Preferred selection needs at least one model selector');
  if (policy.modelMode !== undefined && !['one-of', 'all-of'].includes(policy.modelMode)) errors.push('modelMode must be one-of or all-of');
  if (!['any-eligible', 'listed-only', 'none'].includes(policy.fallback)) errors.push('Explicit fallback must be any-eligible, listed-only or none');
  if (policy.fallback === 'listed-only' && (!Array.isArray(policy.fallbackModels) || !policy.fallbackModels.length || policy.fallbackModels.some(x => !selectorValid(x)))) errors.push('listed-only fallback requires known fallbackModels');
  if (errors.length) return { eligible: false, errors, depth: policy.depth, reviews: [] };

  // Keep the latest current-source decision from each distinct reviewing session.
  const latest = new Map();
  for (const comment of comments) {
    if (!String(comment.body).includes(MARKER)) continue;
    // Public comments cannot grant approval or retract an authorized decision.
    // Permission comes from authenticated API metadata, never the editable body.
    if (!publisherAuthorized(comment, comment.user?.login)) { rejectedPublications.push(`Comment ${comment.id}: publisher is not verified with repository write/admin access${comment.publisherPermission?.reason ? ` (${comment.publisherPermission.reason})` : ''}`); continue; }
    let review;
    try { review = record(comment.body, 'review'); } catch (error) { errors.push(`Comment ${comment.id}: ${error.message}`); continue; }
    if (!review || !sourceMatches(review, pr)) continue;
    if (comment.publication?.type === 'native-review') {
      if (['PENDING', 'DISMISSED'].includes(comment.publication.state)) continue;
      if (comment.publication.commitSha !== pr.head.sha) { errors.push(`Native review ${comment.publication.id} targets a stale commit`); continue; }
      if (!['COMMENTED', 'APPROVED', 'CHANGES_REQUESTED'].includes(comment.publication.state) || !comment.created_at) { errors.push(`Native review ${comment.publication.id} is not a published review`); continue; }
      if (comment.publication.state === 'CHANGES_REQUESTED' && review.verdict === 'APPROVE') { errors.push(`Native review ${comment.publication.id} requests changes but its record claims approval`); continue; }
    }
    if (!publisherMatches(review, comment.user?.login)) { errors.push(`Comment ${comment.id}: publisher mismatch`); continue; }
    const problems = identityErrors(review.identity, 'review.identity');
    if (problems.length) { errors.push(...problems); continue; }
    if (policy.differentAccount && (login(review.identity.account) !== login(review.publisher) || login(author.identity.account) !== login(author.publisher))) { errors.push('Strict account separation requires direct, authenticated publication'); continue; }
    if (!independent(review.identity, author, policy)) { errors.push(`Review session ${review.identity.session} is not independent under this policy`); continue; }
    const entry = { ...review, commentId: comment.id, publication: comment.publication ?? { type: 'issue-comment', id: comment.id, url: comment.html_url }, publishedAt: comment.updated_at ?? comment.created_at };
    const previous = [...latest.values()].find(x => sameSession(x.identity, review.identity));
    if (!previous || Date.parse(entry.publishedAt) > Date.parse(previous.publishedAt) || (Date.parse(entry.publishedAt) === Date.parse(previous.publishedAt) && entry.commentId > previous.commentId)) {
      if (previous) latest.delete(previous.commentId);
      latest.set(entry.commentId, entry);
    }
  }
  const reviews = [...latest.values()];
  if (!reviews.length) errors.push('No current-source independent review');
  if (reviews.some(x => x.verdict !== 'APPROVE')) errors.push('Current-source review has outstanding changes or is incomplete');
  if (reviews.some(x => !nonempty(x.coverage) || x.coverageComplete !== true || !Array.isArray(x.limitations) || x.limitations.some(v => typeof v !== 'string') || !Array.isArray(x.findings) || DEPTHS.indexOf(x.depth) < DEPTHS.indexOf(policy.depth))) errors.push('Completed coverage at the required review depth, limitations and finding index are required');
  if (reviews.length < policy.minReviewers) errors.push(`Need ${policy.minReviewers} distinct independent reviewing sessions`);
  const modelSatisfied = policy.modelMode === 'all-of'
    ? policy.models.every(selector => reviews.some(x => selected(x.identity, selector)))
    : reviews.some(x => policy.models.some(selector => selected(x.identity, selector)));
  if (policy.selection === 'required' && !modelSatisfied) errors.push('Required review model/effort selection is not satisfied');
  if (policy.selection === 'preferred' && !modelSatisfied) {
    if (policy.fallback === 'none') errors.push('Preferred model unavailable and fallback is not authorized');
    if (!reviews.some(x => nonempty(x.selectionNote))) errors.push('Actual preferred-model substitution and reason must be recorded');
    for (const review of reviews) {
      if (policy.models.some(selector => selected(review.identity, selector))) continue;
      if (!nonempty(review.selectionNote)) errors.push('Actual preferred-model substitution and reason must be recorded');
      if (policy.fallback === 'listed-only' && !policy.fallbackModels.some(selector => selected(review.identity, selector))) errors.push('Reviewer is outside the explicitly permitted fallback models');
    }
  }

  const indexed = new Map();
  for (const review of reviews) {
    for (const finding of (Array.isArray(review.findings) ? review.findings : [])) {
      if (!finding || !/^F[1-9]\d*$/.test(finding.id) || !['P0', 'P1', 'P2', 'P3'].includes(finding.severity) || !TERMINAL.includes(finding.disposition) || !nonempty(finding.threadId)) { errors.push('Finding needs stable F-number, severity, thread and terminal disposition'); continue; }
      if (finding.disposition === 'DEFERRED' && ['P0', 'P1'].includes(finding.severity)) errors.push(`${finding.severity} finding ${finding.id} cannot be deferred`);
      if (indexed.has(finding.id) && indexed.get(finding.id).threadId !== finding.threadId) errors.push(`Finding ${finding.id} points to conflicting threads`);
      indexed.set(finding.id, finding);
    }
  }
  const handled = new Set();
  const superseded = new Map();
  const nonDeferrable = new Set();
  for (const thread of threads) {
    if (!thread.isResolved) errors.push(`Thread ${thread.id} is unresolved`);
    const root = thread.comments[0];
    if (root && !publisherAuthorized(root, root.author?.login)) {
      rejectedPublications.push(`Thread ${thread.id}: root publisher is unverified; resolved outside findings need authorized adoption to enter the finding index`);
      continue;
    }
    let finding;
    if (String(root?.body).includes(MARKER)) {
      try { finding = record(root.body, 'finding'); } catch (error) { errors.push(`Thread ${thread.id}: ${error.message}`); }
    }
    if (!finding) {
      // A substantive inline review thread cannot disappear by omission from the index.
      if (root) errors.push(`Thread ${thread.id} has no structured finding record`);
      continue;
    }
    const index = indexed.get(finding.id);
    if (!index || index.threadId !== thread.id || index.severity !== finding.severity) { errors.push(`Finding ${finding.id} is missing or inconsistent in review index`); continue; }
    if (finding.nonDeferrable === true || ['P0', 'P1'].includes(finding.severity)) nonDeferrable.add(finding.id);
    let verified = false;
    const replies = thread.comments.slice(1).sort((a, b) => (Date.parse(a.updatedAt) || 0) - (Date.parse(b.updatedAt) || 0));
    for (const comment of replies) {
      if (!String(comment.body).includes(MARKER)) continue;
      if (!publisherAuthorized(comment, comment.author?.login)) { rejectedPublications.push(`Thread ${thread.id}: verification publisher is not verified with repository write/admin access${comment.publisherPermission?.reason ? ` (${comment.publisherPermission.reason})` : ''}`); continue; }
      try {
        const value = record(comment.body, 'verification');
        if (!value || !sourceMatches(value, pr) || !publisherMatches(value, comment.author?.login) || value.findingId !== finding.id || identityErrors(value.identity, 'verification.identity').length || !independent(value.identity, author, policy) || !nonempty(value.evidence)) continue;
        if (!reviews.some(x => fields.every(key => x.identity[key] === value.identity[key]))) continue;
        // A later STILL_OPEN or changed disposition retracts earlier verification.
        verified = false;
        superseded.delete(finding.id);
        if (value.disposition !== index.disposition) continue;
        if (value.disposition === 'DEFERRED' && (['P0', 'P1'].includes(finding.severity) || finding.nonDeferrable === true || !author.allowedDeferrals?.includes(finding.id) || !value.followUp)) continue;
        if (value.disposition === 'SUPERSEDED' && (!indexed.has(value.supersededBy) || value.supersededBy === finding.id)) continue;
        if (value.disposition === 'SUPERSEDED') superseded.set(finding.id, value.supersededBy);
        verified = true;
      } catch (error) { errors.push(`Thread ${thread.id}: ${error.message}`); }
    }
    if (!verified) errors.push(`Finding ${finding.id} has no current-source independent terminal verification`);
    handled.add(finding.id);
  }
  for (const finding of indexed.values()) if (!handled.has(finding.id)) errors.push(`Finding ${finding.id} has no matching code thread`);
  for (const id of superseded.keys()) {
    const seen = new Set();
    let target = id;
    while (superseded.has(target) && !seen.has(target)) { seen.add(target); target = superseded.get(target); }
    if (seen.has(target) || indexed.get(target)?.disposition === 'SUPERSEDED') errors.push(`Finding ${id} has no finite verified superseding disposition`);
    if (nonDeferrable.has(id) && indexed.get(target)?.disposition === 'DEFERRED') errors.push(`Blocking finding ${id} cannot be deferred through supersession`);
  }
  return { eligible: errors.length === 0, errors, rejectedPublications, depth: policy.depth, reviews };
}

export function phase({ pr, evidence, ci, action }) {
  if (pr.state === 'closed') return pr.merged ? 'workflow:complete' : 'workflow:cancelled';
  if (action === 'synchronize') return 'workflow:review';
  if (ci?.status === 'completed' && ci.conclusion !== 'success' && action !== 'ready_for_review') return 'workflow:changes-required';
  if (!pr.draft && evidence.eligible) return ci?.status === 'completed' && ci.conclusion === 'success' ? 'workflow:ready-to-merge' : 'workflow:ci';
  if (evidence.reviews?.some(x => x.verdict === 'CHANGES_REQUIRED')) return 'workflow:changes-required';
  return evidence.depth ? 'workflow:review' : 'workflow:draft';
}

export class GitHub {
  constructor(repository, token, fetcher = fetch) {
    if (!/^[\w.-]+\/[\w.-]+$/.test(repository ?? '') || !token) throw new Error('GITHUB_REPOSITORY and token are required');
    this.repository = repository; this.token = token; this.fetcher = fetcher;
    this.root = process.env.GITHUB_API_URL ?? 'https://api.github.com';
    this.permissionCache = new Map();
  }
  async request(path, method = 'GET', body) {
    const response = await this.fetcher(`${this.root}${path}`, { method, headers: { Authorization: `Bearer ${this.token}`, Accept: 'application/vnd.github+json', 'X-GitHub-Api-Version': '2022-11-28', 'Content-Type': 'application/json' }, body: body === undefined ? undefined : JSON.stringify(body) });
    if (!response.ok) throw new Error(`GitHub ${method} ${path}: ${response.status} ${await response.text()}`);
    return response.status === 204 ? null : response.json();
  }
  async list(path) {
    const items = [];
    for (let page = 1; page <= 20; page++) {
      const data = await this.request(`${path}${path.includes('?') ? '&' : '?'}per_page=100&page=${page}`);
      if (!Array.isArray(data)) throw new Error(`Expected paginated array: ${path}`);
      items.push(...data); if (data.length < 100) return items;
    }
    throw new Error('Pagination limit exceeded; evidence is incomplete');
  }
  async graphql(query, variables) {
    const value = await this.request('/graphql', 'POST', { query, variables });
    if (value.errors) throw new Error(JSON.stringify(value.errors));
    return value.data;
  }
  async pull(number) { return this.request(`/repos/${this.repository}/pulls/${number}`); }
  async publisherPermission(publisher) {
    const key = login(publisher);
    if (this.permissionCache.has(key)) return this.permissionCache.get(key);
    let value = { login: publisher, verified: false, permission: 'unknown' };
    if (key && this.permissionCache.size < 100) {
      try {
        const data = await this.request(`/repos/${this.repository}/collaborators/${encodeURIComponent(publisher)}/permission`);
        if (login(data.user?.login) === key && ['admin', 'write', 'read', 'none'].includes(data.permission)) value = { login: data.user.login, verified: true, permission: data.permission, role: data.role_name };
      } catch (error) { value.reason = error.message; }
    } else value.reason = 'Missing publisher or bounded permission lookup limit exceeded';
    this.permissionCache.set(key, value);
    return value;
  }
  async authorizePublications(comments, threads) {
    // Serial lookups are bounded and cache by authenticated account within this snapshot.
    for (const comment of comments) if (String(comment.body).includes(MARKER)) comment.publisherPermission = await this.publisherPermission(comment.user?.login);
    for (const thread of threads) for (const [i, comment] of thread.comments.entries()) if (i === 0 || String(comment.body).includes(MARKER)) comment.publisherPermission = await this.publisherPermission(comment.author?.login);
  }
  async snapshot(number) {
    this.permissionCache.clear();
    const pr = await this.pull(number);
    const [issueComments, nativeReviews] = await Promise.all([
      this.list(`/repos/${this.repository}/issues/${number}/comments`),
      this.list(`/repos/${this.repository}/pulls/${number}/reviews`),
    ]);
    const comments = [
      ...issueComments.map(x => ({ ...x, id: `comment:${x.id}`, publication: { type: 'issue-comment', id: x.id, url: x.html_url } })),
      ...nativeReviews.map(x => ({ id: `review:${x.id}`, body: x.body, user: x.user, created_at: x.submitted_at, publication: { type: 'native-review', id: x.id, url: x.html_url, commitSha: x.commit_id, state: x.state } })),
    ];
    const [owner, name] = this.repository.split('/');
    const threads = [];
    let cursor = null;
    for (let page = 0; page < 20; page++) {
      const data = await this.graphql(`query($owner:String!,$name:String!,$number:Int!,$cursor:String){repository(owner:$owner,name:$name){pullRequest(number:$number){reviewThreads(first:100,after:$cursor){nodes{id isResolved comments(first:100){nodes{id body updatedAt author{login} isMinimized} pageInfo{hasNextPage}}} pageInfo{hasNextPage endCursor}}}}}`, { owner, name, number, cursor });
      const connection = data.repository.pullRequest.reviewThreads;
      for (const thread of connection.nodes) {
        if (thread.comments.pageInfo.hasNextPage) throw new Error('Thread exceeds 100 comments; evidence is incomplete');
        threads.push({ ...thread, comments: thread.comments.nodes });
      }
      if (!connection.pageInfo.hasNextPage) { await this.authorizePublications(comments, threads); return { pr, comments, threads }; }
      cursor = connection.pageInfo.endCursor;
    }
    throw new Error('Thread pagination limit exceeded; evidence is incomplete');
  }
}

export async function preflight(api, number) {
  const pr = await api.pull(number);
  const errors = [];
  const match = /^(feat|fix|docs|test|refactor|chore)\([a-zA-Z][\w./-]*\): \S.* \(#([1-9]\d*)\)$/.exec(pr.title ?? '');
  if (!match) errors.push('Title must be <type>(<component>): <change> (#N), using feat/fix/docs/test/refactor/chore');
  const issueNumber = match ? Number(match[2]) : null;
  if (issueNumber && !new RegExp(`\\b(?:Closes|Resolves|Fixes)\\s+#${issueNumber}\\b`, 'i').test(pr.body ?? '')) errors.push(`Body must link the same repository issue with Closes/Resolves/Fixes #${issueNumber}`);
  for (const heading of ['Purpose', 'Work completed', 'Authorship and provenance', 'Acceptance evidence', 'Local validation', 'Review requirements', 'Risks and follow-up']) {
    if (!new RegExp(`^#{2,3}\\s+${heading}\\s*$`, 'im').test(pr.body ?? '')) errors.push(`Description is missing the ${heading} section`);
  }
  let author;
  try { author = record(pr.body, 'author'); } catch (error) { errors.push(error.message); }
  if (!author) errors.push('Author process record is required');
  else {
    errors.push(...identityErrors(author.identity, 'author.identity'));
    if (!publisherMatches(author, pr.user.login)) errors.push('Author publisher must match the actual PR creator');
    if (!sourceMatches(author, pr)) errors.push('Author evidence does not match the current raw head/current target tip');
    if (!Array.isArray(author.contributors) || !author.contributors.length) errors.push('Implementation contributor roster is required');
    for (const [i, contributor] of (Array.isArray(author.contributors) ? author.contributors : []).entries()) errors.push(...identityErrors(contributor, `contributors[${i}]`));
    if (!Array.isArray(author.contributors) || !author.contributors.some(x => fields.every(key => x?.[key] === author.identity?.[key]))) errors.push('Primary author must be in contributor roster');
    if (!Array.isArray(author.validation)) errors.push('Validation must be an array, even before checks are completed');
    for (const [i, value] of (Array.isArray(author.validation) ? author.validation : []).entries()) {
      errors.push(...identityErrors(value?.performer, `validation[${i}].performer`));
      if (!nonempty(value?.command) || !['pass', 'fail', 'blocked'].includes(value?.result) || (value?.result === 'blocked' && !nonempty(value?.reason))) errors.push(`validation[${i}] requires a command, pass/fail/blocked outcome and blocker explanation`);
    }
    if (!DEPTHS.includes(author.reviewPolicy?.depth)) errors.push('Required review depth is missing or invalid');
  }
  if (issueNumber) {
    try {
      const issue = await api.request(`/repos/${api.repository}/issues/${issueNumber}`);
      if (issue.number !== issueNumber || issue.pull_request) errors.push(`#${issueNumber} must be an existing same-repository issue, not a PR`);
    } catch (error) { errors.push(`Linked issue #${issueNumber} could not be verified: ${error.message}`); }
  }
  return { eligible: errors.length === 0, errors, issueNumber, headSha: pr.head.sha, baseSha: pr.base.sha };
}

export async function check(api, number, expected = {}) {
  const snapshot = await api.snapshot(number);
  const evidence = evaluate(snapshot);
  if (snapshot.pr.draft) evidence.errors.push('PR is still draft');
  if (snapshot.pr.state !== 'open') evidence.errors.push('PR is no longer open');
  if ((expected.head && expected.head !== snapshot.pr.head.sha) || (expected.base && expected.base !== snapshot.pr.base.sha)) evidence.errors.push('Workflow source does not match the current candidate');
  const fresh = await api.pull(number);
  if (fresh.head.sha !== snapshot.pr.head.sha || fresh.base.sha !== snapshot.pr.base.sha || fresh.draft || fresh.body !== snapshot.pr.body) evidence.errors.push('Candidate moved, provenance changed or PR returned to draft during validation');
  if (fresh.state !== 'open') evidence.errors.push('PR closed during validation');
  evidence.eligible = evidence.errors.length === 0;
  return { ...evidence, headSha: snapshot.pr.head.sha, baseSha: snapshot.pr.base.sha };
}

async function main() {
  const api = new GitHub(process.env.GITHUB_REPOSITORY, process.env.GH_TOKEN);
  const number = Number(process.env.PR_NUMBER);
  if (!Number.isInteger(number) || number < 1) throw new Error('PR_NUMBER is required');
  const mode = process.argv[2] === 'preflight' ? 'PR Preflight' : 'Review Evidence';
  const result = mode === 'PR Preflight' ? await preflight(api, number) : await check(api, number, { head: process.env.EXPECTED_HEAD, base: process.env.EXPECTED_BASE });
  if (process.env.GITHUB_OUTPUT) await appendFile(process.env.GITHUB_OUTPUT, `eligible=${result.eligible}\nhead=${result.headSha}\nbase=${result.baseSha}\n`);
  if (process.env.GITHUB_STEP_SUMMARY) await appendFile(process.env.GITHUB_STEP_SUMMARY, `${mode} for #${number}\n\nHead: ${result.headSha}\nBase: ${result.baseSha}\nTested merge: ${process.env.GITHUB_SHA ?? 'local/read-only inspection'}\n\n${result.eligible ? (mode === 'PR Preflight' ? 'Title, issue, description and source/provenance fields accepted; this is not review approval.' : 'Current-source independent review and finding verification accepted.') : result.errors.map(x => `- ${x}`).join('\n')}\n`);
  console.log(JSON.stringify(result, null, 2));
  if (!result.eligible) process.exitCode = 1;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) main().catch(error => { console.error(error.message); process.exitCode = 1; });
