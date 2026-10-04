import importlib.util
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET

spec = importlib.util.spec_from_file_location("results", Path(__file__).with_name("ci-results.py"))
results = importlib.util.module_from_spec(spec)
spec.loader.exec_module(results)
BASELINE_CLASS = "HVO.WebSite.PlaywrightTests.PlaywrightTestSetupTests"
BASELINE_METHOD = "PlaywrightSuite_IsConfiguredButDisabledByDefault"
BASELINE = f"{BASELINE_CLASS}.{BASELINE_METHOD}"
NAMESPACE = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"


class ResultsTests(unittest.TestCase):
    def report(self, folder, outcomes, *, classes=None, names=None, ids=None, definitions=True,
               passed=None, total=None, filename="run.trx"):
        classes = classes or [BASELINE_CLASS] * len(outcomes)
        names = names or [BASELINE_METHOD if outcome == "NotExecuted" else f"Passed{index}"
                          for index, outcome in enumerate(outcomes)]
        ids = ids or [f"arbitrary-id-{index}" for index in range(len(outcomes))]
        root = ET.Element("TestRun", xmlns=NAMESPACE)
        result_nodes = ET.SubElement(root, "Results")
        definitions_node = ET.SubElement(root, "TestDefinitions")
        for index, outcome in enumerate(outcomes):
            ET.SubElement(result_nodes, "UnitTestResult", testId=ids[index],
                          testName=names[index], outcome=outcome)
            if definitions:
                definition = ET.SubElement(definitions_node, "UnitTest", id=ids[index], name=names[index])
                ET.SubElement(definition, "TestMethod", className=classes[index], name=names[index])
        summary = ET.SubElement(root, "ResultSummary")
        ET.SubElement(summary, "Counters", total=str(len(outcomes) if total is None else total),
                      passed=str(outcomes.count("Passed") if passed is None else passed))
        path = Path(folder, filename)
        ET.ElementTree(root).write(path, encoding="unicode")
        return path

    def mutate(self, path, change):
        tree = ET.parse(path)
        change(tree.getroot())
        tree.write(path, encoding="unicode")

    def test_selected_suite_requires_real_successful_results(self):
        with tempfile.TemporaryDirectory() as folder:
            with self.assertRaises(ValueError):
                results.verify(folder)
            for outcomes in ([], ["Failed"], ["Inconclusive"], ["NotExecuted"]):
                self.report(folder, outcomes)
                with self.assertRaises(ValueError):
                    results.verify(folder, [BASELINE])
            self.report(folder, ["Passed", "Passed"], definitions=False)
            self.assertEqual(results.verify(folder), {"reports": 1, "passed": 2, "ignored": 0})

    def test_one_baseline_ignore_uses_class_method_identity_without_pinned_id(self):
        with tempfile.TemporaryDirectory() as folder:
            self.report(folder, ["Passed", "NotExecuted"])
            with self.assertRaises(ValueError):
                results.verify(folder)
            self.assertEqual(results.verify(folder, [BASELINE]), {"reports": 1, "passed": 1, "ignored": 1})
            with self.assertRaises(ValueError):
                results.verify(folder, [BASELINE_METHOD])

    def test_same_method_name_in_another_class_is_not_the_baseline(self):
        with tempfile.TemporaryDirectory() as folder:
            self.report(folder, ["Passed", "NotExecuted"], classes=[BASELINE_CLASS, "Other.SetupTests"])
            with self.assertRaisesRegex(ValueError, "Unexpected"):
                results.verify(folder, [BASELINE])

    def test_missing_definition_or_test_id_rejects_ignored_result(self):
        with tempfile.TemporaryDirectory() as folder:
            self.report(folder, ["Passed", "NotExecuted"], definitions=False)
            with self.assertRaisesRegex(ValueError, "missing/ambiguous"):
                results.verify(folder, [BASELINE])
            path = self.report(folder, ["Passed", "NotExecuted"])
            self.mutate(path, lambda root: root.find("t:Results", results.NS)[1].attrib.pop("testId"))
            with self.assertRaisesRegex(ValueError, "missing/ambiguous"):
                results.verify(folder, [BASELINE])

    def test_definition_names_must_match_ignored_result_method(self):
        with tempfile.TemporaryDirectory() as folder:
            for field in ("result", "definition"):
                path = self.report(folder, ["Passed", "NotExecuted"])
                target = "t:Results" if field == "result" else "t:TestDefinitions"
                attribute = "testName" if field == "result" else "name"
                self.mutate(path, lambda root: root.find(target, results.NS)[1].set(attribute, "Different"))
                with self.assertRaisesRegex(ValueError, "disagrees"):
                    results.verify(folder, [BASELINE])

    def test_duplicate_definition_id_is_ambiguous(self):
        with tempfile.TemporaryDirectory() as folder:
            self.report(folder, ["Passed", "NotExecuted"], ids=["same-id", "same-id"])
            with self.assertRaisesRegex(ValueError, "ambiguous"):
                results.verify(folder, [BASELINE])

    def test_missing_class_or_method_identity_is_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            for field in ("className", "name"):
                path = self.report(folder, ["Passed", "NotExecuted"])
                self.mutate(path, lambda root: root.find("t:TestDefinitions", results.NS)[1][0].attrib.pop(field))
                with self.assertRaisesRegex(ValueError, "incomplete"):
                    results.verify(folder, [BASELINE])

    def test_duplicate_ignored_identity_in_one_report_is_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            self.report(folder, ["Passed", "NotExecuted", "NotExecuted"])
            with self.assertRaisesRegex(ValueError, "repeated"):
                results.verify(folder, [BASELINE])

    def test_duplicate_ignored_identity_across_reports_is_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            self.report(folder, ["Passed", "NotExecuted"], filename="one.trx")
            self.report(folder, ["Passed", "NotExecuted"], ids=["new-pass-id", "new-ignore-id"], filename="two.trx")
            with self.assertRaisesRegex(ValueError, "repeated"):
                results.verify(folder, [BASELINE])

    def test_allowance_does_not_hide_an_additional_unlisted_skip(self):
        with tempfile.TemporaryDirectory() as folder:
            self.report(folder, ["Passed", "NotExecuted", "NotExecuted"],
                        names=["Pass", BASELINE_METHOD, "NewIgnoredCase"])
            with self.assertRaisesRegex(ValueError, "Unexpected"):
                results.verify(folder, [BASELINE])

    def test_each_full_identity_can_be_allowed_once(self):
        with tempfile.TemporaryDirectory() as folder:
            other_class = "Other.SetupTests"
            self.report(folder, ["Passed", "NotExecuted", "NotExecuted"],
                        classes=[BASELINE_CLASS, BASELINE_CLASS, other_class])
            self.assertEqual(results.verify(folder, [BASELINE, f"{other_class}.{BASELINE_METHOD}"]),
                             {"reports": 1, "passed": 1, "ignored": 2})

    def test_multiple_reports_with_only_one_baseline_ignore_remain_valid(self):
        with tempfile.TemporaryDirectory() as folder:
            self.report(folder, ["Passed", "NotExecuted"], filename="one.trx")
            self.report(folder, ["Passed", "Passed"], filename="two.trx")
            self.assertEqual(results.verify(folder, [BASELINE]), {"reports": 2, "passed": 3, "ignored": 1})

    def test_counters_must_agree_with_results(self):
        with tempfile.TemporaryDirectory() as folder:
            for counters in ({"passed": 2}, {"total": 3}):
                self.report(folder, ["Passed", "NotExecuted"], **counters)
                with self.assertRaisesRegex(ValueError, "counters disagree"):
                    results.verify(folder, [BASELINE])


    def test_legacy_solution_allows_empty_assemblies_only_with_actual_passing_tests(self):
        with tempfile.TemporaryDirectory() as folder:
            self.report(folder, [], filename="empty.trx")
            self.report(folder, ["Passed"], filename="passing.trx")
            with self.assertRaises(ValueError):
                results.verify(folder)
            self.assertEqual(results.verify(folder, allow_empty_reports=True), {"reports": 2, "passed": 1, "ignored": 0})

    def test_legacy_all_empty_or_inconsistent_empty_reports_fail(self):
        with tempfile.TemporaryDirectory() as folder:
            self.report(folder, [])
            with self.assertRaisesRegex(ValueError, "No actual passing"):
                results.verify(folder, allow_empty_reports=True)
            self.report(folder, ["Passed"], filename="passing.trx")
            for counters in ({"total": 1}, {"passed": 1}):
                self.report(folder, [], **counters)
                with self.assertRaises(ValueError):
                    results.verify(folder, allow_empty_reports=True)

    def test_legacy_empty_allowance_does_not_accept_failures_or_ignored_results(self):
        with tempfile.TemporaryDirectory() as folder:
            self.report(folder, ["Passed"], filename="passing.trx")
            for outcomes in (["Failed"], ["Inconclusive"], ["Passed", "NotExecuted"]):
                self.report(folder, outcomes)
                with self.assertRaises(ValueError):
                    results.verify(folder, allow_empty_reports=True)


if __name__ == "__main__":
    unittest.main()
