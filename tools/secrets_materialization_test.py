"""Offline materialization regressions; only copied scripts and test-owned files run."""

import json
import os
from pathlib import Path
import re
import shlex
import stat
import subprocess
import tempfile
import unittest


SCRIPT = Path(__file__).resolve().parents[1] / "scripts/sync-secrets-from-keyvault.sh"
SCRIPT_TEXT = Path(os.environ.get("HVO_MATERIALIZATION_SCRIPT_UNDER_TEST", SCRIPT)).read_text()
SQL_KEY = "ConnectionStrings__HualapaiValleyObservatory"
SQL = "Server=sql.example.invalid,14330;Database=fixture;Password=old-sql-sentinel;Encrypt=True;TrustServerCertificate=False;"
SSH = "offline-ssh-sentinel\nsecond-ssh-line"
WEBSITE = "deploy/hvo-docker/.env"
HANDOFF = ".env-handoff/hvo-docker.env"
YAML = "deploy/home-assistant/configuration/esphome/secrets.yaml"
SQL_SECRET = "obs-docker-mssql-sa-password"
LATE_SECRET = "HomeAssistant--EspHomeProxyFallbackPassword"

AZ_STUB = '''#!/usr/bin/python3
import json, os, sys
from pathlib import Path
args = sys.argv[1:]
if args[:2] in (["account", "show"], ["keyvault", "show"]):
    sys.exit(0)
if args[:3] != ["keyvault", "secret", "show"]:
    sys.exit(99)
name = args[args.index("--name") + 1]
with open(os.environ["STUB_CALLS"], "a") as calls:
    calls.write(name + "\\n")
settings = json.loads(Path(os.environ["STUB_SETTINGS"]).read_text())
if name == settings.get("fail"):
    print("failed-read-stdout-sentinel", end="")
    print("failed-read-stderr-sentinel", file=sys.stderr)
    sys.exit(23)
print(settings.get("values", {}).get(name, "offline-vault-sentinel-" + name), end="")
'''


class MaterializationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="kv437-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.write("scripts/sync-secrets-from-keyvault.sh", SCRIPT_TEXT)
        self.write("bin/az", AZ_STUB, 0o700)
        self.settings = {"values": {"obs-ssh-private-key": SSH}}
        self.bootstrap()
        for path in (WEBSITE, HANDOFF):
            self.write(path, f"# owner settings\nOWNER_SETTING=keep\n{SQL_KEY}={SQL}\n")
        self.write("deploy/pi-gateways/davis/.env", "DAVIS_STATION_HOST=192.0.2.10\nDAVIS_API_KEY=old-davis-sentinel\n")
        self.write(".env-handoff/jkbms.env", "BMS_API_KEY=old-bms-sentinel\n")
        self.write("deploy/pi-gateways/smartshunt/secrets/mqtt-password", "old-mqtt-sentinel\n")
        self.write(YAML, 'owner_ssid: "keep"\nhvo_wifi_password: "old-wifi-sentinel"\n')
        self.write("custom/website.env", "CUSTOM_SENTINEL=untouched\n")
        self.environment = {
            "PATH": f"{self.root / 'bin'}:/usr/bin:/bin",
            "HOME": str(self.root),
            "TMPDIR": str(self.root),
            "MSBUILDDISABLENODEREUSE": "1",
            "STUB_SETTINGS": str(self.root / "settings.json"),
            "STUB_CALLS": str(self.root / "az-calls"),
        }

    def write(self, path, content, mode=0o600):
        target = self.root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content)
        target.chmod(mode)

    def bootstrap(self, sql=SQL, ssh=SSH):
        self.write(".env", "# trusted offline bootstrap\nOWNER_SETTING=keep\n"
                   + (f"SSH_PRIVATE_KEY={shlex.quote(ssh)}\n" if ssh is not None else "")
                   + (f"{SQL_KEY}={shlex.quote(sql)}\n" if sql is not None else ""))

    def snapshot(self):
        result = {}
        for relative in (".env", "deploy", ".env-handoff", "custom"):
            path = self.root / relative
            for entry in [path, *sorted(path.rglob("*"))]:
                if entry.exists():
                    info = entry.stat()
                    result[str(entry.relative_to(self.root))] = (
                        info.st_mode, info.st_ino, info.st_mtime_ns,
                        entry.read_bytes() if entry.is_file() else None,
                    )
        return result

    def run_sync(self, mode, trace=False):
        self.write("settings.json", json.dumps(self.settings))
        command = ["bash", *(["-x"] if trace else []), str(self.root / "scripts/sync-secrets-from-keyvault.sh"), mode]
        result = subprocess.run(command, cwd=self.root, env=self.environment,
                                capture_output=True, text=True, timeout=30)
        # Do not include captured output or fixture values in assertion failures.
        log = result.stdout + result.stderr
        forbidden = [SSH, "second-ssh-line", "bootstrap-output-sentinel", "old-sql-sentinel", "old-davis-sentinel",
                     "old-bms-sentinel", "old-mqtt-sentinel", "old-wifi-sentinel",
                     "offline-vault-sentinel-", "failed-read-stdout-sentinel",
                     "failed-read-stderr-sentinel", *self.settings["values"].values()]
        self.assertFalse(any(value and value in log for value in forbidden), "a secret value reached logs")
        if not trace and result.returncode == 0:
            self.assertTrue(not result.stderr, "success wrote diagnostics")
        return result, log

    def reject(self, mode, configure):
        configure(self)
        before = self.snapshot()
        result, log = self.run_sync(mode)
        self.assertNotEqual(0, result.returncode, "invalid input was accepted")
        self.assertTrue(before == self.snapshot(), "preflight rejection changed destination contents or metadata")
        self.assertNotIn("updated ", log)
        self.assertNotIn("Applied ", log)
        self.assertNotIn("All allowlisted", log)
        self.assertIn("Error:", log)

    def materialize(self):
        result, _ = self.run_sync("--apply")
        self.assertEqual(0, result.returncode, "valid setup apply failed")

    def empty_materialized_sql(self):
        self.materialize()
        path = self.root / ".env"
        path.write_text(re.sub(rf"^{SQL_KEY}=.*$", f"{SQL_KEY}=", path.read_text(), flags=re.MULTILINE))

    def test_valid_apply_then_check(self):
        result, _ = self.run_sync("--apply")
        self.assertEqual(0, result.returncode, "valid apply failed")
        password = "offline-vault-sentinel-" + SQL_SECRET
        expected_sql = SQL.replace("old-sql-sentinel", password)
        for path in (".env", WEBSITE, HANDOFF):
            content = (self.root / path).read_text()
            self.assertTrue(expected_sql in content, "SQL target/options were not preserved")
            self.assertTrue("OWNER_SETTING=keep" in content, "owner setting was changed")
        source = subprocess.run(["bash", "-c", 'source "$1"; [[ "$SSH_PRIVATE_KEY" == "$2" ]]',
                                 "fixture", str(self.root / ".env"), SSH],
                                env=self.environment, capture_output=True, timeout=10)
        self.assertEqual(0, source.returncode, "SSH bootstrap changed")
        before = self.snapshot()
        checked, _ = self.run_sync("--check")
        self.assertEqual(0, checked.returncode, "valid check failed")
        self.assertTrue(before == self.snapshot(), "check changed destinations")
        applied, _ = self.run_sync("--apply")
        self.assertEqual(0, applied.returncode, "idempotent apply failed")
        self.assertTrue(before == self.snapshot(), "idempotent apply changed destinations")
        for entry in (self.root / "deploy").rglob("*"):
            if entry.is_file() and ("secrets" in entry.parts or entry.name in (".env", "secrets.yaml")):
                self.assertEqual(0o600, stat.S_IMODE(entry.stat().st_mode))
            if entry.is_dir() and entry.name == "secrets":
                self.assertEqual(0o700, stat.S_IMODE(entry.stat().st_mode))
        calls = (self.root / "az-calls").read_text().splitlines()
        self.assertEqual(3, calls.count(SQL_SECRET), "a run should fetch each secret once")

    def test_valid_check_reports_drift_without_writes(self):
        before = self.snapshot()
        result, log = self.run_sync("--check")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Detected ", log)
        self.assertTrue(before == self.snapshot(), "drift check changed destinations")

    def test_optional_paths_stay_absent_and_custom_paths_untouched(self):
        for path in (WEBSITE, HANDOFF, YAML):
            (self.root / path).unlink()
        custom = (self.root / "custom/website.env").read_bytes()
        result, _ = self.run_sync("--apply")
        self.assertEqual(0, result.returncode)
        for path in (WEBSITE, HANDOFF, YAML,
                     "deploy/pi-gateways/davis/secrets/weather-underground-station-key",
                     "deploy/pi-gateways/davis/secrets/cwop-passcode",
                     "deploy/pi-gateways/home-assistant-exporter/secrets/central-ingest-api-key"):
            self.assertFalse((self.root / path).exists(), "an optional destination was created")
        self.assertTrue(custom == (self.root / "custom/website.env").read_bytes())

    def test_optional_registered_secrets_only(self):
        self.write("deploy/pi-gateways/davis/gateway.json", json.dumps({
            "WeatherUnderground": {"Enabled": True},
            "Cwop": {"Enabled": True, "PasscodeSecret": "custom-passcode"},
        }))
        self.write("deploy/pi-gateways/jkbms/gateway.json.example", json.dumps({"JkBms": {"Devices": [
            {"DeviceId": "bank-2a", "Enabled": True, "SettingsPasswordSecret": "jkbms-bank-2a-settings-password"},
            {"DeviceId": "bank-2b", "Enabled": False, "SettingsPasswordSecret": "jkbms-bank-2b-settings-password"},
            {"DeviceId": "bank-2c", "Enabled": True, "SettingsPasswordSecret": "custom-password"},
        ]}}))
        result, _ = self.run_sync("--apply")
        self.assertEqual(0, result.returncode)
        calls = (self.root / "az-calls").read_text().splitlines()
        self.assertIn("WeatherUnderground--StationKey", calls)
        self.assertIn("Edge--JkBms--Bank2ASettingsPassword", calls)
        for name in ("Cwop--AprsIsPasscode", "Edge--JkBms--Bank2BSettingsPassword", "Edge--JkBms--Bank2CSettingsPassword"):
            self.assertNotIn(name, calls)

    def test_ssh_drift_is_preserved_and_reported(self):
        self.settings["values"]["obs-ssh-private-key"] = "different-ssh-sentinel"
        result, log = self.run_sync("--apply")
        self.assertEqual(0, result.returncode)
        self.assertIn("approved manual rotation", log)
        self.assertTrue(shlex.quote(SSH) in (self.root / ".env").read_text(), "SSH was rewritten")
        result, _ = self.run_sync("--check")
        self.assertNotEqual(0, result.returncode, "SSH drift was lost")

    def test_tracing_cannot_log_values(self):
        result, _ = self.run_sync("--apply", trace=True)
        self.assertEqual(0, result.returncode)

    def test_write_failure_is_nonzero_and_explicitly_partial(self):
        self.write("bin/mv", '#!/bin/bash\nif [[ "${@: -1}" == */deploy/hvo-docker/.env ]]; then exit 31; fi\nexec /bin/mv "$@"\n', 0o700)
        result, log = self.run_sync("--apply")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("partial", log.lower())
        self.assertNotIn("Applied ", log)
        self.assertTrue("TAILSCALE_AUTHKEY=" in (self.root / ".env").read_text(), "failure did not exercise a partial apply")


REJECTIONS = {
    "missing_bootstrap": lambda t: (t.root / ".env").unlink(),
    "missing_ssh": lambda t: (t.bootstrap(ssh=None), t.environment.update(SSH_PRIVATE_KEY=SSH)),
    "empty_ssh": lambda t: t.bootstrap(ssh=""),
    "invalid_shell_bootstrap": lambda t: t.write(".env", "SSH_PRIVATE_KEY='unterminated\n"),
    "failing_bootstrap": lambda t: t.write(".env", "printf 'bootstrap-output-sentinel' >&2\nreturn 9\n"),
    "missing_root_sql": lambda t: t.bootstrap(sql=None),
    "missing_password_segment": lambda t: t.bootstrap(sql="Server=sql.example.invalid;Database=fixture;"),
    "missing_semicolon": lambda t: t.bootstrap(sql="Server=sql.example.invalid;Password=old-sql-sentinel"),
    "empty_root_sql": lambda t: t.bootstrap(sql=""),
    "empty_materialized_sql": lambda t: t.empty_materialized_sql(),
    "empty_materialized_sql_secret": lambda t: (t.materialize(), t.settings["values"].update({SQL_SECRET: ""})),
    "malformed_website_sql": lambda t: t.write(WEBSITE, f"{SQL_KEY}=Server=sql.example.invalid;Password=old-sql-sentinel\n"),
    "malformed_handoff_sql": lambda t: t.write(HANDOFF, f"{SQL_KEY}=\n"),
    "early_secret_read_failure": lambda t: t.settings.update(fail="obs-tailscale-authkey"),
    "sql_secret_read_failure": lambda t: t.settings.update(fail=SQL_SECRET),
    "gateway_secret_read_failure": lambda t: t.settings.update(fail="Edge--SmartShunt--DiagnosticsApiKey"),
    "late_secret_read_failure": lambda t: t.settings.update(fail=LATE_SECRET),
    "empty_sql_secret": lambda t: t.settings["values"].update({SQL_SECRET: ""}),
    "multiline_dotenv_secret": lambda t: t.settings["values"].update({"obs-github-token": "multiline-secret-sentinel\nsecond-line"}),
    "unsafe_yaml_secret": lambda t: t.settings["values"].update({LATE_SECRET: 'yaml-secret-sentinel"unsafe'}),
}


def rejection_test(mode, configure):
    def test(self):
        self.reject(mode, configure)
    return test


for case, configure in REJECTIONS.items():
    for mode in ("--check", "--apply"):
        setattr(MaterializationTests, f"test_reject_{case}_{mode[2:]}", rejection_test(mode, configure))


if __name__ == "__main__":
    unittest.main()
