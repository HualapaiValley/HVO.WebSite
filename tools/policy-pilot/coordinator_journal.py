"""Disabled local coordinator records; no transport or recovery execution."""
import hashlib
import json
import os
import re
from datetime import datetime, timedelta, timezone
from journal import Blocked, Journal, encoded, text

TRANSITIONS = {'INTENT': {'LAUNCH_SENT', 'BLOCKED'}, 'LAUNCH_SENT': {'LAUNCHED', 'BLOCKED', 'DUPLICATE'},
               'LAUNCHED': {'TERMINAL', 'REOPENED', 'BLOCKED'}, 'TERMINAL': {'EVIDENCE_COLLECTED', 'REOPENED', 'BLOCKED'},
               'EVIDENCE_COLLECTED': {'REOPENED', 'BLOCKED'}, 'REOPENED': {'TERMINAL', 'BLOCKED'},
               'BLOCKED': set(), 'DUPLICATE': set()}
WINDOW_MINUTES = 10
SCAN_MINUTES = (0, 2, 10)


def timestamp(value):
    result = datetime.fromisoformat(value.replace('Z', '+00:00'))
    if result.tzinfo is None or result.utcoffset() is None:
        raise ValueError('Timestamp offset required')
    return result.astimezone(timezone.utc)


class CoordinatorJournal(Journal):
    @classmethod
    def create(cls, path, owner):
        j = cls(path)
        text(owner)
        fd = os.open(j.path, os.O_CREAT | os.O_EXCL | os.O_RDWR, 0o600)
        os.close(fd)
        with j.connection(initializing=True) as db:
            db.executescript('''BEGIN IMMEDIATE;
                CREATE TABLE domain (owner TEXT NOT NULL, generation INTEGER NOT NULL);
                CREATE TABLE coordinator_attempts (
                attempt_id TEXT PRIMARY KEY, work_key TEXT NOT NULL, binding TEXT NOT NULL, state TEXT NOT NULL,
                thread_id TEXT, launch_sent_at TEXT, flags TEXT NOT NULL, run_id TEXT,
                ordinal INTEGER, evidence_void INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE coordinator_work (work_key TEXT PRIMARY KEY, binding TEXT NOT NULL);
                CREATE TABLE coordinator_scans (attempt_id TEXT NOT NULL, read_at TEXT NOT NULL, PRIMARY KEY(attempt_id, read_at));
                CREATE TABLE coordinator_requests (request_id TEXT PRIMARY KEY, attempt_id TEXT NOT NULL UNIQUE, binding TEXT NOT NULL);
                CREATE TABLE coordinator_events (attempt_id TEXT NOT NULL, event_key TEXT NOT NULL, digest TEXT NOT NULL,
                payload TEXT NOT NULL, disposition TEXT NOT NULL, PRIMARY KEY(attempt_id,event_key));
                PRAGMA user_version=3;''')
            db.execute('INSERT INTO domain VALUES (?, 1)', (owner,))
            db.commit()
        parent = os.open(j.path.parent, os.O_RDONLY)
        try:
            os.fsync(parent)
        finally:
            os.close(parent)
        return j

    def connection(self, initializing=False, readonly=False):
        # Base connection validates schema 1; validate our schema separately while
        # retaining identical connection and file safety behavior.
        import contextlib
        import sqlite3
        from urllib.parse import quote
        @contextlib.contextmanager
        def opened():
            if self.path.is_symlink():
                raise Blocked('Journal symlink rejected')
            db = sqlite3.connect(f'file:{quote(str(self.path))}?mode={"ro" if readonly else "rw"}', uri=True, timeout=1)
            db.row_factory = sqlite3.Row
            try:
                db.execute('PRAGMA foreign_keys=ON')
                if not readonly:
                    db.execute('PRAGMA synchronous=FULL')
                if not initializing and db.execute('PRAGMA user_version').fetchone()[0] != 3:
                    raise Blocked('Unknown or incomplete journal schema')
                yield db
            finally:
                db.close()
        return opened()

    def intent(self, owner, generation, attempt_id, binding):
        text(attempt_id)
        if not re.fullmatch(r'[a-z0-9-]{1,64}', attempt_id):
            raise Blocked('Invalid attempt ID')
        keys = ('clientRequestId', 'titleNonce', 'environmentId', 'projectId', 'providerInstanceId',
                'model', 'effort', 'runtimeMode', 'promptSha256', 'intentAt', 'head', 'base', 'workKey')
        if not isinstance(binding, dict):
            raise ValueError('Binding required')
        for key in keys:
            text(binding.get(key))
        if binding['titleNonce'] != f'[uwf:{attempt_id}]' or type(binding.get('generation')) is not int or binding.get('generation') != generation:
            raise Blocked('Invalid nonce or generation')
        timestamp(binding['intentAt'])
        if any(not re.fullmatch(r'[a-f0-9]{40}', binding[k]) for k in ('head', 'base')) or not re.fullmatch(r'[a-f0-9]{64}', binding['promptSha256']):
            raise Blocked('Immutable source and prompt digests required')
        work_binding = binding.get('workBinding')
        if not isinstance(work_binding, dict) or work_binding.get('head') != binding['head'] or work_binding.get('base') != binding['base']:
            raise Blocked('Immutable work binding with matching source required')
        work_body = encoded(work_binding)
        body = encoded(binding)
        with self.connection() as db:
            db.execute('BEGIN IMMEDIATE'); self.owner(db, owner, generation)
            work = db.execute('SELECT binding FROM coordinator_work WHERE work_key=?', (binding['workKey'],)).fetchone()
            if work and work['binding'] != work_body:
                raise Blocked('Work binding changed')
            db.execute('INSERT OR IGNORE INTO coordinator_work VALUES (?,?)', (binding['workKey'], work_body))
            old = db.execute('SELECT binding FROM coordinator_attempts WHERE attempt_id=?', (attempt_id,)).fetchone()
            if old and old[0] != body:
                raise Blocked('Attempt binding changed')
            request = db.execute('SELECT attempt_id, binding FROM coordinator_requests WHERE request_id=?', (binding['clientRequestId'],)).fetchone()
            if request and (request['attempt_id'] != attempt_id or request['binding'] != body):
                raise Blocked('Replay key binding changed')
            if not old:
                unresolved = db.execute("SELECT 1 FROM coordinator_attempts WHERE work_key=? AND state IN ('INTENT','LAUNCH_SENT','LAUNCHED','REOPENED','DUPLICATE','BLOCKED')", (binding['workKey'],)).fetchone()
                if unresolved:
                    raise Blocked('Unresolved attempt for work')
                db.execute('INSERT INTO coordinator_requests VALUES (?,?,?)', (binding['clientRequestId'], attempt_id, body))
                db.execute('INSERT INTO coordinator_attempts(attempt_id,work_key,binding,state,flags) VALUES (?,?,?,?,?)',
                           (attempt_id, binding['workKey'], body, 'INTENT', '[]'))
            db.commit()
        return {'dispatch_allowed': False, 'replay_allowed': False}

    @staticmethod
    def _transition(db, row, state, **fields):
        if state not in TRANSITIONS.get(row['state'], set()):
            raise Blocked('Invalid transition')
        allowed = {'thread_id', 'launch_sent_at', 'flags', 'run_id', 'ordinal', 'evidence_void'}
        if not set(fields) <= allowed:
            raise ValueError('Invalid field')
        if state == 'REOPENED':
            fields['evidence_void'] = 1
        if 'flags' in fields:
            encoded(json.loads(fields['flags']))
        assignments = ['state=?'] + [f'{key}=?' for key in fields]
        db.execute('UPDATE coordinator_attempts SET ' + ','.join(assignments) + ' WHERE attempt_id=?',
                   [state, *fields.values(), row['attempt_id']])

    def transition(self, owner, generation, attempt_id, state, **fields):
        with self.connection() as db:
            db.execute('BEGIN IMMEDIATE'); self.owner(db, owner, generation)
            row = db.execute('SELECT * FROM coordinator_attempts WHERE attempt_id=?', (attempt_id,)).fetchone()
            if not row:
                raise Blocked('Unknown attempt')
            allowed_fields = {'LAUNCH_SENT': {'launch_sent_at', 'now', 'thread_id'}, 'LAUNCHED': {'thread_id'},
                              'BLOCKED': set(), 'EVIDENCE_COLLECTED': set()}
            if state not in allowed_fields or not set(fields) <= allowed_fields[state]:
                raise Blocked('Transition reserved for observation or resolver')
            if state == 'LAUNCH_SENT':
                sent = timestamp(fields.get('launch_sent_at', ''))
                now = timestamp(fields.pop('now', ''))
                if not timestamp(json.loads(row['binding'])['intentAt']) <= sent <= now:
                    raise Blocked('Launch timestamp outside recorded interval')
                if 'thread_id' in fields: text(fields['thread_id'])
            if state == 'LAUNCHED':
                text(fields.get('thread_id'))
                if row['thread_id'] and row['thread_id'] != fields['thread_id']:
                    raise Blocked('Launch response thread changed')
            self._transition(db, row, state, **fields); db.commit()
        return {'dispatch_allowed': False, 'replay_allowed': False}

    def resolve_from_hub_reads(self, owner, generation, attempt_id, pages, reads, now):
        with self.connection() as db:
            db.execute('BEGIN IMMEDIATE'); self.owner(db, owner, generation)
            row = db.execute('SELECT * FROM coordinator_attempts WHERE attempt_id=?', (attempt_id,)).fetchone()
            if not row or row['state'] != 'LAUNCH_SENT':
                raise Blocked('Expected ambiguous launch')
            b = json.loads(row['binding']); now_at = timestamp(now)
            if timestamp(row['launch_sent_at']) > now_at:
                raise Blocked('Read time precedes launch sent')
            for obj in [*pages, *reads]:
                if obj.get('coordinatorRecorded') is not True:
                    raise Blocked('Coordinator-recorded reads required')
                if not timestamp(row['launch_sent_at']) <= timestamp(obj.get('readAt', '')) <= now_at:
                    raise Blocked('Saved read outside recorded interval')
            candidates = {t['threadId']: t for p in pages for t in p.get('threads', [])
                          if b['titleNonce'] in t.get('title', '') and t.get('threadId')}
            request = pages[0].get('request', {}) if len(pages) == 1 else {}
            complete = (len(pages) == 1 and not pages[0].get('hasMore') and pages[0].get('nextCursor') is None
                        and (type(pages[0].get('total')) is not int or pages[0]['total'] <= len(pages[0].get('threads', [])))
                        and request.get('environmentId') == b['environmentId'] and request.get('allProjects') is True
                        and request.get('includeSubagents') is True and request.get('titleContains') == b['titleNonce']
                        and not request.get('statuses') and request.get('cursor') in (None, 0))
            flags = []; state = 'LAUNCH_SENT'; thread_id = None
            if len(candidates) >= 2:
                state = 'DUPLICATE'; flags = ['DUPLICATE_THREADS']; thread_id = json.dumps(sorted(candidates))
            elif len(candidates) == 1:
                tid = next(iter(candidates)); r = next((x for x in reads if x.get('thread', {}).get('threadId') == tid), {})
                t = r.get('thread', {}); listed = candidates[tid]; messages = [x for x in r.get('items', []) if x.get('type') == 'user_message']
                first = min(messages, key=lambda x: x.get('position', 0)) if messages else {}
                prompt = first.get('text')
                try:
                    time_ok = timestamp(b['intentAt']) <= timestamp(t.get('createdAt', '')) <= timestamp(row['launch_sent_at']) + timedelta(minutes=WINDOW_MINUTES)
                except (ValueError, TypeError):
                    time_ok = False
                full = ((row['thread_id'] is None or row['thread_id'] == tid)
                        and r.get('environmentId') == b['environmentId']
                        and (t.get('environmentId') is None or t['environmentId'] == b['environmentId'])
                        and all(listed.get(k) is None or t.get(k) is None or listed[k] == t[k] for k in ('environmentId', 'projectId', 'title', 'createdAt', 'createdBy', 'creationSource', 'providerInstanceId', 'model', 'runtimeMode', 'effort')) and t.get('projectId') == b['projectId']
                        and b['titleNonce'] in t.get('title', '') and time_ok
                        and t.get('creationSource') == 'mcp' and t.get('createdBy') == 'agent'
                        and all(t.get(k) is None or t[k] == b[k] for k in ('providerInstanceId', 'runtimeMode', 'model', 'effort'))
                        and all(all(x.get(k) is None or x[k] == b[k] for k in ('providerInstanceId', 'effort')) for x in r.get('recentRuns', []))
                        and isinstance(prompt, str) and not first.get('textTruncated')
                        and hashlib.sha256(prompt.encode()).hexdigest() == b['promptSha256']
                        and bool(r.get('recentRuns')) and all(x.get('model') == b['model'] for x in r['recentRuns'])
                        and not r.get('hasMore') and r.get('nextCursor') is None
                        and type(t.get('runCount')) is int and t['runCount'] == len(r.get('recentRuns', [])))
                if full and complete:
                    state = 'LAUNCHED'; flags = ['RECOVERED_BY_BINDINGS']; thread_id = tid
                else:
                    flags = ['PARTIAL_BINDING_MATCH']
            else:
                for page in pages if complete else []:
                    db.execute('INSERT OR IGNORE INTO coordinator_scans VALUES (?,?)', (attempt_id, page['readAt']))
                scans = [timestamp(x[0]) for x in db.execute('SELECT read_at FROM coordinator_scans WHERE attempt_id=?', (attempt_id,))]
                start = timestamp(row['launch_sent_at'])
                if all(any(start + timedelta(minutes=n) <= x <= timestamp(now) for x in scans) for n in SCAN_MINUTES) and len(set(scans)) >= len(SCAN_MINUTES):
                    state = 'BLOCKED'; flags = ['SCAN_SCHEDULE_EXHAUSTED']
            if state != 'LAUNCH_SENT':
                self._transition(db, row, state, flags=encoded(flags), thread_id=thread_id)
            else:
                db.execute('UPDATE coordinator_attempts SET flags=? WHERE attempt_id=?', (encoded(flags), attempt_id))
            db.commit()
        return {'state': state, 'ambiguous': state == 'LAUNCH_SENT', 'flags': flags,
                'dispatch_allowed': False, 'replay_allowed': False}

    def observe_run(self, owner, generation, attempt_id, event):
        body = encoded(event); digest = hashlib.sha256(body.encode()).hexdigest()
        key = encoded([event.get(k) for k in ('threadId', 'runId', 'status', 'completedAt')])
        with self.connection() as db:
            db.execute('BEGIN IMMEDIATE'); self.owner(db, owner, generation)
            old = db.execute('SELECT digest, disposition FROM coordinator_events WHERE attempt_id=? AND event_key=?', (attempt_id, key)).fetchone()
            if old and old['digest'] != digest:
                raise Blocked('Conflicting run event')
            if old and old['disposition'] != 'quarantined':
                return {'duplicate': True, 'dispatch_allowed': False, 'replay_allowed': False}
            row = db.execute('SELECT * FROM coordinator_attempts WHERE attempt_id=?', (attempt_id,)).fetchone()
            disposition = 'recorded'
            if (not row or row['thread_id'] != event.get('threadId') or type(event.get('generation')) is not int
                    or event.get('generation') != generation or row['state'] not in ('LAUNCHED','TERMINAL','EVIDENCE_COLLECTED','REOPENED')
                    or event.get('sourceHead') != json.loads(row['binding']).get('head')
                    or event.get('sourceBase') != json.loads(row['binding']).get('base')):
                disposition = 'quarantined'
            else:
                run_id, ordinal = event.get('runId'), event.get('ordinal')
                if not isinstance(run_id, str) or not run_id or type(ordinal) is not int or ordinal < 1:
                    if row['state'] != 'BLOCKED': self._transition(db, row, 'BLOCKED', evidence_void=1, flags=encoded(['EVIDENCE_REOPENED']))
                    disposition = 'blocked:unknown-run-ordinal'
                else:
                    current_id, current_ordinal = row['run_id'], row['ordinal'] or 1
                    if current_id and run_id == current_id and ordinal != current_ordinal:
                        self._transition(db, row, 'BLOCKED', evidence_void=1, flags=encoded(['EVIDENCE_REOPENED'])); disposition = 'blocked:run-ordinal-conflict'
                    elif current_id and run_id != current_id and ordinal == current_ordinal:
                        self._transition(db, row, 'BLOCKED', evidence_void=1, flags=encoded(['EVIDENCE_REOPENED'])); disposition = 'blocked:run-ordinal-conflict'
                    elif current_id and run_id != current_id and ordinal < current_ordinal:
                        disposition = 'quarantined'
                    elif (current_id and run_id != current_id) or (not current_id and ordinal > 1):
                        flags = set(json.loads(row['flags'])) | {'EXTRA_RUN', 'EVIDENCE_REOPENED'}
                        if event.get('createdBy') == 'user' and event.get('creationSource') == 'web': flags.add('HUMAN_RETRY')
                        if row['state'] == 'REOPENED':
                            db.execute('UPDATE coordinator_attempts SET run_id=?, ordinal=?, evidence_void=1, flags=? WHERE attempt_id=?', (run_id, ordinal, encoded(sorted(flags)), attempt_id))
                        else:
                            self._transition(db, row, 'REOPENED', flags=encoded(sorted(flags)), run_id=run_id, ordinal=ordinal)
                        if event.get('status') in ('completed','failed','cancelled','interrupted','rolled_back'):
                            disposition = 'quarantined'  # Re-read after the reopen before accepting terminal.
                    elif row['state'] in ('LAUNCHED', 'REOPENED'):
                        if event.get('status') in ('completed','failed','cancelled','interrupted','rolled_back'):
                            self._transition(db, row, 'TERMINAL', run_id=run_id, ordinal=ordinal)
                        elif not current_id:
                            db.execute('UPDATE coordinator_attempts SET run_id=?, ordinal=? WHERE attempt_id=?', (run_id, ordinal, attempt_id))
            db.execute('INSERT OR REPLACE INTO coordinator_events VALUES (?,?,?,?,?)', (attempt_id, key, digest, body, disposition)); db.commit()
        return {'duplicate': False, 'disposition': disposition, 'dispatch_allowed': False, 'replay_allowed': False}

    def recover(self):
        with self.connection(readonly=True) as db:
            rows = [dict(x) for x in db.execute('SELECT * FROM coordinator_attempts ORDER BY attempt_id')]
        for row in rows:
            row['ambiguous'] = row['state'] == 'LAUNCH_SENT'
            row['re_read'] = ([f"thread:{row['thread_id']}"] if row['thread_id'] and row['state'] != 'DUPLICATE' else ['nonce scan']) + ['PR', 'statuses', 'branch tips']
        return {'attempts': rows, 'dispatch_allowed': False, 'replay_allowed': False, 'merge_allowed': False}

    def recovery_plan(self):
        return {'recovery': self.recover(), 'resume_allowed': False, 'dispatch_allowed': False,
                'merge_allowed': False, 'next_action': 'Inspect saved reads and reconcile manually; no resume is implemented'}
