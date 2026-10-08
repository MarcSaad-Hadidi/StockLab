import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from xml.etree import ElementTree as ET


VALIDATOR = Path(__file__).resolve().parents[1] / "verify_sql_test_results.py"
NAMESPACE = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"
TESTS = [
    "StockLab.UnitTests.Api.PriceAlertsSqlServerApiTests.Sql_login_CRUD_and_disabled_persistence_survive_restart",
    'StockLab.UnitTests.Api.PriceAlertsSqlServerApiTests.Sql_generated_rowversion_conflicts_return_409(method: "PUT", suffix: "")',
    'StockLab.UnitTests.Api.PriceAlertsSqlServerApiTests.Sql_generated_rowversion_conflicts_return_409(method: "POST", suffix: "/disable")',
    'StockLab.UnitTests.Api.PriceAlertsSqlServerApiTests.Sql_generated_rowversion_conflicts_return_409(method: "DELETE", suffix: "")',
]


class SqlResultsTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.expected = Path(self.directory.name) / "expected.txt"
        self.report = Path(self.directory.name) / "sql.trx"
        self.expected.write_text("\n".join(TESTS), encoding="utf-8-sig")
        self.write_report([(name, "Passed") for name in TESTS])

    def write_report(self, results, counters=None):
        root = ET.Element("TestRun", xmlns=NAMESPACE)
        records = ET.SubElement(root, "Results")
        for name, outcome in results:
            ET.SubElement(records, "UnitTestResult", testName=name, outcome=outcome)
        summary = ET.SubElement(root, "ResultSummary", outcome="Completed")
        values = dict(total=str(len(results)), executed=str(len(results)), passed=str(len(results)),
                      failed="0", error="0", notExecuted="0")
        values.update(counters or {})
        ET.SubElement(summary, "Counters", **values)
        ET.ElementTree(root).write(self.report, encoding="utf-8", xml_declaration=True)

    def run_validator(self):
        return subprocess.run(
            [sys.executable, str(VALIDATOR), "--trx", str(self.report), "--expected-tests", str(self.expected)],
            capture_output=True, text=True,
        )

    def assert_rejected(self):
        result = self.run_validator()
        self.assertNotEqual(0, result.returncode)

    def test_accepts_every_discovered_parameterized_case_when_passed(self):
        result = self.run_validator()
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("4 SQL Server tests executed and passed", result.stdout)

    def test_rejects_skipped_failed_or_aborted_cases_even_with_positive_counters(self):
        for outcome in ("NotExecuted", "Failed", "Error", "Aborted"):
            with self.subTest(outcome=outcome):
                self.write_report([(TESTS[0], outcome)] + [(name, "Passed") for name in TESTS[1:]])
                self.assert_rejected()

    def test_rejects_missing_parameterized_case(self):
        self.write_report([(name, "Passed") for name in TESTS[:-1]])
        self.assert_rejected()

    def test_rejects_unexpected_case(self):
        self.write_report([(name, "Passed") for name in TESTS] + [(TESTS[0] + "_extra", "Passed")])
        self.assert_rejected()

    def test_rejects_duplicate_results(self):
        self.write_report([(name, "Passed") for name in TESTS[:-1]] + [(TESTS[0], "Passed")])
        self.assert_rejected()

    def test_rejects_empty_or_duplicate_discovery(self):
        for names in ([], TESTS + [TESTS[0]]):
            with self.subTest(names=names):
                self.expected.write_text("\n".join(names), encoding="utf-8")
                self.assert_rejected()

    def test_rejects_non_sql_discovery(self):
        self.expected.write_text("StockLab.UnitTests.Api.PortfolioApiTests.Empty_portfolio", encoding="utf-8")
        self.write_report([("StockLab.UnitTests.Api.PortfolioApiTests.Empty_portfolio", "Passed")])
        self.assert_rejected()

    def test_rejects_inconsistent_or_missing_counters(self):
        for counters in ({"executed": "3"}, {"passed": "3"}, {"total": "5"},
                         {"notExecuted": "1"}, {"failed": "1"}, {"error": "1"}, {"total": "bad"}):
            with self.subTest(counters=counters):
                self.write_report([(name, "Passed") for name in TESTS], counters)
                self.assert_rejected()
        root = ET.parse(self.report)
        summary = root.find(f"{{{NAMESPACE}}}ResultSummary")
        summary.clear()
        root.write(self.report)
        self.assert_rejected()

    def test_rejects_empty_results(self):
        self.write_report([])
        self.assert_rejected()

    def test_rejects_missing_report(self):
        self.report.unlink()
        self.assert_rejected()

    def test_rejects_malformed_report(self):
        self.report.write_text("not XML", encoding="utf-8")
        self.assert_rejected()

    def test_rejects_missing_discovery(self):
        self.expected.unlink()
        self.assert_rejected()


if __name__ == "__main__":
    unittest.main()
