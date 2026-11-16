"""Offline, unauthenticated evidence inspection. No external access or admission."""
import hashlib
import json
import re
import sys
from pathlib import Path

LIMIT = 8 * 1024 * 1024


def saved_json(path):
    try:
        data = Path(path).read_bytes()
        if len(data) > LIMIT:
            return None
        text = data.decode('utf-8')
        decoder = json.JSONDecoder()
        # Only accept a complete object; an incomplete object is never repaired.
        start = text.find('{')
        if start < 0:
            return None
        value, _ = decoder.raw_decode(text[start:])
        return value if isinstance(value, dict) else None
    except (OSError, UnicodeError, ValueError):
        return None


def digest(value):
    return hashlib.sha256(value.encode('utf-8')).hexdigest()


def collect(config, thread, pages, github):
    flags = {'FAMILY_UNKNOWN', 'SESSION_DISTINCTNESS_UNKNOWN'}
    if any(x is None for x in [thread, github, *pages]) or not pages:
        flags.add('FILE_TRUNCATED_OR_UNPARSABLE')
    thread = thread or {}; github = github or {}; pages = [p for p in pages if p is not None]
    t = thread.get('thread') or {}; runs = thread.get('recentRuns') or []; items = thread.get('items') or []
    if thread.get('hasMore') or any(p.get('hasMore') or p.get('nextCursor') is not None for p in pages): flags.add('PAGINATION_INCOMPLETE')
    if t.get('runCount', 0) > len(runs): flags.add('RUNS_HIDDEN')
    if any(not r.get('executedModel') for r in runs): flags.add('MODEL_EXECUTED_ABSENT')
    if any(r.get('executedModel') and r.get('model') != r.get('executedModel') for r in runs): flags.add('MODEL_REQUESTED_EXECUTED_MISMATCH')
    if len({r.get('executedModel') for r in runs if r.get('executedModel')}) > 1: flags.add('MIXED_RUN_MODELS')
    if t.get('runCount', 0) > 1 or len(runs) > 1: flags.add('EXTRA_RUN')
    if any(x.get('createdBy') == 'user' and x.get('creationSource') == 'web' for x in items): flags.add('HUMAN_RETRY')
    if any(x.get('textTruncated') for x in items): flags.add('TEXT_TRUNCATED')
    if any(x.get('parentThreadId') == t.get('threadId') for p in pages for x in p.get('threads', [])): flags.add('SUBAGENTS_PRESENT')
    candidates = {x.get('threadId') for p in pages for x in p.get('threads', []) if config.get('titleNonce') and config['titleNonce'] in x.get('title', '')}
    if len(candidates) > 1: flags.add('DUPLICATE_THREADS')
    if config.get('ambiguousLaunch'): flags.add('AMBIGUOUS_LAUNCH')
    if config.get('evidenceReopened'): flags.add('EVIDENCE_REOPENED')
    if not config.get('launchedByCoordinator'): flags.add('NOT_COORDINATOR_LAUNCHED')
    pr = github.get('pr') or {}; head = (pr.get('head') or {}).get('sha'); base = (pr.get('base') or {}).get('sha')
    if not config.get('expectedHead'): flags.add('HEAD_SELF_REPORTED_ONLY')
    if config.get('expectedHead') and head != config['expectedHead']: flags.add('HEAD_STALE')
    if config.get('expectedBase') and base != config['expectedBase']: flags.add('HEAD_MISMATCH')
    statuses = github.get('statuses') or {}; contexts = statuses.get('statuses', [])
    required = config.get('requiredContexts', [])
    if any(not [s for s in contexts if s.get('context') == c] or [s for s in contexts if s.get('context') == c][0].get('state') != 'success' for c in required): flags.add('CHECKS_FAILED')
    workflows = github.get('workflowRuns') or []
    if isinstance(workflows, dict): workflows = workflows.get('workflow_runs', [])
    if any(w.get('conclusion') in ('failure', 'cancelled', 'timed_out', 'action_required') for w in workflows): flags.add('CHECKS_FAILED')
    ci = [w for w in workflows if w.get('path', '').split('/')[-1] == 'ci.yml' and w.get('head_sha') == head and w.get('base_sha') == base and w.get('merge_sha') == config.get('expectedMerge')]
    if any(w.get('run_attempt', 0) >= 2 for w in ci): flags.add('CI_ATTEMPT_NOT_FIRST')
    if len({w.get('id') for w in ci if w.get('run_attempt') == 1}) > 3: flags.add('CI_RETRY_BUDGET_EXHAUSTED')
    selected = next((r for r in runs if r.get('runId') == config.get('countedRunId')), None)
    if selected is None and runs: selected = max(runs, key=lambda x: x.get('ordinal') or 0)
    if {'DUPLICATE_THREADS', 'AMBIGUOUS_LAUNCH', 'EVIDENCE_REOPENED'} & flags:
        selected = None
    selected = selected or {}
    reports = [x for x in items if x.get('type') == 'assistant_message' and x.get('runId') == selected.get('runId')]
    report = max(reports, key=lambda x: x.get('position', 0)) if reports else {}
    report_text = report.get('text') if not report.get('textTruncated') and report.get('status') == 'completed' and 'FILE_TRUNCATED_OR_UNPARSABLE' not in flags and 'PAGINATION_INCOMPLETE' not in flags else None
    verdicts = re.findall(r'(?m)^\s*(?:VERDICT\s*:\s*)?(CLEAN|CHANGES_REQUIRED|INCOMPLETE|BLOCKED)\s*$', report_text or '')
    users = [x for x in items if x.get('type') == 'user_message']
    first = min(users, key=lambda x: x.get('position', 0)) if users else {}
    prompt = first.get('text') if not first.get('textTruncated') else None
    return {'repository': config.get('repository'), 'prNumber': config.get('prNumber'),
            'expectedBase': config.get('expectedBase'), 'expectedHead': config.get('expectedHead'),
            'githubHeadAtCollection': head, 'githubBaseAtCollection': base, 'policyVersion': config.get('policyVersion'),
            'manifestDigest': config.get('manifestDigest'), 'environmentId': config.get('environmentId'),
            'projectId': t.get('projectId'), 'threadId': t.get('threadId'), 'countedRunId': selected.get('runId'),
            'countedRunOrdinal': selected.get('ordinal'), 'runCount': t.get('runCount'), 'runsRead': runs,
            'requestedAt': selected.get('requestedAt'), 'completedAt': selected.get('completedAt'),
            'requestedModel': selected.get('model'), 'executedModel': selected.get('executedModel'),
            'promptSha256': digest(prompt) if isinstance(prompt, str) else None,
            'reportSha256': digest(report_text) if isinstance(report_text, str) else None,
            'verdict': verdicts[-1] if verdicts else None, 't3ServerVersion': config.get('t3ServerVersion'),
            't3Build': 'unknown', 'launchedByCoordinator': config.get('launchedByCoordinator'),
            'collectedAt': config.get('collectedAt'), 'flags': sorted(flags), 'eligible': False,
            'authenticated': False, 'dispatchAllowed': False, 'publishAllowed': False,
            'mergeAllowed': False, 'canonicalFamily': None}


def main(argv):
    config = saved_json(argv[1]) if len(argv) == 2 else None
    if not config:
        result = collect({}, None, [], None)
    else:
        try:
            result = collect(config, saved_json(config.get('threadFile', '')), [saved_json(p) for p in config.get('listFiles', [])], saved_json(config.get('githubFile', '')))
        except (TypeError, ValueError, AttributeError, KeyError):
            result = collect({}, None, [], None)
    print(json.dumps(result, sort_keys=True, separators=(',', ':')))
    return 2


if __name__ == '__main__':
    sys.exit(main(sys.argv))
