"""Require actual passing SQL tests; a zero-match or absent report is not evidence."""
import sys
import xml.etree.ElementTree as ET

report = ET.parse(sys.argv[1])
counters = report.find(".//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters")
if counters is None:
    raise SystemExit("Required SQL Server test report has no counters.")
total = int(counters.get("total", "0"))
passed = int(counters.get("passed", "0"))
if total < 1 or passed != total:
    raise SystemExit("Required SQL Server lane did not execute only passing tests.")
print(f"Qualified SQL Server integration: {passed} passed, zero failed/skipped.")
