"""Offline materialization regressions; only copied scripts and test-owned files run."""

import json
import os
from pathlib import Path
import re
import shlex
import shutil
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
value = settings.get("values", {}).get(name, "offline-vault-sentinel-" + name)
output = args[args.index("--output") + 1]
if output == "json":
    print(json.dumps(value))
elif output == "tsv":
    print(value)
else:
    sys.exit(98)
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
            info = entry.lstat()
            result[str(relative)] = (
                info.st_mode, info.st_ino, info.st_mtime_ns,
                os.readlink(entry) if entry.is_symlink() else entry.read_bytes() if entry.is_file() else None,
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

    def reject(self, mode, configure, expected_secret=None):
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
        if expected_secret:
            self.assertIn(expected_secret, log, "error did not identify the probed secret")
        return log

    def accept_literal_secret(self, secret, value, key, mode, locale, relative_file):
        self.settings["values"][secret] = value
        if not value.isascii() and relative_file == ".env":
            (self.root / YAML).unlink()  # Exercise root grammar independently of YAML restrictions.
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
                compose = self.root / "compose.yaml"
                compose.write_text("services:\n  fixture:\n    image: fixture\n    environment:\n      VALUE: ${" + key + "}\n")
                parsed = subprocess.run(["docker", "compose", "--env-file", str(self.root / path),
                                         "-f", str(compose), "config", "--format", "json"],
                                        env=self.environment, capture_output=True, text=True, check=True)
                self.assertTrue(value == json.loads(parsed.stdout)["services"]["fixture"]["environment"]["VALUE"], "Compose did not preserve the credential")
                compose.unlink()
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
        self.materialize()
        self.settings["values"]["obs-ssh-private-key"] = "different-ssh-sentinel"
        result, log = self.run_sync("--apply")
        self.assertEqual(0, result.returncode)
        self.assertIn("approved manual rotation", log)
        self.assertIn("Applied 0 Key Vault synchronization update(s).", log)
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

    def root_managed_crlf_control(self):
        self.materialize()
        root = self.root / ".env"
        content = root.read_bytes()
        content, count = re.subn(rb"^(GH_PAT=[^\r\n]*)\n", rb"\1\r\n", content, flags=re.MULTILINE)
        self.assertEqual(1, count, "managed fixture line missing")
        root.write_bytes(content)
        before = self.snapshot()
        checked, log = self.run_sync("--check")
        self.assertNotEqual(0, checked.returncode, "root CR was not reported as drift")
        self.assertIn("drift   .env:GH_PAT", log)
        self.assertTrue(before == self.snapshot(), "check changed destinations")
        applied, log = self.run_sync("--apply")
        self.assertEqual(0, applied.returncode)
        self.assertIn("updated .env:GH_PAT", log)
        sourced = subprocess.run(["bash", "-c", 'source "$1" >/dev/null 2>&1; [[ "$GH_PAT" == "$2" ]]',
                                 "--", str(root), "offline-vault-sentinel-obs-github-token"],
                                env=self.environment, capture_output=True)
        self.assertEqual(0, sourced.returncode, "root secret did not round-trip through Bash")
        self.assertNotRegex(root.read_bytes(), rb"(?m)^GH_PAT=[^\n]*\r\n")
        checked, _ = self.run_sync("--check")
        self.assertEqual(0, checked.returncode)

    def test_root_managed_crlf_is_repaired(self):
        self.root_managed_crlf_control()

    def test_root_sql_crlf_is_derived_and_repaired(self):
        self.materialize()
        root = self.root / ".env"
        original = root.read_bytes()
        crlf, count = re.subn(
            rb"^(" + SQL_KEY.encode() + rb"=[^\r\n]*)\n", rb"\1\r\n",
            original, flags=re.MULTILINE,
        )
        self.assertEqual(1, count, "expected one root SQL assignment")
        root.write_bytes(crlf)

        before = self.snapshot()
        checked, log = self.run_sync("--check")
        self.assertEqual(1, checked.returncode, "check must report drift")
        self.assertNotIn("Error:", log, "drift must not hide a preparation failure")
        self.assertIn("Detected 1 Key Vault synchronization difference(s).", log)
        self.assertIn(f"drift   .env:{SQL_KEY}", log)
        self.assertTrue(before == self.snapshot(), "check changed destinations")

        applied, log = self.run_sync("--apply")
        self.assertEqual(0, applied.returncode, "apply failed to derive root SQL")
        self.assertIn(f"updated .env:{SQL_KEY}", log)
        self.assertTrue(root.read_bytes() == original, "root SQL line was not restored to LF")
        checked, _ = self.run_sync("--check")
        self.assertEqual(0, checked.returncode, "re-check failed after root SQL repair")

    def test_root_sql_crlf_mutant_is_detected(self):
        script = self.root / "scripts/sync-secrets-from-keyvault.sh"
        original = script.read_text()
        # Keep the trailing CR during root SQL derivation, as in M3 (#501).
        mutant = original.replace(
            f'{SQL_KEY} || true)"',
            f'{SQL_KEY} "$([[ $relative_file != .env ]] || echo preserve-cr)" || true)"',
        )
        self.assertTrue(original != mutant, "root SQL CR mutant was not installed")
        script.write_text(mutant)
        with self.assertRaises(AssertionError):
            self.test_root_sql_crlf_is_derived_and_repaired()

    def test_root_crlf_mutant_is_detected(self):
        script = self.root / "scripts/sync-secrets-from-keyvault.sh"
        original = script.read_text()
        mutant = original.replace('if [[ "${3:-}" != preserve-cr ]]; then', 'if true; then')
        self.assertTrue(original != mutant, "CR mutant was not installed")
        script.write_text(mutant)
        with self.assertRaises(AssertionError):
            self.root_managed_crlf_control()

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

    def directory_destination(self, path="deploy/pi-gateways/davis/secrets/central-ingest-api-key"):
        self.write(f"{path}/owner-note", "owner-directory-sentinel\n")

    def linked_directory_destination(self, path="deploy/pi-gateways/eg4/secrets/central-ingest-api-key"):
        self.write("linked-owner-directory/owner-note", "owner-directory-sentinel\n")
        target = self.root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.symlink_to(self.root / "linked-owner-directory", target_is_directory=True)

    def test_rename_guard_rejects_directory_when_preparation_check_is_bypassed(self):
        # Defense in depth for a destination that becomes a directory after preparation.
        script = self.root / "scripts/sync-secrets-from-keyvault.sh"
        script.write_text(script.read_text().replace("\nvalidate_destinations\n", "\n:\n"))
        self.directory_destination()
        result, log = self.run_sync("--apply")
        self.assertNotEqual(0, result.returncode, "directory destination was accepted")
        self.assertIn("partial updates may remain", log)
        self.assertNotIn("Applied ", log)
        destination = self.root / "deploy/pi-gateways/davis/secrets/central-ingest-api-key"
        self.assertTrue(destination.is_dir() and not destination.is_symlink(), "directory destination was replaced")
        self.assertEqual(["owner-note"], sorted(entry.name for entry in destination.iterdir()),
                         "a credential file was placed inside the directory")
        self.assertEqual([], [str(entry) for entry in self.root.rglob("*.tmp.*")], "a temporary credential file remains")

    def test_bootstrap_stdout_is_suppressed(self):
        with (self.root / ".env").open("a") as bootstrap:
            bootstrap.write("printf bootstrap-output-sentinel\n")
        result, _ = self.run_sync("--apply")
        self.assertEqual(0, result.returncode)
        checked, log = self.run_sync("--check")
        self.assertEqual(0, checked.returncode, "bootstrap stdout changed sourced SSH")
        self.assertNotIn("approved manual rotation", log)

    def test_bootstrap_stdout_mutant_is_detected(self):
        script = self.root / "scripts/sync-secrets-from-keyvault.sh"
        original = script.read_text()
        mutant = original.replace('source "$1" >/dev/null 2>&1', 'source "$1"')
        self.assertNotEqual(original, mutant)
        script.write_text(mutant)
        with self.assertRaises(AssertionError):
            self.test_bootstrap_stdout_is_suppressed()

    def test_ssh_trailing_newline_round_trip(self):
        value = SSH + "\n"
        self.bootstrap(ssh=value)
        self.settings["values"]["obs-ssh-private-key"] = value
        self.materialize()
        checked, log = self.run_sync("--check")
        self.assertEqual(0, checked.returncode)
        self.assertNotIn("approved manual rotation", log)

    def test_ssh_trailing_newline_compatibility(self):
        for vault, local in ((SSH + "\n", SSH), (SSH, SSH + "\n")):
            with self.subTest(vault_has_lf=vault.endswith("\n")):
                self.bootstrap(ssh=local)
                self.settings["values"]["obs-ssh-private-key"] = vault
                self.materialize()
                checked, log = self.run_sync("--check")
                self.assertEqual(0, checked.returncode)
                self.assertNotIn("approved manual rotation", log)

    def test_python_filter_ignores_cwd_modules(self):
        self.write("json.py", "raise RuntimeError('cwd-import-sentinel')\n")
        self.materialize()
        result, _ = self.run_sync("--check")
        self.assertEqual(0, result.returncode)

    def test_content_and_read_failures_have_distinct_diagnostics(self):
        self.settings["values"]["obs-github-token"] = "bad\0root"
        result, content_log = self.run_sync("--check")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("contains unsupported content or invalid JSON: obs-github-token", content_log)
        self.settings["values"].pop("obs-github-token")
        self.settings["fail"] = "obs-github-token"
        result, access_log = self.run_sync("--check")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("secret is unavailable: obs-github-token", access_log)
        self.assertNotIn("unsupported content", access_log)

    def test_tsv_mutant_fails_named_probe(self):
        script = self.root / "scripts/sync-secrets-from-keyvault.sh"
        original = script.read_text()
        mutant = original.replace("--output json", "--output tsv")
        self.assertNotEqual(original, mutant)
        script.write_text(mutant)
        with self.assertRaises(AssertionError):
            self.reject("--check", REJECTIONS["root_nul"], "obs-github-token")

    def test_yaml_round_trip(self):
        value = "valid yaml: # punctuation!"
        self.settings["values"][LATE_SECRET] = value
        self.materialize()
        if shutil.which("ruby"):
            parsed = subprocess.run(["ruby", "-ryaml", "-e", 'exit(YAML.safe_load_file(ARGV[0])["hvo_bluetooth_proxy_fallback_password"] == ARGV[1] ? 0 : 1)', str(self.root / YAML), value], capture_output=True)
            self.assertEqual(0, parsed.returncode, "YAML parser did not preserve the credential")
        else:
            import yaml
            self.assertTrue(value == yaml.safe_load((self.root / YAML).read_text())["hvo_bluetooth_proxy_fallback_password"], "YAML did not preserve the credential")
        result, _ = self.run_sync("--check")
        self.assertEqual(0, result.returncode)

    def special_destination(self, kind):
        target = self.root / "deploy/pi-gateways/davis/secrets/central-ingest-api-key"
        target.parent.mkdir(parents=True, exist_ok=True)
        if kind == "fifo":
            os.mkfifo(target)
        else:
            if kind == "regular":
                self.write("owner-target", "owner sentinel\n")
            target.symlink_to(self.root / ("owner-target" if kind == "regular" else "missing-target"))

    def test_rename_race_does_not_move_into_directory(self):
        target = self.root / "deploy/pi-gateways/davis/secrets/central-ingest-api-key"
        self.write("bin/mv", '#!/bin/bash\nif [[ "${@: -1}" == "' + str(target) + '" ]]; then mkdir -p "${@: -1}"; fi\nexec /bin/mv "$@"\n', 0o700)
        result, log = self.run_sync("--apply")
        self.assertNotEqual(0, result.returncode)
        self.assertNotIn("Applied ", log)
        self.assertEqual([], list(target.iterdir()))

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
    "file_trailing_newline": lambda t: t.settings["values"].update({"HomeAssistant--MqttPassword": "badfile\n"}),
    "root_nul": lambda t: t.settings["values"].update({"obs-github-token": "bad\0root"}),
    "root_trailing_newline": lambda t: t.settings["values"].update({"obs-github-token": "badroot\n"}),
    "website_nul": lambda t: t.settings["values"].update({"WebsiteRuntime--AzureClientSecret": "bad\0website"}),
    "website_trailing_newline": lambda t: t.settings["values"].update({"WebsiteRuntime--AzureClientSecret": "badwebsite\n"}),
    "yaml_nul": lambda t: t.settings["values"].update({LATE_SECRET: "bad\0yaml"}),
    "yaml_trailing_newline": lambda t: t.settings["values"].update({LATE_SECRET: "badyaml\n"}),
    "compose_single_quote": lambda t: t.settings["values"].update({"WebsiteRuntime--AzureClientSecret": "bad'quote"}),
    "compose_backslash": lambda t: t.settings["values"].update({"WebsiteRuntime--AzureClientSecret": r"bad\path"}),
    "compose_tab": lambda t: t.settings["values"].update({"WebsiteRuntime--AzureClientSecret": "bad\tvalue"}),
    "compose_leading_space": lambda t: t.settings["values"].update({"WebsiteRuntime--AzureClientSecret": " bad"}),
    "compose_trailing_space": lambda t: t.settings["values"].update({"WebsiteRuntime--AzureClientSecret": "bad "}),
    "compose_unicode_utf8": lambda t: (t.environment.update(LC_ALL="C.UTF-8"), t.settings["values"].update({"WebsiteRuntime--AzureClientSecret": "café-secret"})),
    "yaml_unicode_utf8": lambda t: (t.environment.update(LC_ALL="C.UTF-8"), t.settings["values"].update({LATE_SECRET: "café-secret"})),
    "yaml_backslash_q": lambda t: t.settings["values"].update({LATE_SECRET: r"pa\qss"}),
    "yaml_backslash_n": lambda t: t.settings["values"].update({LATE_SECRET: r"pa\nss"}),
    "yaml_control": lambda t: t.settings["values"].update({LATE_SECRET: "pa\tss"}),
    "compose_dollar": lambda t: t.settings["values"].update({"WebsiteRuntime--AzureClientSecret": "$UNSET_PROBE"}),
    "compose_quote": lambda t: t.settings["values"].update({"WebsiteRuntime--AzureClientSecret": '"unmatched'}),
    "compose_comment": lambda t: t.settings["values"].update({"WebsiteRuntime--AzureClientSecret": "value # comment"}),
    "dangling_link": lambda t: t.special_destination("dangling"),
    "regular_link": lambda t: t.special_destination("regular"),
    "fifo": lambda t: t.special_destination("fifo"),
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
    "secret_file_directory": lambda t: t.directory_destination(),
    "secret_file_linked_directory": lambda t: t.linked_directory_destination(),
}


EXPECTED_REJECTION_ERRORS = {
    "sql_password_single_quote": "The SQL password cannot contain a single quote.",
    "secret_file_directory": "A managed destination exists but is not a regular file: deploy/pi-gateways/davis/secrets/central-ingest-api-key",
    "secret_file_linked_directory": "A managed destination exists but is not a regular file: deploy/pi-gateways/eg4/secrets/central-ingest-api-key",
}


PROBED_SECRETS = {
    "root_nul": "obs-github-token", "root_trailing_newline": "obs-github-token",
    "website_nul": "WebsiteRuntime--AzureClientSecret",
    "website_trailing_newline": "WebsiteRuntime--AzureClientSecret",
    "yaml_nul": LATE_SECRET, "yaml_trailing_newline": LATE_SECRET,
    "file_trailing_newline": "HomeAssistant--MqttPassword",
}


def rejection_test(mode, configure, expected_error=None, expected_secret=None):
    def test(self):
        log = self.reject(mode, configure, expected_secret)
        if expected_error:
            self.assertIn(expected_error, log)
    return test


for case, configure in REJECTIONS.items():
    for mode in ("--check", "--apply"):
        expected_error = EXPECTED_REJECTION_ERRORS.get(case)
        setattr(MaterializationTests, f"test_reject_{case}_{mode[2:]}", rejection_test(mode, configure, expected_error, PROBED_SECRETS.get(case)))

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
