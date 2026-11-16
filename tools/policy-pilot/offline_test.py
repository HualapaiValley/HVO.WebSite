import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from coordinator_journal import CoordinatorJournal, Blocked, TRANSITIONS
from evidence_collector import collect as collect_raw, saved_json

def collect(*args):
    result = collect_raw(*args)
    for key in ('eligible', 'authenticated', 'dispatchAllowed', 'publishAllowed', 'mergeAllowed'):
        assert result[key] is False, key
    assert result['canonicalFamily'] is None
    assert {'FAMILY_UNKNOWN', 'SESSION_DISTINCTNESS_UNKNOWN'} <= set(result['flags'])
    return result
from journal import Journal


class OfflineTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.j = CoordinatorJournal.create(str(Path(self.tmp.name) / 'journal'), 'owner')
        self.b = dict(clientRequestId='request', titleNonce='[uwf:a]', environmentId='env', projectId='project', providerInstanceId='provider', model='model', effort='high', runtimeMode='default', promptSha256=hashlib.sha256(b'prompt').hexdigest(), intentAt='2026-11-16T00:00:00Z', generation=1, head='head', base='base', workKey='work', workBinding={'head':'head','base':'base','repository':'repo'})
        self.j.intent('owner', 1, 'a', self.b)
        self.config = dict(expectedHead='head', expectedBase='base', expectedMerge='merge', countedRunId='run', collectedAt='2026-11-16T00:00:00Z')
        self.thread = {'thread': {'threadId':'thread', 'projectId':'project', 'runCount':1}, 'recentRuns':[{'runId':'run', 'ordinal':1, 'model':'model', 'executedModel':None}], 'items':[{'type':'user_message', 'position':1, 'text':'prompt'}, {'type':'assistant_message', 'position':2, 'runId':'run', 'status':'completed', 'text':'VERDICT: CLEAN'}], 'hasMore':False}
        self.pages = [{'threads':[], 'nextCursor':None}]
        self.github = {'pr':{'head':{'sha':'head'}, 'base':{'sha':'base'}}, 'workflowRuns':[]}

    def envelope(self):
        e = collect(self.config, self.thread, self.pages, self.github)
        for k in ('eligible','authenticated','dispatchAllowed','publishAllowed','mergeAllowed'): self.assertIs(e[k], False)
        self.assertIsNone(e['canonicalFamily'])
        self.assertTrue({'FAMILY_UNKNOWN', 'SESSION_DISTINCTNESS_UNKNOWN'} <= set(e['flags']))
        return e

    def send(self):
        self.j.transition('owner', 1, 'a', 'LAUNCH_SENT', launch_sent_at='2026-11-16T00:01:00Z')

    def launch(self):
        self.send(); self.j.transition('owner', 1, 'a', 'LAUNCHED', thread_id='thread')

    def event(self, **kw):
        return dict(dict(threadId='thread', runId='run', status='completed', completedAt='2026-11-16T00:03:00Z', ordinal=1, generation=1, sourceHead='head', sourceBase='base'), **kw)

    def reads(self):
        t = dict(threadId='thread', title='[uwf:a]', environmentId='env', projectId='project', createdAt='2026-11-16T00:00:30Z', createdBy='agent', creationSource='mcp')
        return ([dict(coordinatorRecorded=True, readAt='2026-11-16T00:02:00Z', threads=[t], nextCursor=None)], [dict(coordinatorRecorded=True, readAt='2026-11-16T00:02:00Z', thread=t.copy(), recentRuns=[{'model':'model'}], items=[{'type':'user_message', 'text':'prompt', 'position':0}], hasMore=False)])

    def test_missing_executed_model(self):
        e = self.envelope(); self.assertIn('MODEL_EXECUTED_ABSENT', e['flags']); self.assertEqual(e['verdict'], 'CLEAN')

    def test_aliases_never_gain_family(self):
        for model in ('gpt-6.1-sol','oai/gpt-6.1-sol','oai/oai/gpt-6.1-sol','claude-fable-5-dd-arret-1.6-tpg','claude-fable-5-dd-los-1.6-tpg','model-20261116','mоdel','mo\u200bdel'):
            with self.subTest(model=model):
                self.thread['recentRuns'][0]['executedModel'] = model
                e = self.envelope(); self.assertIn('MODEL_REQUESTED_EXECUTED_MISMATCH', e['flags'])

    def test_mixed_human_runs(self):
        self.thread['thread']['runCount'] = 2
        self.thread['recentRuns'][0].update(executedModel='one', status='failed')
        self.thread['items'].append(dict(type='error', runId='run', text='Claude gave up after repeated API errors'))
        self.thread['recentRuns'].append(dict(runId='run2',ordinal=2,model='two',executedModel='two',status='completed'))
        self.thread['items'].append(dict(type='user_message',createdBy='user',creationSource='web',text='Try again'))
        e = self.envelope(); self.assertTrue({'EXTRA_RUN','HUMAN_RETRY','MIXED_RUN_MODELS'} <= set(e['flags']))

    def test_pagination_and_truncation(self):
        self.thread['hasMore'] = True; self.thread['thread']['runCount'] = 2
        self.thread['items'][1]['textTruncated'] = True; self.pages[0]['nextCursor'] = 2
        e = self.envelope(); self.assertTrue({'PAGINATION_INCOMPLETE','RUNS_HIDDEN','TEXT_TRUNCATED'} <= set(e['flags'])); self.assertIsNone(e['verdict'])
        path = Path(self.tmp.name) / 'cut'; path.write_text('{"thread":')
        self.assertIsNone(saved_json(path)); self.assertIn('FILE_TRUNCATED_OR_UNPARSABLE', collect({}, None, [], None)['flags'])

    def test_duplicate_and_conflicting_events(self):
        self.launch(); e = self.event(); self.j.observe_run('owner',1,'a',e)
        self.assertTrue(self.j.observe_run('owner',1,'a',e)['duplicate'])
        with self.assertRaises(Blocked): self.j.observe_run('owner',1,'a',dict(e, extra='changed'))

    def test_missed_poll_reconciles(self):
        self.launch(); self.j.observe_run('owner',1,'a',self.event())
        self.assertEqual(self.j.recover()['attempts'][0]['state'],'TERMINAL'); self.assertFalse(self.j.recovery_plan()['dispatch_allowed'])

    def test_recovery_states(self):
        for state in ('INTENT','LAUNCH_SENT','LAUNCHED'):
            with self.subTest(state=state):
                p = str(Path(self.tmp.name) / state)
                script = '''import os,sys\nfrom coordinator_journal import CoordinatorJournal\nj=CoordinatorJournal.create(sys.argv[1],"owner")\nb=%r\nj.intent("owner",1,"a",b)\nif sys.argv[2] != "INTENT": j.transition("owner",1,"a","LAUNCH_SENT",launch_sent_at="2026-11-16T00:01:00Z")\nif sys.argv[2] == "LAUNCHED": j.transition("owner",1,"a","LAUNCHED",thread_id="thread")\nos._exit(0)''' % self.b
                env = dict(os.environ, PYTHONPATH=str(Path(__file__).parent))
                subprocess.run([sys.executable,'-c',script,p,state],check=True,env=env)
                r = CoordinatorJournal(p).recover(); self.assertEqual(r['attempts'][0]['state'], state); self.assertFalse(r['dispatch_allowed']); self.assertFalse(r['replay_allowed'])

    def test_resolve_full(self):
        self.send(); pages, reads = self.reads(); r = self.j.resolve_from_hub_reads('owner',1,'a',pages,reads,'2026-11-16T00:02:00Z')
        self.assertEqual(r['state'],'LAUNCHED'); self.assertIn('RECOVERED_BY_BINDINGS',r['flags'])

    def test_resolve_partial_bindings(self):
        for field, value in [('prompt','wrong'),('truncated',True),('createdAt','2026-11-15T00:00:00Z'),('projectId','wrong'),('createdBy','user')]:
            with self.subTest(field=field):
                p = str(Path(self.tmp.name) / field); j = CoordinatorJournal.create(p,'owner'); j.intent('owner',1,'a',self.b); j.transition('owner',1,'a','LAUNCH_SENT',launch_sent_at='2026-11-16T00:01:00Z')
                pages, reads = self.reads()
                if field == 'prompt': reads[0]['items'][0]['text'] = value
                elif field == 'truncated': reads[0]['items'][0]['textTruncated'] = value
                else: reads[0]['thread'][field] = value
                r = j.resolve_from_hub_reads('owner',1,'a',pages,reads,'2026-11-16T00:02:00Z'); self.assertTrue(r['ambiguous']); self.assertIn('PARTIAL_BINDING_MATCH',r['flags'])

    def test_resolve_zero_and_duplicate(self):
        self.send(); pages, reads = self.reads(); pages[0]['threads'] = []
        r = self.j.resolve_from_hub_reads('owner',1,'a',pages,[dict(coordinatorRecorded=True, readAt='2026-11-16T00:02:00Z', text='thread id thread')],'2026-11-16T00:02:00Z'); self.assertTrue(r['ambiguous'])
        pages[0]['readAt'] = '2026-11-16T00:03:00Z'; self.j.resolve_from_hub_reads('owner',1,'a',pages,reads,'2026-11-16T00:03:00Z')
        pages[0]['readAt'] = '2026-11-16T00:12:00Z'; r = self.j.resolve_from_hub_reads('owner',1,'a',pages,reads,'2026-11-16T00:12:00Z'); self.assertEqual(r['state'],'BLOCKED')
        p = str(Path(self.tmp.name)/'dup'); j = CoordinatorJournal.create(p,'owner'); j.intent('owner',1,'a',self.b); j.transition('owner',1,'a','LAUNCH_SENT',launch_sent_at='2026-11-16T00:01:00Z')
        pages, reads = self.reads(); pages[0]['threads'].append(dict(pages[0]['threads'][0],threadId='other'))
        self.assertEqual(j.resolve_from_hub_reads('owner',1,'a',pages,reads,'2026-11-16T00:02:00Z')['state'],'DUPLICATE')

    def test_later_run_reopens(self):
        self.launch(); self.j.observe_run('owner',1,'a',self.event()); self.j.transition('owner',1,'a','EVIDENCE_COLLECTED')
        e = dict(self.event(),runId='run2',ordinal=2,createdBy='user',creationSource='web')
        self.j.observe_run('owner',1,'a',e); r = self.j.recover()['attempts'][0]
        self.assertEqual(r['state'],'REOPENED'); self.assertEqual(r['evidence_void'],1); self.assertIn('EVIDENCE_REOPENED',json.loads(r['flags']))

    def test_wrong_owner_and_generation(self):
        for owner, gen in [('other',1),('owner',2)]:
            with self.assertRaises(Blocked): self.j.transition(owner,gen,'a','LAUNCH_SENT',launch_sent_at='2026-11-16T00:01:00Z')

    def test_heads_and_checks(self):
        self.github['pr']['head']['sha']='new'; self.config['requiredContexts']=['required']; self.github['statuses']={'statuses':[{'context':'required','state':'failure'}]}
        self.assertTrue({'HEAD_STALE','CHECKS_FAILED'} <= set(self.envelope()['flags']))
        self.config['expectedHead']=None; self.assertIn('HEAD_SELF_REPORTED_ONLY',self.envelope()['flags'])
        self.github['workflowRuns']=[{'conclusion':'failure'}]; self.assertIn('CHECKS_FAILED',self.envelope()['flags'])

    def test_ci_initial_plus_two_new_runs_and_fourth_exhausts(self):
        def run(i, attempt): return dict(id=i,run_attempt=attempt,path='.github/workflows/ci.yml',head_sha='head',base_sha='base',merge_sha='merge',conclusion='success')
        self.github['workflowRuns']=[run(1,2)]; self.assertIn('CI_ATTEMPT_NOT_FIRST',self.envelope()['flags'])
        self.github['workflowRuns']=[run(i,1) for i in range(3)]; self.assertNotIn('CI_RETRY_BUDGET_EXHAUSTED',self.envelope()['flags'])
        self.github['workflowRuns'].append(run(3,1)); self.assertIn('CI_RETRY_BUDGET_EXHAUSTED',self.envelope()['flags'])

    def test_cli_and_static_imports(self):
        cfg = Path(self.tmp.name)/'config'; cfg.write_text('{}')
        for p in (cfg, Path(self.tmp.name)/'missing'):
            result = subprocess.run([sys.executable,str(Path(__file__).with_name('evidence_collector.py')),str(p)],capture_output=True)
            self.assertEqual(result.returncode,2); self.assertFalse(json.loads(result.stdout)['eligible'])
        for name in ('coordinator_journal.py','evidence_collector.py'):
            source = Path(__file__).with_name(name).read_text()
            for forbidden in ('import socket','import urllib.request','import http.client','import subprocess'): self.assertNotIn(forbidden,source)

    def test_immutable_work_binding(self):
        changed = dict(self.b, workBinding=dict(self.b['workBinding'], repository='other'), clientRequestId='other', titleNonce='[uwf:b]')
        with self.assertRaises(Blocked): self.j.intent('owner', 1, 'b', changed)
        with self.assertRaises(Blocked): self.j.intent('owner', 1, 'a', dict(self.b, model='different'))
        self.assertEqual(len(self.j.recover()['attempts']), 1)

    def test_disallowed_transition_table(self):
        with self.j.connection() as db:
            db.execute('BEGIN IMMEDIATE')
            for source, allowed in TRANSITIONS.items():
                for target in TRANSITIONS:
                    if target not in allowed:
                        with self.subTest(source=source, target=target):
                            with self.assertRaises(Blocked):
                                self.j._transition(db, {'state': source, 'attempt_id': 'a'}, target)
            db.rollback()

    def test_late_old_or_nonterminal_event_never_regresses_terminal(self):
        self.launch(); self.j.observe_run('owner', 1, 'a', self.event())
        for event in (dict(self.event(), status='running', completedAt=None),
                      dict(self.event(), runId='older', ordinal=0, status='failed')):
            self.j.observe_run('owner', 1, 'a', event)
            self.assertEqual(self.j.recover()['attempts'][0]['state'], 'TERMINAL')

    def test_worker_text_is_not_a_binding(self):
        self.send(); pages, reads = self.reads(); pages[0]['threads'] = []
        reads[0]['items'] = [{'type':'assistant_message', 'text':'thread id thread, head trusted-head'}]
        result = self.j.resolve_from_hub_reads('owner', 1, 'a', pages, reads, '2026-11-16T00:02:00Z')
        self.assertTrue(result['ambiguous']); self.assertFalse(result['dispatch_allowed'])
        self.config['expectedHead'] = None
        self.thread['items'][1]['text'] = 'head: head\nVERDICT: CLEAN'
        self.assertIn('HEAD_SELF_REPORTED_ONLY', self.envelope()['flags'])

    def test_truncated_first_user_message_is_unavailable(self):
        self.send(); pages, reads = self.reads()
        reads[0]['items'][0]['textTruncated'] = True
        result = self.j.resolve_from_hub_reads('owner', 1, 'a', pages, reads, '2026-11-16T00:02:00Z')
        self.assertTrue(result['ambiguous']); self.assertIn('PARTIAL_BINDING_MATCH', result['flags'])

    def test_pragmas(self):
        p = str(Path(self.tmp.name)/'old'); j = Journal.create(p,'owner')
        for journal in (j,self.j):
            with journal.connection() as db:
                self.assertEqual(db.execute('pragma journal_mode').fetchone()[0], 'delete'); self.assertEqual(db.execute('pragma synchronous').fetchone()[0],2)


if __name__ == '__main__': unittest.main()
