"""Reject empty, failed, incomplete or stale-by-reuse selected test evidence."""
import argparse
from pathlib import Path
import xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def verify(directory, allowed_ignored=()):
    allowed = set(allowed_ignored)
    if any(not identity.rpartition(".")[0] or not identity.rpartition(".")[2] for identity in allowed):
        raise ValueError("Ignored-test allowances require the full class.method identity")
    seen_ignored = set()
    reports = sorted(Path(directory).glob("*.trx"))
    if not reports:
        raise ValueError(f"No TRX reports for selected test invocation: {directory}")
    passed, ignored = 0, 0
    for report in reports:
        root = ET.parse(report).getroot()
        counters = root.find("t:ResultSummary/t:Counters", NS)
        results = root.findall("t:Results/t:UnitTestResult", NS)
        if counters is None or not results:
            raise ValueError(f"Empty/incomplete selected test report: {report}")
        if any(int(counters.get(key, "0")) != 0 for key in ("failed", "error", "timeout", "aborted", "inconclusive", "notRunnable", "disconnected", "pending", "inProgress")):
            raise ValueError(f"Selected test report contains failures: {report}")
        successful = sum(result.get("outcome") == "Passed" for result in results)
        skipped = sum(result.get("outcome") == "NotExecuted" for result in results)
        if successful < 1 or successful + skipped != len(results):
            raise ValueError(f"Selected tests did not complete successfully: {report}")
        definitions = {}
        for definition in root.findall("t:TestDefinitions/t:UnitTest", NS):
            definitions.setdefault(definition.get("id"), []).append(definition)
        for result in results:
            if result.get("outcome") != "NotExecuted":
                continue
            test_id = result.get("testId")
            matches = definitions.get(test_id, []) if test_id else []
            if len(matches) != 1:
                raise ValueError(f"Ignored result has missing/ambiguous test definition: {report}")
            definition = matches[0]
            methods = definition.findall("t:TestMethod", NS)
            if len(methods) != 1 or not methods[0].get("className") or not methods[0].get("name"):
                raise ValueError(f"Ignored result has incomplete method identity: {report}")
            method = methods[0]
            if result.get("testName") != method.get("name") or definition.get("name") != method.get("name"):
                raise ValueError(f"Ignored result disagrees with its method definition: {report}")
            identity = f"{method.get('className')}.{method.get('name')}"
            if identity not in allowed or identity in seen_ignored:
                raise ValueError(f"Unexpected or repeated ignored test {identity}: {report}")
            seen_ignored.add(identity)
        if int(counters.get("passed", "-1")) != successful or int(counters.get("total", "-1")) != len(results):
            raise ValueError(f"TRX counters disagree with actual results: {report}")
        passed += successful
        ignored += skipped
    return {"reports": len(reports), "passed": passed, "ignored": ignored}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory")
    parser.add_argument("--allow-ignored-test", action="append", default=[], metavar="CLASS.METHOD")
    args = parser.parse_args()
    print(verify(args.directory, args.allow_ignored_test))
