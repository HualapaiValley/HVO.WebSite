import concurrent.futures
import contextlib
import importlib.util
from pathlib import Path
import subprocess
import sqlite3
import sys
import tempfile
import unittest
from unittest.mock import patch

MODULE = Path(__file__).with_name('journal.py')
spec = importlib.util.spec_from_file_location('pilot_journal', MODULE)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
Journal, Blocked = module.Journal, module.Blocked
BINDING = {'repository': 'HualapaiValley/HVO.WebSite', 'issue': 'fixture',
           'assignmentRevision': '1', 'base': '2' * 40, 'head': '1' * 40,
           'branch': 'fixture', 'scope': 'local fixture only', 'endpoint': 'implementation',
           'risk': 'high', 'policyVersion': '2026-11-16.3', 'profileVersion': 'fixture-1'}


class JournalTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='hvo-pilot-journal-')
        self.path = Path(self.temp.name) / 'state.sqlite'
        self.journal = Journal.create(self.path, 'owner-a')
        self.journal.claim('owner-a', 1, 'work-1', BINDING)

    def tearDown(self):
        self.temp.cleanup()

    def prepare(self):
        return self.journal.prepare_attempt('owner-a', 1, 'work-1', 'attempt-1')

    def event(self, **changes):
        return dict({'event_id': 'event-1', 'attempt_id': 'attempt-1', 'work_key': 'work-1',
                     'generation': 1, 'source_head': BINDING['head'], 'source_base': BINDING['base'], 'kind': 'succeeded'}, **changes)

    def child(self, code):
        bootstrap = ('import importlib.util,os; '
                     f's=importlib.util.spec_from_file_location("j",{str(MODULE)!r}); '
                     'm=importlib.util.module_from_spec(s); s.loader.exec_module(m); '
                     f'j=m.Journal({str(self.path)!r}); ')
        return subprocess.run([sys.executable, '-c', bootstrap + code], capture_output=True, text=True, timeout=10)

    def test_explicit_create_never_overwrites_and_permissions_are_private(self):
        with self.assertRaises(FileExistsError):
            Journal.create(self.path, 'other-owner')
        self.assertEqual(0o600, self.path.stat().st_mode & 0o777)
        missing = Journal(Path(self.temp.name) / 'missing.sqlite')
        with self.assertRaises(sqlite3.OperationalError):
            missing.recover()
        self.assertFalse(missing.path.exists())

    def test_intent_survives_process_exit_and_never_replays(self):
        result = self.child('j.prepare_attempt("owner-a",1,"work-1","attempt-1"); os._exit(0)')
        self.assertEqual(0, result.returncode, result.stderr)
        recovered = Journal(self.path).recover()
        self.assertEqual('AMBIGUOUS', recovered['attempts'][0]['state'])
        self.assertEqual(BINDING, recovered['work'][0]['binding'])
        self.assertTrue(recovered['attempts'][0]['created_at'])
        self.assertFalse(recovered['dispatch_allowed'])
        self.assertFalse(recovered['merge_allowed'])
        self.assertFalse(self.prepare()['created'])
        with self.assertRaises(Blocked):
            self.journal.prepare_attempt('owner-a', 1, 'work-1', 'attempt-2')

    def test_uncommitted_crash_rolls_back(self):
        result = self.child('c=j.connection(); db=c.__enter__(); db.execute("BEGIN IMMEDIATE"); '
                            'db.execute("INSERT INTO work VALUES (?,?)",("uncommitted","{}")); os._exit(0)')
        self.assertEqual(0, result.returncode, result.stderr)
        with self.assertRaises(Blocked):
            self.journal.prepare_attempt('owner-a', 1, 'uncommitted', 'attempt-2')

    def test_concurrent_intents_cannot_create_two_attempts(self):
        def attempt(number):
            try:
                return Journal(self.path).prepare_attempt('owner-a', 1, 'work-1', f'attempt-{number}')['created']
            except Blocked:
                return False
        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
            self.assertEqual(1, sum(pool.map(attempt, [1, 2])))
        self.assertEqual(1, len(self.journal.recover()['attempts']))

    def test_duplicate_callback_is_durable_and_collision_fails(self):
        self.prepare()
        first = self.journal.observe('owner-a', 1, self.event())
        self.assertTrue(first['persisted'])
        self.assertFalse(first['trusted'])
        self.assertTrue(Journal(self.path).observe('owner-a', 1, self.event())['duplicate'])
        with self.assertRaises(Blocked):
            self.journal.observe('owner-a', 1, self.event(kind='failed'))
        self.assertEqual(1, len(self.journal.recover()['events']))

    def test_commit_failure_cannot_acknowledge_callback(self):
        self.prepare()
        connection = self.journal.connection

        @contextlib.contextmanager
        def broken_connection():
            with connection() as db:
                class FailedCommit:
                    execute = db.execute

                    def commit(self):
                        raise sqlite3.OperationalError('injected write failure')

                yield FailedCommit()

        with patch.object(self.journal, 'connection', broken_connection):
            with self.assertRaises(sqlite3.OperationalError):
                self.journal.observe('owner-a', 1, self.event())
        self.assertEqual([], self.journal.recover()['events'])

    def test_callback_commit_survives_lost_acknowledgment(self):
        self.prepare()
        result = self.child(f'j.observe("owner-a",1,{self.event()!r}); os._exit(0)')
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertTrue(self.journal.observe('owner-a', 1, self.event())['duplicate'])
        self.assertEqual(1, len(self.journal.recover()['events']))

    def test_wrong_owner_or_generation_cannot_mutate(self):
        self.prepare()
        before = self.path.read_bytes()
        for owner, generation in [('owner-b', 1), ('owner-a', 2), ('owner-a', True)]:
            with self.assertRaises(Blocked):
                self.journal.observe(owner, generation, self.event())
            with self.assertRaises(Blocked):
                self.journal.prepare_attempt(owner, generation, 'work-1', 'attempt-1')
        self.assertEqual(before, self.path.read_bytes())

    def test_unknown_attempt_stale_generation_and_changed_head_are_quarantined(self):
        self.prepare()
        for i, changes in enumerate([{'attempt_id': 'unknown'}, {'generation': 2}, {'generation': True}, {'source_head': '3' * 40}, {'source_base': '4' * 40}]):
            self.journal.observe('owner-a', 1, self.event(event_id=f'e-{i}', **changes))
        self.assertTrue(all(x['disposition'].startswith('quarantined:') for x in self.journal.recover()['events']))
        with self.assertRaises(Blocked):
            self.journal.claim('owner-a', 1, 'work-1', {**BINDING, 'head': '3' * 40})

    def test_reported_success_absence_and_provider_failure_never_release_ambiguity(self):
        self.prepare()
        for i, kind in enumerate(['succeeded', 'absent', 'provider-failure', 'started', 'ci-failed', 'merged']):
            self.journal.observe('owner-a', 1, self.event(event_id=f'e-{i}', kind=kind, authenticated=True))
        recovered = self.journal.recover()
        self.assertEqual('AMBIGUOUS', recovered['attempts'][0]['state'])
        self.assertFalse(recovered['dispatch_allowed'])
        self.assertEqual(6, len(recovered['events']))
        with self.assertRaises(Blocked):
            self.journal.prepare_attempt('owner-a', 1, 'work-1', 'retry')

    def test_other_work_cannot_bypass_unresolved_domain_intent(self):
        self.prepare()
        self.journal.claim('owner-a', 1, 'work-2', {**BINDING, 'issue': 'other'})
        with self.assertRaises(Blocked):
            self.journal.prepare_attempt('owner-a', 1, 'work-2', 'attempt-2')
        with self.assertRaises(Blocked):
            self.journal.prepare_attempt('owner-a', 1, 'work-2', 'attempt-1')

    def test_recovery_without_callbacks_is_read_only(self):
        self.prepare()
        before = self.path.read_bytes()
        self.assertEqual([], Journal(self.path).recover()['events'])
        self.assertEqual(before, self.path.read_bytes())

    def test_invalid_schema_symlink_and_oversized_event_fail_closed(self):
        self.prepare()
        with self.assertRaises(ValueError):
            self.journal.observe('owner-a', 1, self.event(payload='x' * 65536))
        link = Path(self.temp.name) / 'link.sqlite'
        link.symlink_to(self.path)
        with self.assertRaises(ValueError):
            Journal(link)
        with self.journal.connection() as db:
            db.execute('PRAGMA user_version=2')
        with self.assertRaises(Blocked):
            self.journal.recover()


if __name__ == '__main__':
    unittest.main()
