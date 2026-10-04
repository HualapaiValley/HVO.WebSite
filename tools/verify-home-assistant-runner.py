"""Exercise HA selection, preparation, reports and cleanup without Docker/services."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

SOURCE = Path(__file__).resolve().parent
MQTT = "tests/HVO.Edge.HomeAssistant.Mqtt.Tests/HVO.Edge.HomeAssistant.Mqtt.Tests.csproj"
EXPORTER = "tests/HVO.Edge.Exporter.HomeAssistant.Tests/HVO.Edge.Exporter.HomeAssistant.Tests.csproj"
MIGRATION = "tests/HVO.Tools.HomeAssistantEntityMigration.Tests/HVO.Tools.HomeAssistantEntityMigration.Tests.csproj"

FAKE = r'''#!/usr/bin/env python3
import json,os,sys
from pathlib import Path
name=Path(sys.argv[0]).name
args=sys.argv[1:]
with open(os.environ['FIXTURE_LOG'],'a') as log: log.write(json.dumps([name,*args])+'\n')
if name=='docker':
    if 'up' in args and os.environ.get('FAIL_UP'): sys.exit(23)
    if 'down' in args and os.environ.get('FAIL_DOWN'): sys.exit(43)
    if 'port' in args: print('127.0.0.1:12345')
    if args[:2]==['ps','-aq'] and os.environ.get('REMAINING'): print('owned-resource')
elif name=='openssl': print('fixture-password')
elif name=='curl':
    url=next(arg for arg in args if arg.startswith('http:'))
    state=Path(os.environ['FIXTURE_STATE'])
    if '/__test/central-ingest/' in url: state.write_text(url.rsplit('/',1)[1])
    elif '/api/telemetry' in url: print('503' if state.exists() and state.read_text()=='unavailable' else '201')
    elif '/api/onboarding/users' in url: print('{"auth_code":"fixture-code"}')
    elif '/auth/token' in url: print('{"access_token":"fixture-access","refresh_token":"fixture-refresh"}')
    elif '/api/config/config_entries/flow/' in url: print('{"type":"create_entry"}')
    elif '/api/config/config_entries/flow' in url: print('{"flow_id":"fixture-flow"}')
    elif '/api/config/config_entries/entry' in url: print('[{"domain":"mqtt","state":"loaded"}]')
    else: print('{}')
elif name=='dotnet':
    if args[0]=='build' and os.environ.get('FAIL_BUILD'): sys.exit(19)
    if args[0]=='test':
        if os.environ.get('FAIL_TEST'): sys.exit(37)
        directory=Path(args[args.index('--results-directory')+1]);directory.mkdir(parents=True,exist_ok=True)
        if not os.environ.get('NO_REPORT'):
            outcome='NotExecuted' if os.environ.get('IGNORED_REPORT') else 'Passed'
            passed=0 if outcome=='NotExecuted' else 1
            (directory/'current.trx').write_text(f'<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results><UnitTestResult outcome="{outcome}"/></Results><ResultSummary><Counters total="1" passed="{passed}" failed="0"/></ResultSummary></TestRun>')
'''


class HomeAssistantRunnerTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="hvo-ha-runner-fixture-")
        self.root = Path(self.temporary.name)
        tools = self.root / "tools"
        tools.mkdir()
        for name in ("run-home-assistant-integration-tests.sh", "ci-results.py"):
            shutil.copy2(SOURCE / name, tools / name)
        (tools / "validate-home-assistant-managed-config.sh").write_text("#!/bin/sh\nexit 0\n")
        (tools / "validate-home-assistant-managed-config.sh").chmod(0o755)
        (self.root / "integration.runsettings").write_text("fixture settings")
        bin_directory = self.root / "bin"
        bin_directory.mkdir()
        dispatcher = bin_directory / "fake-command"
        dispatcher.write_text(FAKE)
        dispatcher.chmod(0o755)
        for name in ("docker", "dotnet", "curl", "openssl"):
            (bin_directory / name).symlink_to(dispatcher)
        self.log = self.root / "commands.jsonl"
        (self.root / "temporary-config").mkdir()
        self.env = {**os.environ, "PATH": f"{bin_directory}:{os.environ['PATH']}",
                    "FIXTURE_LOG": str(self.log), "FIXTURE_STATE": str(self.root / "ingest-state"),
                    "TMPDIR": str(self.root / "temporary-config")}

    def tearDown(self):
        self.temporary.cleanup()

    def run_script(self, *arguments, **environment):
        result = subprocess.run(["bash", str(self.root / "tools/run-home-assistant-integration-tests.sh"), *arguments],
                                env={**self.env, **environment}, capture_output=True, text=True, timeout=15)
        self.commands = [json.loads(line) for line in self.log.read_text().splitlines()] if self.log.exists() else []
        return result

    def test_selected_prebuilt_only_runs_selected_ha_without_restore_or_coverage(self):
        result = self.run_script("--prebuilt", "--projects", MQTT)
        self.assertEqual(result.returncode, 0, result.stderr)
        dotnet = [args for args in self.commands if args[0] == "dotnet"]
        self.assertEqual(len(dotnet), 1)
        command = dotnet[0]
        self.assertEqual(command[1:3], ["test", str(self.root / MQTT)])
        self.assertIn("--no-build", command)
        self.assertIn("--no-restore", command)
        self.assertEqual(command[command.index("--filter") + 1], "TestCategory=HomeAssistantIntegration&TestCategory!=Live")
        self.assertEqual(command[command.index("--settings") + 1], str(self.root / "integration.runsettings"))
        self.assertFalse(any("--collect" in arg for arg in command))

    def test_default_standalone_prepares_three_projects_before_provisioning(self):
        result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stderr)
        restores = [args for args in self.commands if args[:2] == ["dotnet", "restore"]]
        builds = [args for args in self.commands if args[:2] == ["dotnet", "build"]]
        tests = [args for args in self.commands if args[:2] == ["dotnet", "test"]]
        self.assertEqual(len(restores), 3)
        self.assertTrue(all("--locked-mode" in args for args in restores))
        self.assertEqual(len(builds), 3)
        self.assertTrue(all("--no-restore" in args for args in builds))
        self.assertEqual(len(tests), 3)
        self.assertEqual(len({args[args.index("--results-directory") + 1] for args in tests}), 3)
        docker_index = next(i for i, args in enumerate(self.commands) if args[0] == "docker")
        self.assertTrue(all(self.commands.index(args) < docker_index for args in builds))

    def test_coverage_is_explicit_and_configuration_results_are_preserved(self):
        directory = str(self.root / "custom-results")
        result = self.run_script("--prebuilt", "--projects", EXPORTER, "--coverage", "--configuration", "Release", "--results-directory", directory)
        self.assertEqual(result.returncode, 0, result.stderr)
        command = next(args for args in self.commands if args[:2] == ["dotnet", "test"])
        self.assertIn("--collect:XPlat Code Coverage", command)
        self.assertEqual(command[command.index("-c") + 1], "Release")
        self.assertTrue(command[command.index("--results-directory") + 1].startswith(directory))

    def test_invalid_empty_duplicate_or_nonha_selection_runs_no_commands(self):
        for arguments in [("--projects",), ("--projects", "unknown"), ("--projects", MQTT, MQTT), ("--unknown",), ("--projects", "tests/HVO.WebSite.ApiTests/HVO.WebSite.ApiTests.csproj")]:
            with self.subTest(arguments=arguments):
                result = self.run_script(*arguments)
                self.assertEqual(result.returncode, 2)
                self.assertEqual(self.commands, [])

    def test_build_failure_precedes_docker(self):
        result = self.run_script("--projects", MQTT, FAIL_BUILD="1")
        self.assertEqual(result.returncode, 19)
        self.assertFalse(any(args[0] == "docker" for args in self.commands))

    def test_failure_diagnostics_and_cleanup_preserve_test_exit(self):
        result = self.run_script("--prebuilt", "--projects", MQTT, FAIL_TEST="1", FAIL_DOWN="1")
        self.assertEqual(result.returncode, 37)
        self.assertTrue(any("ps" in args and "--all" in args for args in self.commands))
        self.assertTrue(any("logs" in args and "mosquitto" in args for args in self.commands))
        self.assertTrue(any("down" in args for args in self.commands))
        self.assertTrue(any(args[:2] == ["docker", "ps"] for args in self.commands))
        self.assertEqual(list((self.root / "temporary-config").iterdir()), [])

    def test_provisioning_failure_cleans_stack_before_any_test(self):
        result = self.run_script("--prebuilt", "--projects", MQTT, FAIL_UP="1")
        self.assertEqual(result.returncode, 23)
        self.assertFalse(any(args[0] == "dotnet" for args in self.commands))
        self.assertTrue(any("logs" in args and "mosquitto" in args for args in self.commands))
        self.assertTrue(any("down" in args for args in self.commands))
        self.assertEqual(list((self.root / "temporary-config").iterdir()), [])

    def test_missing_reports_fail_even_when_stale_success_existed(self):
        directory = self.root / "TestResults/integration/home-assistant/HVO.Edge.HomeAssistant.Mqtt.Tests"
        directory.mkdir(parents=True)
        (directory / "stale.trx").write_text("stale report")
        result = self.run_script("--prebuilt", "--projects", MQTT, NO_REPORT="1")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("No TRX reports", result.stderr)
        self.assertFalse((directory / "stale.trx").exists())
        self.assertTrue(any("down" in args for args in self.commands))

    def test_ignored_ha_report_is_not_a_passing_suite(self):
        result = self.run_script("--prebuilt", "--projects", MQTT, IGNORED_REPORT="1")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("did not complete successfully", result.stderr)

    def test_cleanup_failure_fails_otherwise_successful_tests(self):
        result = self.run_script("--prebuilt", "--projects", MQTT, FAIL_DOWN="1")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Failed to remove", result.stderr)

    def test_owned_resource_leak_fails_otherwise_successful_tests(self):
        result = self.run_script("--prebuilt", "--projects", MIGRATION, REMAINING="1")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("resources remain", result.stderr)


if __name__ == "__main__":
    unittest.main(verbosity=2)
