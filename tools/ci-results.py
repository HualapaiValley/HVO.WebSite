"""Reject empty, failed, incomplete or stale-by-reuse selected test evidence."""
import argparse
from pathlib import Path
import xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def verify(directory, allowed_ignored=()):
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
        unexpected_ignored = any(result.get("outcome") == "NotExecuted" and result.get("testName") not in allowed_ignored for result in results)
        if successful < 1 or successful + skipped != len(results) or unexpected_ignored:
            raise ValueError(f"Selected tests did not complete successfully: {report}")
        if int(counters.get("passed", "-1")) != successful or int(counters.get("total", "-1")) != len(results):
            raise ValueError(f"TRX counters disagree with actual results: {report}")
        passed += successful
        ignored += skipped
    return {"reports": len(reports), "passed": passed, "ignored": ignored}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory")
    parser.add_argument("--allow-ignored-test", action="append", default=[])
    args = parser.parse_args()
    print(verify(args.directory, args.allow_ignored_test))
