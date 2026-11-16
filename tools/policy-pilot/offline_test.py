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

HEAD = 'a' * 40
BASE = 'b' * 40
MERGE = 'c' * 40


class OfflineTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.j = CoordinatorJournal.create(str(Path(self.tmp.name) / 'journal'), 'owner')
        self.b = dict(clientRequestId='request', titleNonce='[uwf:a]', environmentId='env', projectId='project', providerInstanceId='provider', model='model', effort='high', runtimeMode='default', promptSha256=hashlib.sha256(b'prompt').hexdigest(), intentAt='2026-11-16T00:00:00Z', generation=1, head=HEAD, base=BASE, workKey='work', workBinding={'head':HEAD,'base':BASE,'repository':'repo'})
        self.j.intent('owner', 1, 'a', self.b)
        self.config = dict(expectedHead=HEAD, expectedBase=BASE, expectedMerge=MERGE, prNumber=1, titleNonce='[uwf:a]', countedRunId='run', collectedAt='2026-11-16T00:00:00Z')
        self.thread = {'thread': {'threadId':'thread', 'projectId':'project', 'runCount':1, 'latestRunId':'run'}, 'recentRuns':[{'runId':'run', 'ordinal':1, 'model':'model', 'executedModel':None, 'status':'completed'}], 'items':[{'type':'user_message', 'position':1, 'text':'prompt'}, {'type':'assistant_message', 'position':2, 'runId':'run', 'status':'completed', 'text':'VERDICT: CLEAN'}], 'hasMore':False}
        self.pages = [{'threads':[], 'nextCursor':None}]
        self.github = {'pr':{'head':{'sha':HEAD}, 'base':{'sha':BASE}}, 'statuses':{'sha':HEAD,'statuses':[]}, 'workflowRuns':{'total_count':0,'workflow_runs':[]}}

    def envelope(self):
        e = collect(self.config, self.thread, self.pages, self.github)
        for k in ('eligible','authenticated','dispatchAllowed','publishAllowed','mergeAllowed'): self.assertIs(e[k], False)
        self.assertIsNone(e['canonicalFamily'])
        self.assertTrue({'FAMILY_UNKNOWN', 'SESSION_DISTINCTNESS_UNKNOWN'} <= set(e['flags']))
        return e

    def send(self):
        self.j.transition('owner', 1, 'a', 'LAUNCH_SENT', launch_sent_at='2026-11-16T00:01:00Z', now='2026-11-16T00:01:00Z')

    def launch(self):
        self.send(); self.j.transition('owner', 1, 'a', 'LAUNCHED', thread_id='thread')

    def event(self, **kw):
        return dict(dict(threadId='thread', runId='run', status='completed', completedAt='2026-11-16T00:03:00Z', ordinal=1, generation=1, sourceHead=HEAD, sourceBase=BASE), **kw)

    def reads(self):
        t = dict(threadId='thread', title='[uwf:a]', environmentId='env', projectId='project', createdAt='2026-11-16T00:00:30Z', createdBy='agent', creationSource='mcp', runCount=1)
        return ([dict(coordinatorRecorded=True, readAt='2026-11-16T00:02:00Z', threads=[t], nextCursor=None, request={'environmentId':'env','allProjects':True,'includeSubagents':True,'titleContains':'[uwf:a]','cursor':0})], [dict(coordinatorRecorded=True, readAt='2026-11-16T00:02:00Z', environmentId='env', thread=t.copy(), recentRuns=[{'model':'model'}], items=[{'type':'user_message', 'text':'prompt', 'position':0}], hasMore=False)])

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
                script = '''import os,sys\nfrom coordinator_journal import CoordinatorJournal\nj=CoordinatorJournal.create(sys.argv[1],"owner")\nb=%r\nj.intent("owner",1,"a",b)\nif sys.argv[2] != "INTENT": j.transition("owner",1,"a","LAUNCH_SENT",launch_sent_at="2026-11-16T00:01:00Z",now="2026-11-16T00:01:00Z")\nif sys.argv[2] == "LAUNCHED": j.transition("owner",1,"a","LAUNCHED",thread_id="thread")\nos._exit(0)''' % self.b
                env = dict(os.environ, PYTHONPATH=str(Path(__file__).parent))
                subprocess.run([sys.executable,'-c',script,p,state],check=True,env=env)
                r = CoordinatorJournal(p).recover(); self.assertEqual(r['attempts'][0]['state'], state); self.assertFalse(r['dispatch_allowed']); self.assertFalse(r['replay_allowed'])

    def test_resolve_full(self):
        self.send(); pages, reads = self.reads(); r = self.j.resolve_from_hub_reads('owner',1,'a',pages,reads,'2026-11-16T00:02:00Z')
        self.assertEqual(r['state'],'LAUNCHED'); self.assertIn('RECOVERED_BY_BINDINGS',r['flags'])

    def test_resolve_partial_bindings(self):
        for field, value in [('prompt','wrong'),('truncated',True),('createdAt','2026-11-15T00:00:00Z'),('projectId','wrong'),('createdBy','user')]:
            with self.subTest(field=field):
                p = str(Path(self.tmp.name) / field); j = CoordinatorJournal.create(p,'owner'); j.intent('owner',1,'a',self.b); j.transition('owner',1,'a','LAUNCH_SENT',launch_sent_at='2026-11-16T00:01:00Z', now='2026-11-16T00:01:00Z')
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
        p = str(Path(self.tmp.name)/'dup'); j = CoordinatorJournal.create(p,'owner'); j.intent('owner',1,'a',self.b); j.transition('owner',1,'a','LAUNCH_SENT',launch_sent_at='2026-11-16T00:01:00Z', now='2026-11-16T00:01:00Z')
        pages, reads = self.reads(); pages[0]['threads'].append(dict(pages[0]['threads'][0],threadId='other'))
        self.assertEqual(j.resolve_from_hub_reads('owner',1,'a',pages,reads,'2026-11-16T00:02:00Z')['state'],'DUPLICATE')

    def test_later_run_reopens(self):
        self.launch(); self.j.observe_run('owner',1,'a',self.event()); self.j.transition('owner',1,'a','EVIDENCE_COLLECTED')
        e = dict(self.event(),runId='run2',ordinal=2,createdBy='user',creationSource='web')
        self.j.observe_run('owner',1,'a',e); r = self.j.recover()['attempts'][0]
        self.assertEqual(r['state'],'REOPENED'); self.assertEqual(r['evidence_void'],1); self.assertIn('EVIDENCE_REOPENED',json.loads(r['flags']))

    def test_wrong_owner_and_generation(self):
        for owner, gen in [('other',1),('owner',2)]:
            with self.assertRaises(Blocked): self.j.transition(owner,gen,'a','LAUNCH_SENT',launch_sent_at='2026-11-16T00:01:00Z', now='2026-11-16T00:01:00Z')

    def test_heads_and_checks(self):
        self.github['pr']['head']['sha']='new'; self.config['requiredContexts']=['required']; self.github['statuses']={'statuses':[{'context':'required','state':'failure'}]}
        self.assertTrue({'HEAD_STALE','CHECKS_FAILED'} <= set(self.envelope()['flags']))
        self.config['expectedHead']=None; self.assertIn('HEAD_SELF_REPORTED_ONLY',self.envelope()['flags'])
        self.github['workflowRuns']={'total_count':1,'workflow_runs':[{'status':'completed','conclusion':'failure'}]}; self.assertIn('CHECKS_FAILED',self.envelope()['flags'])

    def test_ci_initial_plus_two_new_runs_and_fourth_exhausts(self):
        def run(i, attempt):
            return dict(id=i,run_attempt=attempt,path='.github/workflows/ci.yml',head_sha=HEAD,
                        display_title=f'HVO-PR-CI v1 pr=1 head={HEAD} base={BASE} merge={MERGE}',status='completed',conclusion='success')
        self.github['workflowRuns']={'total_count':1,'workflow_runs':[run(1,2)]}; self.assertIn('CI_ATTEMPT_NOT_FIRST',self.envelope()['flags'])
        self.github['workflowRuns']={'total_count':3,'workflow_runs':[run(i,1) for i in range(3)]}; self.assertNotIn('CI_RETRY_BUDGET_EXHAUSTED',self.envelope()['flags'])
        self.github['workflowRuns']['workflow_runs'].append(run(3,1)); self.github['workflowRuns']['total_count']=4; self.assertIn('CI_RETRY_BUDGET_EXHAUSTED',self.envelope()['flags'])

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
            expected = {'INTENT': {'LAUNCH_SENT','BLOCKED'}, 'LAUNCH_SENT': {'LAUNCHED','BLOCKED','DUPLICATE'}, 'LAUNCHED': {'TERMINAL','REOPENED','BLOCKED'}, 'TERMINAL': {'EVIDENCE_COLLECTED','REOPENED','BLOCKED'}, 'EVIDENCE_COLLECTED': {'REOPENED','BLOCKED'}, 'REOPENED': {'TERMINAL','BLOCKED'}, 'BLOCKED': set(), 'DUPLICATE': set()}
            self.assertEqual(TRANSITIONS, expected)
            for source, allowed in expected.items():
                for target in expected:
                    if target not in allowed:
                        with self.subTest(source=source, target=target):
                            with self.assertRaises(Blocked):
                                self.j._transition(db, {'state': source, 'attempt_id': 'a'}, target)
            db.rollback()

    def test_late_old_or_nonterminal_event_never_regresses_terminal(self):
        self.launch(); self.j.observe_run('owner', 1, 'a', self.event())
        for event in (dict(self.event(), status='running', completedAt=None),
                      dict(self.event(), runId='run', ordinal=1, status='running', completedAt='earlier')):
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

    def test_each_binding_and_page_constraint(self):
        cases = ['environment', 'environment_conflict', 'project', 'created_by', 'source', 'model', 'effort', 'upper_time', 'has_more', 'cursor', 'hidden', 'first', 'page_cursor', 'all_projects', 'subagents', 'status_filter', 'nonce', 'page_more', 'multiple_pages', 'recorded_id']
        for case in cases:
            with self.subTest(case=case):
                j = CoordinatorJournal.create(str(Path(self.tmp.name)/case), 'owner'); j.intent('owner',1,'a',self.b)
                kwargs = {'launch_sent_at':'2026-11-16T00:01:00Z', 'now':'2026-11-16T00:01:00Z'}
                if case == 'recorded_id': kwargs['thread_id'] = 'other'
                j.transition('owner',1,'a','LAUNCH_SENT',**kwargs)
                pages, reads = self.reads(); p, r = pages[0], reads[0]; t = r['thread']
                if case == 'environment': r['environmentId']='bad'
                if case == 'environment_conflict': t['environmentId']='bad'
                if case == 'project': t['projectId']='bad'
                if case == 'created_by': t['createdBy']='user'
                if case == 'source': t['creationSource']='web'; p['threads'][0]['creationSource']='web'
                if case == 'model': r['recentRuns'][0]['model']='bad'
                if case == 'effort': r['recentRuns'][0]['effort']='bad'
                if case == 'upper_time': t['createdAt']='2026-11-16T00:12:00Z'; p['threads'][0]['createdAt']='2026-11-16T00:12:00Z'
                if case == 'has_more': r['hasMore']=True
                if case == 'cursor': r['nextCursor']=2
                if case == 'hidden': t['runCount']=2
                if case == 'first': r['items']=[]
                if case == 'page_cursor': p['request']['cursor']=2
                if case == 'all_projects': p['request']['allProjects']=False
                if case == 'subagents': p['request']['includeSubagents']=False
                if case == 'status_filter': p['request']['statuses']=['completed']
                if case == 'nonce': p['request']['titleContains']='wrong'
                if case == 'page_more': p['nextCursor']=1
                if case == 'multiple_pages': pages.append(dict(p))
                result = j.resolve_from_hub_reads('owner',1,'a',pages,reads,'2026-11-16T00:02:00Z')
                self.assertTrue(result['ambiguous'])

    def test_unresolved_work_refuses_second_intent(self):
        self.send()
        b = dict(self.b, clientRequestId='other', titleNonce='[uwf:b]')
        with self.assertRaises(Blocked): self.j.intent('owner',1,'b',b)

    def test_newer_run_precedes_late_completion_and_third_run(self):
        self.launch()
        self.j.observe_run('owner',1,'a',dict(self.event(),runId='run2',ordinal=2,status='running',completedAt=None,createdBy='user',creationSource='web'))
        self.j.observe_run('owner',1,'a',self.event())
        self.j.observe_run('owner',1,'a',dict(self.event(),runId='run3',ordinal=3,status='running',completedAt=None))
        self.j.observe_run('owner',1,'a',dict(self.event(),runId='run2',ordinal=2))
        row=self.j.recover()['attempts'][0]; self.assertEqual((row['state'],row['run_id'],row['evidence_void']),('REOPENED','run3',1))
        self.assertIn('HUMAN_RETRY',json.loads(row['flags']))

    def test_quarantined_event_is_reconsidered(self):
        self.send(); e=self.event()
        self.assertEqual(self.j.observe_run('owner',1,'a',e)['disposition'],'quarantined')
        self.j.transition('owner',1,'a','LAUNCHED',thread_id='thread')
        self.assertFalse(self.j.observe_run('owner',1,'a',e)['duplicate'])
        self.assertEqual(self.j.recover()['attempts'][0]['state'],'TERMINAL')

    def test_wrong_generation_and_owner_on_mutators(self):
        pages, reads = self.reads()
        for owner, gen in [('other',1),('owner',2),('owner',True)]:
            for action in (lambda: self.j.intent(owner,gen,'a',self.b), lambda: self.j.transition(owner,gen,'a','BLOCKED'),
                           lambda: self.j.observe_run(owner,gen,'a',self.event()),
                           lambda: self.j.resolve_from_hub_reads(owner,gen,'a',pages,reads,'2026-11-16T00:02:00Z')):
                with self.assertRaises(Blocked): action()
        self.launch(); self.assertEqual(self.j.observe_run('owner',1,'a',dict(self.event(),generation=True))['disposition'],'quarantined')
        self.assertEqual(self.j.observe_run('owner',1,'a',dict(self.event(),generation=2,runId='other'))['disposition'],'quarantined')

    def test_verdict_suppression_for_incomplete_or_old_runs(self):
        self.assertEqual(self.envelope()['verdict'],'CLEAN')
        for kind in ('hidden','cursor','running','old','no_run','failed','multiple','ambiguous','reopened','duplicate','commentary'):
            with self.subTest(kind=kind):
                import copy
                config, thread, pages, github = copy.deepcopy((self.config,self.thread,self.pages,self.github))
                if kind == 'hidden': thread['thread']['runCount']=2
                if kind == 'cursor': thread['nextCursor']=1
                if kind == 'running': thread['recentRuns'][0]['status']='running'
                if kind == 'old': thread['thread'].update(runCount=2,latestRunId='run2'); thread['recentRuns'].append(dict(runId='run2',ordinal=2,status='running'))
                if kind == 'no_run': config['countedRunId']='missing'
                if kind == 'failed': thread['recentRuns'][0]['status']='failed'
                if kind == 'multiple': thread['items'][1]['text']='CLEAN\nBLOCKED'
                if kind == 'ambiguous': config['ambiguousLaunch']=True
                if kind == 'reopened': config['evidenceReopened']=True
                if kind == 'duplicate': pages[0]['threads']=[{'title':'[uwf:a]','threadId':'1'},{'title':'[uwf:a]','threadId':'2'}]
                if kind == 'commentary': thread['recentRuns'][0]['status']='running'
                self.assertIsNone(collect(config,thread,pages,github)['verdict'])

    def test_strict_json_and_cli_resource_errors(self):
        p=Path(self.tmp.name)/'input'
        for data in ('[{}]', '{} {', 'prefix {}', '{} trailing', '{"x":NaN}', '{"x":'+('['*20000)+(']'*20000)+'}'):
            p.write_text(data); self.assertIsNone(saved_json(p))
            r=subprocess.run([sys.executable,str(Path(__file__).with_name('evidence_collector.py')),str(p)],capture_output=True,timeout=5)
            self.assertEqual(r.returncode,2); self.assertFalse(json.loads(r.stdout)['eligible'])
        fifo=Path(self.tmp.name)/'fifo'; os.mkfifo(fifo)
        self.assertIsNone(saved_json(fifo)); self.assertIsNone(saved_json('/dev/zero'))

    def test_scan_schedule_requires_three_reads(self):
        self.send(); pages, reads=self.reads(); pages[0]['threads']=[]
        for when in ('2026-11-16T00:02:00Z','2026-11-16T00:12:00Z'):
            pages[0]['readAt']=when
            result=self.j.resolve_from_hub_reads('owner',1,'a',pages,reads,when)
            self.assertTrue(result['ambiguous'])

    def test_completed_extra_run_is_reconciled_after_reopen(self):
        self.launch(); event=dict(self.event(),runId='run2',ordinal=2,createdBy='user',creationSource='web')
        self.j.observe_run('owner',1,'a',event)
        self.assertEqual(self.j.recover()['attempts'][0]['state'],'REOPENED')
        self.j.observe_run('owner',1,'a',event)
        row=self.j.recover()['attempts'][0]
        self.assertEqual((row['state'],row['evidence_void']),('TERMINAL',1))
        self.assertTrue(self.j.observe_run('owner',1,'a',event)['duplicate'])

    def test_running_second_run_before_first_completes(self):
        self.launch()
        self.j.observe_run('owner',1,'a',dict(self.event(),status='running',completedAt=None))
        self.j.observe_run('owner',1,'a',dict(self.event(),runId='run2',ordinal=2,status='running',completedAt=None))
        self.j.observe_run('owner',1,'a',self.event())
        row=self.j.recover()['attempts'][0]
        self.assertEqual((row['state'],row['run_id'],row['evidence_void']),('REOPENED','run2',1))

    def test_quarantine_does_not_consume_other_attempt_event(self):
        self.launch(); event=self.event()
        self.assertEqual(self.j.observe_run('owner',1,'wrong-attempt',event)['disposition'],'quarantined')
        self.assertEqual(self.j.observe_run('owner',1,'a',event)['disposition'],'recorded')
        self.assertEqual(self.j.recover()['attempts'][0]['state'],'TERMINAL')

    def test_new_run_equal_ordinal_blocks_and_voids(self):
        self.launch(); self.j.observe_run('owner',1,'a',self.event()); self.j.transition('owner',1,'a','EVIDENCE_COLLECTED')
        self.j.observe_run('owner',1,'a',dict(self.event(),runId='different'))
        row=self.j.recover()['attempts'][0]
        self.assertEqual((row['state'],row['evidence_void']),('BLOCKED',1))

    def test_timestamp_nonce_and_digest_validation(self):
        for b in (dict(self.b,intentAt='2026-11-16T00:00:00'),dict(self.b,head='main'),dict(self.b,promptSha256='invalid')):
            with self.assertRaises((Blocked,ValueError)): self.j.intent('owner',1,'a',b)
        with self.assertRaises(Blocked): self.j.intent('owner',1,'a]x',dict(self.b,titleNonce='[uwf:a]x]'))
        with self.assertRaises(Blocked): self.j.transition('owner',1,'a','LAUNCH_SENT',launch_sent_at='2026-11-17T00:00:00Z',now='2026-11-16T00:00:00Z')
        with self.assertRaises(ValueError): self.j.transition('owner',1,'a','LAUNCH_SENT',launch_sent_at='2026-11-16T00:01:00',now='2026-11-16T00:01:00Z')

    def test_reserved_transitions_and_unknown_state(self):
        with self.assertRaises(Blocked): self.j.transition('owner',1,'a','BLOCKED',flags='x'*65537)
        with self.j.connection() as db:
            with self.assertRaises(Blocked): self.j._transition(db,{'state':'UNKNOWN','attempt_id':'a'},'BLOCKED')
        self.send()
        with self.assertRaises(Blocked): self.j.transition('owner',1,'a','LAUNCHED',thread_id='thread',flags='["RECOVERED_BY_BINDINGS"]')

    def test_missing_bindings_and_status_sha(self):
        del self.thread['thread']['runCount']; self.config['titleNonce']=None; self.config['expectedBase']=None
        self.github['statuses']['sha']='different'
        e=self.envelope()
        self.assertTrue({'RUN_COUNT_MISSING','TITLE_NONCE_MISSING','EXPECTED_BASE_MISSING','STATUS_HEAD_MISMATCH'} <= set(e['flags']))
        self.assertIsNone(e['verdict'])

    def test_workflow_status_and_pagination(self):
        for status,conclusion in [('in_progress',None),('completed','startup_failure'),('completed',None)]:
            with self.subTest(status=status,conclusion=conclusion):
                self.github['workflowRuns']={'total_count':2,'workflow_runs':[{'status':status,'conclusion':conclusion}]}
                e=self.envelope(); self.assertIn('CHECKS_FAILED',e['flags']); self.assertIn('PAGINATION_INCOMPLETE',e['flags'])

    def test_unknown_run_ordinal_blocks(self):
        self.launch(); self.j.observe_run('owner',1,'a',self.event()); self.j.transition('owner',1,'a','EVIDENCE_COLLECTED')
        self.j.observe_run('owner',1,'a',dict(self.event(),runId='run2',ordinal='2'))
        self.assertEqual(self.j.recover()['attempts'][0]['state'],'BLOCKED')

    def test_pragmas(self):
        p = str(Path(self.tmp.name)/'old'); j = Journal.create(p,'owner')
        for journal in (j,self.j):
            with journal.connection() as db:
                self.assertEqual(db.execute('pragma journal_mode').fetchone()[0], 'delete'); self.assertEqual(db.execute('pragma synchronous').fetchone()[0],2)


if __name__ == '__main__': unittest.main()
