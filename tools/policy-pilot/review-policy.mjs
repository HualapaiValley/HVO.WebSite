// Offline prototype only. Nothing imports this module in the live controller.
import { readFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { evaluate, record, MARKER } from '../pr-process.mjs';

const MINIMA = Object.freeze({
  low: Object.freeze({ minReviewers: 1, depth: 'mechanical', minFamilies: 0, deepReviewers: 0 }),
  standard: Object.freeze({ minReviewers: 1, depth: 'standard', minFamilies: 0, deepReviewers: 0 }),
  high: Object.freeze({ minReviewers: 2, depth: 'standard', minFamilies: 2, deepReviewers: 1 }),
});

// Risk must eventually come from a trusted impact assessment, not author JSON.
// This pure lookup grants no trust or admission and is not a risk classifier.
export function minimumForRisk(risk) {
  if (!Object.hasOwn(MINIMA, risk)) throw new Error('Unknown risk');
  return { ...MINIMA[risk] };
}

const UNKNOWN = /^(unknown|unavailable|provider-managed|auto|n\/a)$/i;
function declaredFamily(identity) {
  const value = identity?.modelFamily;
  return typeof value === 'string' && value.trim() && !UNKNOWN.test(value.trim())
    ? value.trim().toLowerCase() : null;
}

export function inspectCandidate(input) {
  // No impact/runtime receipt verifier exists. Do not let input.risk,
  // authenticated:true, author policy or claimed receipt fields lower the floor.
  const minimum = minimumForRisk('high');
  const problems = [];
  let result;
  try {
    const snapshot = structuredClone(input);
    if (!snapshot || typeof snapshot !== 'object') throw new Error('Snapshot required');
    const author = record(snapshot.pr?.body, 'author');
    if (!author) throw new Error('Author record required');
    // Validate the original schema before strengthening it. A malformed field
    // must not be repaired silently by the effective policy overlay.
    const original = evaluate(snapshot);
    problems.push(...original.errors);
    author.reviewPolicy.minReviewers = Math.max(minimum.minReviewers, author.reviewPolicy.minReviewers);
    if (author.reviewPolicy.depth === 'mechanical') author.reviewPolicy.depth = minimum.depth;
    snapshot.pr.body = `<!-- ${MARKER}\n${JSON.stringify(author)}\n-->`;
    result = evaluate(snapshot);
    problems.push(...result.errors);

    const binding = snapshot.mergeBinding;
    if (binding?.verified !== true || !['head', 'base', 'merge'].every(key => {
      const value = key === 'merge' ? snapshot.pr.merge_commit_sha : snapshot.pr[key]?.sha;
      return /^[a-f0-9]{40}$/.test(value ?? '') && binding[`${key}Sha`] === value;
    })) problems.push('Complete matching head/base/merge binding required');

    const families = new Set();
    for (const review of result.reviews ?? []) {
      const family = declaredFamily(review.identity);
      if (!family) problems.push('Reviewer model-family declaration missing');
      else families.add(family);
    }
    if (families.size < minimum.minFamilies) problems.push('Two distinct declared model families required');
    if ((result.reviews ?? []).filter(x => x.depth === 'deep').length < minimum.deepReviewers)
      problems.push('At least one deep review required');
  } catch (error) {
    problems.push(`Invalid evidence: ${error.message}`);
  }
  return {
    mode: 'offline-disabled',
    eligible: false,
    risk: 'high',
    riskReason: 'Conservative fallback: no authenticated impact assessment',
    minimum,
    structuralEligible: problems.length === 0,
    structuralErrors: [...new Set(problems)],
    blockers: [
      'No authenticated candidate/source or impact-assessment adapter',
      'No authenticated contributor, reviewer-session or model-family receipts',
      'No qualified trusted publisher or protected-branch integration',
      'Pilot admission is disabled; structural consistency is not approval',
    ],
  };
}

// Read-only local inspection; there is deliberately no --enable or receipt-trust flag.
// Exit 2 even for structurally consistent evidence so it cannot be used as a green gate.
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try {
    if (process.argv.length !== 3) throw new Error('Usage: node review-policy.mjs snapshot.json');
    console.log(JSON.stringify(inspectCandidate(JSON.parse(await readFile(process.argv[2], 'utf8'))), null, 2));
    process.exitCode = 2;
  } catch (error) {
    console.error(error.message);
    process.exitCode = 2;
  }
}
