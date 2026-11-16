import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';
import { inspectCandidate, minimumForRisk } from './review-policy.mjs';

const examples = JSON.parse(readFileSync(new URL('../pr-process-fixture.json', import.meta.url), 'utf8'));
const body = x => `<!-- hvo-pr-process\n${JSON.stringify(x)}\n-->`;
function fixture() {
  const author = structuredClone(examples.author);
  Object.assign(author.reviewPolicy, { depth: 'standard', minReviewers: 1,
    differentModel: false, differentProvider: false, differentAccount: false,
    selection: 'auto', models: [], modelMode: 'one-of', fallback: 'any-eligible' });
  const reviews = ['family-a', 'family-b'].map((family, index) => {
    const review = structuredClone(examples.review);
    Object.assign(review.identity, { session: `review-${index}`, modelFamily: family });
    review.depth = index === 0 ? 'deep' : 'standard';
    return review;
  });
  const snapshot = { pr: { number: 1, body: '', user: { login: author.publisher },
    head: { sha: author.headSha }, base: { sha: author.baseSha }, merge_commit_sha: '3'.repeat(40) },
    comments: [], threads: [], files: [{ filename: 'docs/example.md' }],
    mergeBinding: { verified: true, headSha: author.headSha, baseSha: author.baseSha, mergeSha: '3'.repeat(40) } };
  const sync = () => {
    snapshot.pr.body = body(author);
    snapshot.comments = reviews.map((review, i) => ({ id: i + 1, body: body(review),
      user: { login: review.publisher }, created_at: '2026-11-16T00:00:00Z',
      publisherPermission: { verified: true, login: review.publisher, permission: 'write' } }));
    return snapshot;
  };
  return { author, reviews, snapshot, sync };
}

test('ordinary minima require independence, not model diversity', () => {
  assert.equal(minimumForRisk('standard').minReviewers, 1);
  assert.equal(minimumForRisk('standard').minFamilies, 0);
  assert.equal(minimumForRisk('low').minReviewers, 1);
  assert.throws(() => minimumForRisk('author-chosen-waiver'));
  const copy = minimumForRisk('high'); copy.minReviewers = 0;
  assert.equal(minimumForRisk('high').minReviewers, 2);
});

test('structurally complete two-family evidence never becomes authenticated approval', () => {
  const result = inspectCandidate(fixture().sync());
  assert.equal(result.structuralEligible, true, result.structuralErrors.join('\n'));
  assert.equal(result.eligible, false);
  assert.equal(result.mode, 'offline-disabled');
  assert.match(result.blockers.join('\n'), /authenticated/);
});

test('author low-risk claims and fabricated receipt flags cannot lower the trusted-code floor', () => {
  const f = fixture(); f.reviews.pop(); f.author.reviewPolicy.depth = 'mechanical';
  const value = { ...f.sync(), risk: 'low', authenticated: true, receipts: { verified: true } };
  const result = inspectCandidate(value);
  assert.equal(result.risk, 'high');
  assert.equal(result.minimum.minReviewers, 2);
  assert.equal(result.structuralEligible, false);
  assert.equal(result.eligible, false);
  assert.match(result.structuralErrors.join('\n'), /Need 2/);
});

test('same-session aliases cannot fill two reviewer slots', () => {
  const f = fixture(); f.reviews[1].identity.session = f.reviews[0].identity.session;
  assert.equal(inspectCandidate(f.sync()).structuralEligible, false);
});

test('family comparison is normalized but never described as verified provenance', () => {
  const f = fixture(); f.reviews[1].identity.modelFamily = ' FAMILY-A ';
  assert.match(inspectCandidate(f.sync()).structuralErrors.join('\n'), /distinct declared/);
  delete f.reviews[1].identity.modelFamily;
  assert.match(inspectCandidate(f.sync()).structuralErrors.join('\n'), /declaration missing/);
});

for (const [label, families] of [
  ['provider alias', ['claude', 'anthropic']],
  ['Cyrillic lookalike', ['gpt', 'g\u0440t']],
  ['invisible character', ['claude', 'claude\u200b']],
  ['Unicode compatibility form', ['gpt', '\uff47\uff50\uff54']],
]) {
  test(`${label} declarations and forged mappings never establish family provenance`, () => {
    const f = fixture();
    f.reviews.forEach((review, index) => { review.identity.modelFamily = families[index]; });
    const snapshot = f.sync();
    // An attacker also supplies an apparently verified mapping and receipts.
    snapshot.familyMapping = { authenticated: true, canonicalFamilies: ['family-a', 'family-b'] };
    snapshot.receipts = { verified: true, authenticated: true };
    const result = inspectCandidate(snapshot);
    // Document the structural limitation rather than pretending aliases are resolved.
    assert.equal(result.structuralEligible, true);
    assert.equal(result.eligible, false);
    assert.equal(result.mode, 'offline-disabled');
    assert.match(result.blockers.join('\n'), /No trusted canonical family mapping/);
  });
}

test('at least one deep review, not both, is required unless assignment is stricter', () => {
  const f = fixture();
  assert.equal(inspectCandidate(f.sync()).structuralEligible, true);
  f.reviews[0].depth = 'standard';
  assert.match(inspectCandidate(f.sync()).structuralErrors.join('\n'), /one deep/);
  f.reviews[0].depth = 'deep'; f.author.reviewPolicy.depth = 'deep';
  assert.equal(inspectCandidate(f.sync()).structuralEligible, false);
});

test('stronger assignment count and model separation are preserved', () => {
  const f = fixture(); f.author.reviewPolicy.minReviewers = 3;
  assert.match(inspectCandidate(f.sync()).structuralErrors.join('\n'), /Need 3/);
  f.author.reviewPolicy.minReviewers = 1; f.author.reviewPolicy.differentModel = true;
  f.reviews[0].identity.model = f.author.identity.model;
  f.reviews[0].identity.provider = f.author.identity.provider;
  assert.match(inspectCandidate(f.sync()).structuralErrors.join('\n'), /not independent/);
});

test('existing source, local evidence and publication failures stay blocking', () => {
  for (const mutate of [
    f => { f.reviews[0].headSha = 'a'.repeat(40); },
    f => { f.author.validation[0].result = 'fail'; },
    f => { f.reviews[0].identity.session = f.author.identity.session; },
    f => { f.reviews[0].verdict = 'CHANGES_REQUIRED'; },
    f => { f.author.reviewPolicy.minReviewers = 'waived'; },
  ]) {
    const f = fixture(); mutate(f);
    assert.equal(inspectCandidate(f.sync()).structuralEligible, false);
  }
  const f = fixture(); const snapshot = f.sync();
  snapshot.comments[0].publisherPermission.verified = false;
  assert.equal(inspectCandidate(snapshot).structuralEligible, false);
});

test('missing or stale merge tuple and malformed input fail closed without mutation', () => {
  const f = fixture(); const snapshot = f.sync(); const saved = structuredClone(snapshot);
  inspectCandidate(snapshot); assert.deepEqual(snapshot, saved);
  snapshot.mergeBinding.mergeSha = '4'.repeat(40);
  assert.match(inspectCandidate(snapshot).structuralErrors.join('\n'), /binding required/);
  for (const value of [null, {}, [], { pr: { body: '{broken' } }]) {
    assert.equal(inspectCandidate(value).eligible, false);
    assert.equal(inspectCandidate(value).structuralEligible, false);
  }
});

test('CLI never exits successfully even for consistent declarations', () => {
  const dir = mkdtempSync(join(tmpdir(), 'hvo-pilot-evidence-'));
  try {
    const file = join(dir, 'snapshot.json'); writeFileSync(file, JSON.stringify(fixture().sync()));
    const result = spawnSync(process.execPath, [new URL('./review-policy.mjs', import.meta.url).pathname, file], { encoding: 'utf8' });
    assert.equal(result.status, 2);
    assert.equal(JSON.parse(result.stdout).eligible, false);
    assert.equal(JSON.parse(result.stdout).structuralEligible, true);
  } finally { rmSync(dir, { recursive: true }); }
});
