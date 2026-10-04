import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("results", Path(__file__).with_name("ci-results.py"))
results = importlib.util.module_from_spec(spec)
spec.loader.exec_module(results)


class ResultsTests(unittest.TestCase):
    def report(self, folder, outcomes, passed=None):
        actual = outcomes.count("Passed")
        data = ''.join(f'<UnitTestResult testName="Example" outcome="{outcome}"/>' for outcome in outcomes)
        Path(folder, "run.trx").write_text(f'<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>{data}</Results><ResultSummary><Counters total="{len(outcomes)}" passed="{actual if passed is None else passed}"/></ResultSummary></TestRun>')

    def test_selected_suite_must_have_real_successful_results(self):
        with tempfile.TemporaryDirectory() as folder:
            with self.assertRaises(ValueError):
                results.verify(folder)
            for outcomes in ([], ["Failed"], ["Inconclusive"], ["NotExecuted"]):
                self.report(folder, outcomes)
                with self.assertRaises(ValueError):
                    results.verify(folder)
            self.report(folder, ["Passed", "Passed"])
            self.assertEqual(results.verify(folder), {"reports": 1, "passed": 2, "ignored": 0})

    def test_ignored_cases_require_explicit_routine_permission(self):
        with tempfile.TemporaryDirectory() as folder:
            self.report(folder, ["Passed", "NotExecuted"])
            with self.assertRaises(ValueError):
                results.verify(folder)
            self.assertEqual(results.verify(folder, ["Example"])["ignored"], 1)
            with self.assertRaises(ValueError):
                results.verify(folder, ["OtherTest"])
            self.report(folder, ["Passed"], passed=2)
            with self.assertRaises(ValueError):
                results.verify(folder, ["Example"])
