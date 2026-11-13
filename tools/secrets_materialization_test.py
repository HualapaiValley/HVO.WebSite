"""Offline materialization regressions; only copied scripts and test-owned files run."""

import json
import os
from pathlib import Path
import re
import shlex
import signal
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

    def write(self, path, content, mode=0o644):
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
        for entry in sorted(self.root.rglob("*")):
            relative = entry.relative_to(self.root)
            if relative.parts[0] in ("bin", "settings.json", "az-calls"):
                continue
            info = entry.stat()
            result[str(relative)] = (
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
                     "jq-error-secret-sentinel",
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
        self.assertNotIn("Detected ", log, "drift must not hide a preparation failure")
        self.assertNotIn("jq:", log, "raw configuration diagnostics escaped")
        self.assertIn("Error:", log)
        return log

    def accept_literal_secret(self, secret, value, key, mode, locale, relative_file):
        self.settings["values"][secret] = value
        self.environment["LC_ALL"] = locale
        if mode == "--check":
            before = self.snapshot()
            result, log = self.run_sync(mode)
            self.assertNotEqual(0, result.returncode, "initial check must report existing drift")
            self.assertIn("Detected ", log, "literal input failed preparation")
            self.assertNotIn("Error:", log)
            self.assertTrue(before == self.snapshot(), "check changed destinations")
        self.materialize()
        before = self.snapshot()
        result, _ = self.run_sync(mode)
        self.assertEqual(0, result.returncode, "literal secret was rejected")
        self.assertTrue(before == self.snapshot(), "matching literal input was rewritten")
        if relative_file == ".env":
            result = subprocess.run([
                "bash", "-euo", "pipefail", "-c",
                'source "$1" >/dev/null 2>&1; key="$3"; [[ "${!key}" == "$2" ]]',
                "--", str(self.root / relative_file), value, key,
            ], env=self.environment, capture_output=True, timeout=10)
            self.assertEqual(0, result.returncode, "root secret failed a byte-for-byte shell round trip")
        else:
            for path in (WEBSITE, HANDOFF):
                self.assertTrue(f"{key}={value}" in (self.root / path).read_text(),
                                "website-only secret was not preserved")
        result, _ = self.run_sync("--check")
        self.assertEqual(0, result.returncode, "literal secret prevented the following check")
        result, _ = self.run_sync("--apply")
        self.assertEqual(0, result.returncode, "literal secret prevented re-apply")
        self.assertTrue(before == self.snapshot(), "re-apply changed matching destinations")

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
        for entry in self.root.rglob("*"):
            relative = entry.relative_to(self.root)
            managed = relative.parts[0] != "custom" and (
                "secrets" in relative.parts or entry.name in (".env", "secrets.yaml")
                or relative.parts[0] == ".env-handoff")
            if entry.is_file() and managed:
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
        self.assertFalse(list(self.root.rglob("*.tmp.*")), "credential temporary file survived failure")

    def fail_rename(self, destination):
        self.write("bin/mv", '#!/bin/bash\nif [[ "${@: -1}" == "' + str(self.root / destination)
                   + '" ]]; then exit 31; fi\nexec /bin/mv "$@"\n', 0o700)

    def test_f6_root_temp_cleanup_on_write_failure(self):
        self.fail_rename(".env")
        original = (self.root / ".env").read_bytes()
        result, _ = self.run_sync("--apply")
        self.assertNotEqual(0, result.returncode)
        self.assertTrue(original == (self.root / ".env").read_bytes(), "failed rename changed destination")
        self.assertFalse(list(self.root.rglob("*.tmp.*")), "root credential temporary file survived failure")

    def test_yaml_temp_cleanup_on_write_failure(self):
        self.fail_rename(YAML)
        original = (self.root / YAML).read_bytes()
        result, _ = self.run_sync("--apply")
        self.assertNotEqual(0, result.returncode)
        self.assertTrue(original == (self.root / YAML).read_bytes())
        self.assertFalse(list(self.root.rglob("*.tmp.*")), "YAML credential temporary file survived failure")

    def test_secret_file_write_failure_preserves_old_content(self):
        self.materialize()
        target = "deploy/pi-gateways/davis/secrets/central-ingest-api-key"
        self.write(target, "previous-credential-sentinel\n", 0o600)
        original = (self.root / target).read_bytes()
        script = self.root / "scripts/sync-secrets-from-keyvault.sh"
        # Fail the first secret-file write after all preparation has completed.
        script.write_text(script.read_text().replace("umask 077", "umask 077\n\t\ttrap '' XFSZ\n\t\tulimit -f 0"))
        result, log = self.run_sync("--apply")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("partial", log.lower())
        self.assertTrue(original == (self.root / target).read_bytes(), "failed write damaged the existing credential")
        self.assertFalse(list(self.root.rglob("*.tmp.*")))

    def test_temp_ignore_patterns(self):
        content = (SCRIPT.parents[1] / ".gitignore").read_text()
        self.write(".gitignore", content)
        subprocess.run(["git", "init", "--quiet", str(self.root)], check=True, capture_output=True)
        for path in (".env.tmp.X", "deploy/hvo-docker/.env.tmp.X", ".env-handoff/hvo-docker.env.tmp.X",
                     ".env-handoff/davis.env.tmp.X", "deploy/pi-gateways/davis/.env.tmp.X",
                     "deploy/pi-gateways/jkbms/.env.tmp.X"):
            result = subprocess.run(["git", "-C", str(self.root), "check-ignore", "--no-index", "-q", path],
                                    capture_output=True)
            self.assertEqual(0, result.returncode, "credential temporary path is not ignored")

    def signal_during_write(self, number):
        self.write("bin/mv", f'#!/bin/bash\nkill -{number} "$PPID"\nexit 0\n', 0o700)
        result, log = self.run_sync("--apply")
        self.assertEqual(128 + number, result.returncode)
        self.assertIn("partial", log.lower())
        self.assertNotIn("Applied ", log)
        self.assertFalse(list(self.root.rglob("*.tmp.*")), "credential temporary file survived a signal")

    def test_sigterm_cleans_temporary_file(self):
        self.signal_during_write(signal.SIGTERM)

    def test_sigint_cleans_temporary_file(self):
        self.signal_during_write(signal.SIGINT)

    def crlf_control(self, path, mode):
        self.materialize()
        target = self.root / path
        target.write_bytes(target.read_bytes().replace(b"\n", b"\r\n"))
        before = self.snapshot()
        result, _ = self.run_sync(mode)
        self.assertEqual(0, result.returncode, "CRLF SQL input was rejected")
        self.assertTrue(before == self.snapshot(), "already matching CRLF input was rewritten")
        result, _ = self.run_sync("--check")
        self.assertEqual(0, result.returncode)

    def test_snapshot_detects_root_temp_creation(self):
        before = self.snapshot()
        self.write(".env.tmp.sentinel", "fixture-only\n")
        self.assertTrue(before != self.snapshot(), "snapshot missed a root temporary file")

    def fail_selection(self, gateway):
        if gateway == "davis":
            self.write("deploy/pi-gateways/davis/gateway.json", '{"WeatherUnderground":{"Enabled":true}}')
        else:
            self.write("deploy/pi-gateways/jkbms/gateway.json.example", json.dumps({"JkBms": {"Devices": [
                {"DeviceId": "bank-2a", "Enabled": True, "SettingsPasswordSecret": "jkbms-bank-2a-settings-password"},
            ]}}))
        self.write("bin/jq", '#!/bin/bash\nfor arg in "$@"; do\n'
                   'if [[ "$arg" == *"select("* || "$arg" == ".WeatherUnderground.Enabled == true" ]]; then\n'
                   'printf "jq-error-secret-sentinel" >&2\nexit 5\nfi\ndone\nexec /usr/bin/jq "$@"\n', 0o700)

    def test_rejection_detects_nonfatal_derivation_mutant(self):
        script = self.root / "scripts/sync-secrets-from-keyvault.sh"
        script.write_text(script.read_text().replace(
            '[[ "$current" == *Password=* ]] || fail', '[[ "$current" == *Password=* ]] || printf'))
        with self.assertRaises(AssertionError):
            self.reject("--check", REJECTIONS["missing_password_segment"])


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
    "plain_secret_space": lambda t: t.settings["values"].update({"obs-github-token": "tok en"}),
    "plain_secret_dollar": lambda t: t.settings["values"].update({"obs-github-token": "abc$x"}),
    "plain_secret_backtick": lambda t: t.settings["values"].update({"obs-github-token": "x`touch PWNED`y"}),
    "plain_secret_substitution": lambda t: t.settings["values"].update({"obs-github-token": "x$(touch PWNED)y"}),
    "plain_secret_tab": lambda t: t.settings["values"].update({"obs-github-token": "token\tvalue"}),
    "plain_secret_single_quote": lambda t: t.settings["values"].update({"obs-github-token": "token'value"}),
    "plain_secret_double_quote": lambda t: t.settings["values"].update({"obs-github-token": 'token"value'}),
    "plain_secret_backslash": lambda t: t.settings["values"].update({"obs-github-token": "token\\value"}),
    "plain_secret_semicolon": lambda t: t.settings["values"].update({"obs-github-token": "token;value"}),
    "plain_secret_ampersand": lambda t: t.settings["values"].update({"obs-github-token": "token&value"}),
    "plain_secret_pipe": lambda t: t.settings["values"].update({"obs-github-token": "token|value"}),
    "plain_secret_less_than": lambda t: t.settings["values"].update({"obs-github-token": "token<value"}),
    "plain_secret_greater_than": lambda t: t.settings["values"].update({"obs-github-token": "token>value"}),
    "plain_secret_open_parenthesis": lambda t: t.settings["values"].update({"obs-github-token": "token(value"}),
    "plain_secret_close_parenthesis": lambda t: t.settings["values"].update({"obs-github-token": "token)value"}),
    "plain_secret_leading_tilde": lambda t: t.settings["values"].update({"obs-github-token": "~/fixture"}),
    "plain_secret_colon_tilde": lambda t: t.settings["values"].update({"obs-github-token": "token:~/fixture"}),
    "plain_secret_equals_tilde": lambda t: t.settings["values"].update({"obs-github-token": "token=~/fixture"}),
    "sql_password_single_quote": lambda t: t.settings["values"].update({SQL_SECRET: "sql'password-sentinel"}),
    "jkbms_truncated_json": lambda t: t.write("deploy/pi-gateways/jkbms/gateway.json.example", '{"JkBms":'),
    "jkbms_missing_devices": lambda t: t.write("deploy/pi-gateways/jkbms/gateway.json.example", '{"JkBms":{}}'),
    "jkbms_wrong_devices_type": lambda t: t.write("deploy/pi-gateways/jkbms/gateway.json.example", '{"JkBms":{"Devices":{}}}'),
    "jkbms_wrong_enabled_type": lambda t: t.write("deploy/pi-gateways/jkbms/gateway.json.example", '{"JkBms":{"Devices":[{"DeviceId":"bank-2a","Enabled":"yes"}]}}'),
    "davis_truncated_json": lambda t: t.write("deploy/pi-gateways/davis/gateway.json", '{"WeatherUnderground":'),
    "davis_wrong_section_type": lambda t: t.write("deploy/pi-gateways/davis/gateway.json", '{"WeatherUnderground":"yes"}'),
    "davis_wrong_enabled_type": lambda t: t.write("deploy/pi-gateways/davis/gateway.json", '{"WeatherUnderground":{"Enabled":"yes"}}'),
    "cwop_wrong_section_type": lambda t: t.write("deploy/pi-gateways/davis/gateway.json", '{"Cwop":true}'),
    "jkbms_selection_error": lambda t: t.fail_selection("jkbms"),
    "davis_selection_error": lambda t: t.fail_selection("davis"),
}


def rejection_test(mode, configure, expected_error=None):
    def test(self):
        log = self.reject(mode, configure)
        if expected_error:
            self.assertIn(expected_error, log)
    return test


for case, configure in REJECTIONS.items():
    for mode in ("--check", "--apply"):
        expected_error = "The SQL password cannot contain a single quote." if case == "sql_password_single_quote" else None
        setattr(MaterializationTests, f"test_reject_{case}_{mode[2:]}", rejection_test(mode, configure, expected_error))

LITERAL_SECRETS = {
    "exclamation": ("obs-github-token", "literal!sentinel", "GH_PAT", "C", ".env"),
    "comma": ("obs-github-token", "literal,sentinel", "GH_PAT", "C", ".env"),
    "asterisk": ("obs-github-token", "literal*sentinel", "GH_PAT", "C", ".env"),
    "question_mark": ("obs-github-token", "literal?sentinel", "GH_PAT", "C", ".env"),
    "square_brackets": ("obs-github-token", "literal[sentinel]", "GH_PAT", "C", ".env"),
    "braces": ("obs-github-token", "literal{sentinel}", "GH_PAT", "C", ".env"),
    "caret": ("obs-github-token", "literal^sentinel", "GH_PAT", "C", ".env"),
    "midword_hash": ("obs-github-token", "literal#sentinel", "GH_PAT", "C", ".env"),
    "leading_hash": ("obs-github-token", "#literal-sentinel", "GH_PAT", "C", ".env"),
    "midword_tilde": ("obs-github-token", "literal~sentinel", "GH_PAT", "C", ".env"),
    "sql_punctuation": (SQL_SECRET, "P@ss!w0rd,1*", "MSSQL_SA_PASSWORD", "C", ".env"),
    "wifi_punctuation": ("obs-wifi-hvo-password", "wifi!pass?", "ESPHOME_HVO_PROXY_WIFI_PASSWORD", "C", ".env"),
    "unicode_c": ("obs-wifi-hvo-password", "café-wifi", "ESPHOME_HVO_PROXY_WIFI_PASSWORD", "C", ".env"),
    "unicode_utf8": ("obs-wifi-hvo-password", "café-wifi", "ESPHOME_HVO_PROXY_WIFI_PASSWORD", "C.UTF-8", ".env"),
    "website_only_punctuation": ("WebsiteRuntime--AzureClientSecret", "rt!secret{x}", "AZURE_CLIENT_SECRET", "C", WEBSITE),
    "website_only_space": ("WebsiteRuntime--AzureClientSecret", "runtime secret!{x}", "AZURE_CLIENT_SECRET", "C", WEBSITE),
}


def literal_test(mode, parameters):
    def test(self):
        secret, value, key, locale, relative_file = parameters
        self.accept_literal_secret(secret, value, key, mode, locale, relative_file)
    return test


for case, parameters in LITERAL_SECRETS.items():
    for mode in ("--check", "--apply"):
        setattr(MaterializationTests, f"test_accept_literal_{case}_{mode[2:]}", literal_test(mode, parameters))

for path in (WEBSITE, HANDOFF):
    for mode in ("--check", "--apply"):
        def crlf_test(self, path=path, mode=mode):
            self.crlf_control(path, mode)
        case = "website" if path == WEBSITE else "handoff"
        setattr(MaterializationTests, f"test_crlf_{case}_{mode[2:]}", crlf_test)


if __name__ == "__main__":
    unittest.main()
