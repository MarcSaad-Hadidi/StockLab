"""Fail CI unless every discovered SQL Server test executed and passed."""

import argparse
from collections import Counter
from pathlib import Path
import sys
from xml.etree import ElementTree as ET


def verify(trx: Path, expected_tests: Path) -> int:
    expected = expected_tests.read_text(encoding="utf-8-sig").splitlines()
    if not expected or len(set(expected)) != len(expected):
        raise ValueError("SQL Server discovery must contain unique, nonempty test cases.")
    if any(not name.startswith("StockLab.UnitTests.") or "SqlServer" not in name for name in expected):
        raise ValueError("Discovery contains a case outside the SQL Server test group.")

    report = ET.parse(trx)
    results = report.findall(".//{*}UnitTestResult")
    actual = [result.get("testName") for result in results]
    if Counter(actual) != Counter(expected):
        missing = list((Counter(expected) - Counter(actual)).elements())
        unexpected = list((Counter(actual) - Counter(expected)).elements())
        raise ValueError(f"SQL Server coverage mismatch. Missing: {missing}; unexpected: {unexpected}")
    unsuccessful = [(result.get("testName"), result.get("outcome"))
                    for result in results if result.get("outcome") != "Passed"]
    if unsuccessful:
        raise ValueError(f"SQL Server cases did not execute successfully: {unsuccessful}")

    counters = report.find(".//{*}ResultSummary/{*}Counters")
    if counters is None:
        raise ValueError("The SQL Server report has no result counters.")
    for name in ("total", "executed", "passed"):
        if int(counters.get(name, "-1")) != len(expected):
            raise ValueError(f"The report's {name} counter does not match SQL Server discovery.")
    for name in ("failed", "error", "notExecuted"):
        if int(counters.get(name, "-1")) != 0:
            raise ValueError(f"The SQL Server report's {name} counter must be zero.")
    return len(expected)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--trx", required=True, type=Path)
    parser.add_argument("--expected-tests", required=True, type=Path)
    arguments = parser.parse_args()
    try:
        count = verify(arguments.trx, arguments.expected_tests)
    except (OSError, ValueError, ET.ParseError) as error:
        print(f"SQL Server verification failed: {error}", file=sys.stderr)
        return 1
    print(f"{count} SQL Server tests executed and passed; none missing or skipped.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
