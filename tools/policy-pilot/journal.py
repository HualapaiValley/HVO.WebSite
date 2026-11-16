"""Disabled single-host journal. No network, dispatch, merge, or trusted receipt adapter.

Observations are unverified and cannot clear an ambiguous attempt. SQLite provides
transactional durability for an owned local file; this is not cross-host fencing
or protection against an actor who can edit that file. No import-time writes.
"""
import contextlib
import hashlib
import json
import os
import re
from pathlib import Path
import sqlite3
from urllib.parse import quote


class Blocked(RuntimeError):
    pass


def text(value):
    if not isinstance(value, str) or not value.strip() or len(value) > 4096:
        raise ValueError('Nonempty bounded string required')
    return value


def encoded(value):
    data = json.dumps(value, sort_keys=True, separators=(',', ':'), allow_nan=False)
    if len(data.encode()) > 65536:
        raise ValueError('Record exceeds 64 KiB')
    return data


class Journal:
    def __init__(self, path):
        self.path = Path(path)
        if not self.path.is_absolute() or self.path.is_symlink():
            raise ValueError('Absolute owned local database path required; no symlinks')

    @classmethod
    def create(cls, path, owner):
        journal = cls(path)
        text(owner)
        # Explicit creation only: never overwrite an existing journal.
        fd = os.open(journal.path, os.O_CREAT | os.O_EXCL | os.O_RDWR, 0o600)
        os.close(fd)
        with journal.connection(initializing=True) as db:
            db.executescript('''
                BEGIN IMMEDIATE;
                CREATE TABLE domain(owner TEXT NOT NULL, generation INTEGER NOT NULL);
                CREATE TABLE work(
                    work_key TEXT PRIMARY KEY, binding TEXT NOT NULL);
                CREATE TABLE attempts(
                    attempt_id TEXT PRIMARY KEY, work_key TEXT NOT NULL UNIQUE,
                    owner TEXT NOT NULL, generation INTEGER NOT NULL,
                    state TEXT NOT NULL CHECK(state = 'AMBIGUOUS'),
                    created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
                    FOREIGN KEY(work_key) REFERENCES work(work_key));
                CREATE TABLE events(
                    sequence INTEGER PRIMARY KEY AUTOINCREMENT,
                    event_id TEXT NOT NULL UNIQUE,
                    attempt_id TEXT NOT NULL, payload TEXT NOT NULL,
                    digest TEXT NOT NULL, disposition TEXT NOT NULL,
                    recorded_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')));
                PRAGMA user_version=1;
            ''')
            db.execute('INSERT INTO domain VALUES (?, 1)', (owner,))
            db.commit()
        # Ensure the newly created directory entry is persisted as well.
        parent = os.open(journal.path.parent, os.O_RDONLY)
        try:
            os.fsync(parent)
        finally:
            os.close(parent)
        return journal

    @contextlib.contextmanager
    def connection(self, initializing=False, readonly=False):
        if self.path.is_symlink():
            raise Blocked('Journal symlink rejected')
        mode = 'ro' if readonly else 'rw'
        db = sqlite3.connect(f'file:{quote(str(self.path))}?mode={mode}', uri=True, timeout=1)
        db.row_factory = sqlite3.Row
        try:
            db.execute('PRAGMA foreign_keys=ON')
            if not readonly:
                db.execute('PRAGMA synchronous=FULL')
            if not initializing and db.execute('PRAGMA user_version').fetchone()[0] != 1:
                raise Blocked('Unknown or incomplete journal schema')
            yield db
        finally:
            db.close()  # Uncommitted mutations roll back, including on interruption.

    @staticmethod
    def owner(db, owner, generation):
        if not isinstance(owner, str) or type(generation) is not int:
            raise Blocked('Owner string and integer generation required')
        row = db.execute('SELECT owner, generation FROM domain').fetchone()
        if not row or row['owner'] != owner or row['generation'] != generation:
            raise Blocked('Wrong owner/generation; no automatic takeover')

    def claim(self, owner, generation, work_key, binding):
        text(work_key)
        if not isinstance(binding, dict) or not all(
            isinstance(binding.get(k), str) and binding[k].strip()
            for k in ('repository', 'issue', 'assignmentRevision', 'base', 'head',
                      'branch', 'scope', 'endpoint', 'risk', 'policyVersion', 'profileVersion')
        ):
            raise ValueError('Complete assignment, source and policy/profile binding required')
        if (binding['risk'] not in ('low', 'standard', 'high')
                or binding['endpoint'] not in ('research', 'implementation', 'pr-ready', 'merged', 'deployed')
                or not all(re.fullmatch('[a-f0-9]{40}', binding[k]) for k in ('base', 'head'))):
            raise ValueError('Known risk/endpoint and immutable base/head SHAs required')
        body = encoded(binding)
        with self.connection() as db:
            db.execute('BEGIN IMMEDIATE')
            self.owner(db, owner, generation)
            old = db.execute('SELECT binding FROM work WHERE work_key=?', (work_key,)).fetchone()
            if old and old['binding'] != body:
                raise Blocked('Work binding changed; reconcile explicitly')
            db.execute('INSERT OR IGNORE INTO work VALUES (?, ?)', (work_key, body))
            db.commit()

    def prepare_attempt(self, owner, generation, work_key, attempt_id):
        text(attempt_id)
        with self.connection() as db:
            db.execute('BEGIN IMMEDIATE')
            self.owner(db, owner, generation)
            old = db.execute('SELECT * FROM attempts WHERE attempt_id=?', (attempt_id,)).fetchone()
            if old:
                if old['work_key'] != work_key:
                    raise Blocked('Attempt ID belongs to different work')
                return {'attempt_id': attempt_id, 'created': False, 'dispatch_allowed': False}
            if db.execute('SELECT 1 FROM attempts LIMIT 1').fetchone():
                raise Blocked('Unresolved intent exists; no second attempt or dispatch')
            if not db.execute('SELECT 1 FROM work WHERE work_key=?', (work_key,)).fetchone():
                raise Blocked('Claim required before intent')
            # Commit ambiguity BEFORE any hypothetical transport. On restart we
            # cannot distinguish a crash before launch from remote success.
            db.execute('INSERT INTO attempts(attempt_id,work_key,owner,generation,state) VALUES (?, ?, ?, ?, ?)',
                       (attempt_id, work_key, owner, generation, 'AMBIGUOUS'))
            db.commit()
        return {'attempt_id': attempt_id, 'created': True, 'dispatch_allowed': False}

    def observe(self, owner, generation, event):
        if not isinstance(event, dict):
            raise ValueError('Event object required')
        for key in ('event_id', 'attempt_id', 'work_key', 'source_head', 'source_base', 'kind'):
            text(event.get(key))
        body = encoded(event)
        digest = hashlib.sha256(body.encode()).hexdigest()
        with self.connection() as db:
            db.execute('BEGIN IMMEDIATE')
            self.owner(db, owner, generation)
            old = db.execute('SELECT digest FROM events WHERE event_id=?', (event['event_id'],)).fetchone()
            if old:
                if old['digest'] != digest:
                    raise Blocked('Event ID reused with different content')
                return {'persisted': True, 'duplicate': True, 'trusted': False}
            attempt = db.execute('SELECT * FROM attempts WHERE attempt_id=?', (event['attempt_id'],)).fetchone()
            disposition = 'unverified'
            if not attempt or attempt['work_key'] != event['work_key']:
                disposition = 'quarantined:unknown-attempt-or-work'
            else:
                binding = json.loads(db.execute('SELECT binding FROM work WHERE work_key=?', (attempt['work_key'],)).fetchone()[0])
                if (type(event.get('generation')) is not int or event['generation'] != attempt['generation']
                        or event['source_head'] != binding['head'] or event['source_base'] != binding['base']):
                    disposition = 'quarantined:stale-generation-or-source'
            db.execute('INSERT INTO events(event_id,attempt_id,payload,digest,disposition) VALUES (?,?,?,?,?)',
                       (event['event_id'], event['attempt_id'], body, digest, disposition))
            db.commit()  # The returned persistence acknowledgment follows commit.
        # Even a reported success/absence is not authenticated and cannot change
        # AMBIGUOUS, release ownership, complete work or permit another attempt.
        return {'persisted': True, 'duplicate': False, 'trusted': False}

    def recover(self):
        with self.connection(readonly=True) as db:
            db.execute('BEGIN')  # Consistent read snapshot across all tables.
            domain = dict(db.execute('SELECT * FROM domain').fetchone())
            work = [{'work_key': x['work_key'], 'binding': json.loads(x['binding'])}
                    for x in db.execute('SELECT * FROM work ORDER BY work_key')]
            attempts = [dict(x) for x in db.execute('SELECT * FROM attempts ORDER BY attempt_id')]
            events = [dict(x) for x in db.execute('SELECT * FROM events ORDER BY sequence')]
        return {'mode': 'offline-disabled', 'domain': domain, 'work': work, 'attempts': attempts,
                'events': events, 'dispatch_allowed': False, 'merge_allowed': False,
                'next_action': 'Reconcile authoritative outcomes; trusted adapter unavailable'}
