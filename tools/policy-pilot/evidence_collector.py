"""Offline, unauthenticated evidence inspection. No external access or admission."""
import hashlib
import json
import os
import stat
import re
import sys
from pathlib import Path

LIMIT = 8 * 1024 * 1024


def saved_json(path):
    try:
        # Nonblocking open prevents a FIFO replacement from hanging this CLI.
        fd = os.open(path, os.O_RDONLY | os.O_NONBLOCK)
        try:
            if not stat.S_ISREG(os.fstat(fd).st_mode):
                return None
            with os.fdopen(fd, 'rb') as stream:
                fd = -1
                data = stream.read(LIMIT + 1)
            if len(data) > LIMIT:
                return None
            def reject_constant(value):
                raise ValueError('Invalid JSON constant: ' + value)
            value = json.loads(data.decode('utf-8'), parse_constant=reject_constant)
            return value if isinstance(value, dict) else None
        finally:
            if fd != -1: os.close(fd)
    except (Exception, MemoryError):
        return None


def digest(value):
    return hashlib.sha256(value.encode('utf-8')).hexdigest()


def collect(config, thread, pages, github):
    flags = {'FAMILY_UNKNOWN', 'SESSION_DISTINCTNESS_UNKNOWN'}
    if any(x is None for x in [thread, github, *pages]) or not pages:
        flags.add('FILE_TRUNCATED_OR_UNPARSABLE')
    thread = thread or {}; github = github or {}; pages = [p for p in pages if p is not None]
    t = thread.get('thread') or {}; runs = thread.get('recentRuns') or []; items = thread.get('items') or []
    if thread.get('hasMore') or thread.get('nextCursor') is not None or any(p.get('hasMore') or p.get('nextCursor') is not None for p in pages): flags.add('PAGINATION_INCOMPLETE')
    if type(t.get('runCount')) is not int: flags.add('RUN_COUNT_MISSING')
    elif t['runCount'] > len(runs): flags.add('RUNS_HIDDEN')
    if any(not r.get('executedModel') for r in runs): flags.add('MODEL_EXECUTED_ABSENT')
    if any(r.get('executedModel') and r.get('model') != r.get('executedModel') for r in runs): flags.add('MODEL_REQUESTED_EXECUTED_MISMATCH')
    if len({r.get('executedModel') for r in runs if r.get('executedModel')}) > 1: flags.add('MIXED_RUN_MODELS')
    if (type(t.get('runCount')) is int and t['runCount'] > 1) or len(runs) > 1: flags.add('EXTRA_RUN')
    if any(x.get('createdBy') == 'user' and x.get('creationSource') == 'web' for x in items): flags.add('HUMAN_RETRY')
    if any(x.get('textTruncated') for x in items): flags.add('TEXT_TRUNCATED')
    if any(x.get('parentThreadId') == t.get('threadId') for p in pages for x in p.get('threads', [])): flags.add('SUBAGENTS_PRESENT')
    candidates = {x.get('threadId') for p in pages for x in p.get('threads', []) if config.get('titleNonce') and config['titleNonce'] in x.get('title', '')}
    if len(candidates) > 1: flags.add('DUPLICATE_THREADS')
    if config.get('ambiguousLaunch'): flags.add('AMBIGUOUS_LAUNCH')
    if config.get('evidenceReopened'): flags.add('EVIDENCE_REOPENED')
    if not config.get('launchedByCoordinator'): flags.add('NOT_COORDINATOR_LAUNCHED')
    pr = github.get('pr') or {}; head = (pr.get('head') or {}).get('sha'); base = (pr.get('base') or {}).get('sha')
    if not config.get('titleNonce'): flags.add('TITLE_NONCE_MISSING')
    if not config.get('expectedBase'): flags.add('EXPECTED_BASE_MISSING')
    if not config.get('expectedHead'): flags.update(('HEAD_SELF_REPORTED_ONLY', 'EXPECTED_HEAD_MISSING'))
    if config.get('expectedHead') and head != config['expectedHead']: flags.add('HEAD_STALE')
    if config.get('expectedBase') and base != config['expectedBase']: flags.update(('BASE_MISMATCH', 'BASE_STALE'))
    statuses = github.get('statuses') or {}; contexts = statuses.get('statuses', [])
    if statuses.get('sha') != head: flags.add('STATUS_HEAD_MISMATCH')
    required = config.get('requiredContexts', [])
    if any(not [s for s in contexts if s.get('context') == c] or [s for s in contexts if s.get('context') == c][0].get('state') != 'success' for c in required): flags.add('CHECKS_FAILED')
    workflows = github.get('workflowRuns') or {}
    if isinstance(workflows, dict):
        if type(workflows.get('total_count')) is not int or workflows['total_count'] > len(workflows.get('workflow_runs', [])):
            flags.add('PAGINATION_INCOMPLETE')
        workflows = workflows.get('workflow_runs', [])
    else:
        flags.add('PAGINATION_INCOMPLETE')
    if any(w.get('status') != 'completed' or w.get('conclusion') != 'success' for w in workflows): flags.add('CHECKS_FAILED')
    binding = re.compile(r'^HVO-PR-CI v1 pr=(\d+) head=([a-f0-9]{40}) base=([a-f0-9]{40}) merge=([a-f0-9]{40})$')
    ci = []
    for w in workflows:
        match = binding.fullmatch(w.get('display_title', ''))
        if (w.get('path', '').split('/')[-1] == 'ci.yml' and match
                and (match.group(1), match.group(2), match.group(3), match.group(4)) ==
                    (str(config.get('prNumber')), head, base, config.get('expectedMerge')) and w.get('head_sha') == head):
            ci.append(w)
    if any(type(w.get('run_attempt')) is int and w['run_attempt'] >= 2 for w in ci): flags.add('CI_ATTEMPT_NOT_FIRST')
    if len({w.get('id') for w in ci if w.get('run_attempt') == 1}) > 3: flags.add('CI_RETRY_BUDGET_EXHAUSTED')
    selected = next((r for r in runs if r.get('runId') == config.get('countedRunId') and r.get('runId')), None)
    if selected is None: flags.add('COUNTED_RUN_NOT_FOUND')
    latest = max(runs, key=lambda x: x.get('ordinal') if type(x.get('ordinal')) is int else -1, default={})
    if selected and (selected.get('runId') != latest.get('runId') or selected.get('runId') != t.get('latestRunId')):
        flags.add('EVIDENCE_REOPENED')
    if selected and selected.get('status') != 'completed': flags.add('COUNTED_RUN_NOT_COMPLETED')
    selected = selected or {}
    reports = [x for x in items if x.get('type') == 'assistant_message' and x.get('runId') == selected.get('runId')]
    report = max(reports, key=lambda x: x.get('position', 0)) if reports else {}
    blocking = {'FILE_TRUNCATED_OR_UNPARSABLE', 'PAGINATION_INCOMPLETE', 'RUNS_HIDDEN', 'RUN_COUNT_MISSING', 'TEXT_TRUNCATED', 'DUPLICATE_THREADS', 'AMBIGUOUS_LAUNCH', 'EVIDENCE_REOPENED', 'COUNTED_RUN_NOT_FOUND', 'COUNTED_RUN_NOT_COMPLETED', 'TITLE_NONCE_MISSING', 'EXPECTED_BASE_MISSING', 'EXPECTED_HEAD_MISSING', 'HEAD_STALE', 'BASE_MISMATCH', 'STATUS_HEAD_MISMATCH'}
    report_text = report.get('text') if selected.get('runId') and not report.get('textTruncated') and report.get('status') == 'completed' and not (flags & blocking) else None
    verdicts = re.findall(r'(?m)^\s*(?:VERDICT\s*:\s*)?(CLEAN|CHANGES_REQUIRED|INCOMPLETE|BLOCKED)\s*$', report_text or '')
    if len(verdicts) > 1: flags.add('MULTIPLE_VERDICTS')
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
            'verdict': verdicts[0] if len(verdicts) == 1 else None, 't3ServerVersion': config.get('t3ServerVersion'),
            't3Build': 'unknown', 'launchedByCoordinator': config.get('launchedByCoordinator'),
            'collectedAt': config.get('collectedAt'), 'flags': sorted(flags), 'eligible': False,
            'authenticated': False, 'dispatchAllowed': False, 'publishAllowed': False,
            'mergeAllowed': False, 'canonicalFamily': None}


def main(argv):
    try:
        config = saved_json(argv[1]) if len(argv) == 2 else None
        if not config:
            result = collect({}, None, [], None)
        else:
            result = collect(config, saved_json(config.get('threadFile', '')), [saved_json(p) for p in config.get('listFiles', [])], saved_json(config.get('githubFile', '')))
        output = json.dumps(result, sort_keys=True, separators=(',', ':'), allow_nan=False)
    except (Exception, MemoryError):
        output = json.dumps(collect({}, None, [], None), sort_keys=True, separators=(',', ':'), allow_nan=False)
    print(output)
    return 2


if __name__ == '__main__':
    sys.exit(main(sys.argv))
