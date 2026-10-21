"""Isolated launch-contract tests; run with python3 -m unittest discover -s src/tests -p 'Startup*.py'."""

import os
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]


class StartupScripts(unittest.TestCase):
    def setUp(self):
        self.scratch = tempfile.TemporaryDirectory(prefix="startup-", dir="/tmp/opencode")
        self.addCleanup(self.scratch.cleanup)
        directory = Path(self.scratch.name)
        bin_dir = directory / "bin"
        bin_dir.mkdir()
        docker = bin_dir / "docker"
        docker.write_text("#!/bin/sh\nexec python3 \"$FAKE_DOCKER_SCRIPT\" \"$@\"\n")
        docker.chmod(0o755)
        self.log = directory / "docker.log"
        self.state = directory / "state.json"
        self.config = directory / "secrets.env"
        self.config.write_text("WorldAlerts__Delivery__Slack__WebhookUrl=\n")
        self.env = os.environ.copy()
        self.env.update({
            "PATH": str(bin_dir) + os.pathsep + os.environ["PATH"],
            "FAKE_DOCKER_LOG": str(self.log),
            "FAKE_DOCKER_STATE": str(self.state),
            "FAKE_DOCKER_SCRIPT": str(ROOT / "src/tests/StartupFixtures/EngineFake.py"),
            "WORLDALERTS_ENV_FILE": str(self.config),
            "WORLDALERTS_INSTANCE": "task26-test-instance",
            "WORLDALERTS_PORT": "19091",
        })

    def launch(self, launcher, env=None):
        command = ["bash", str(ROOT / "run.sh")] if launcher == "linux" else [
            "pwsh", "-NoProfile", "-File", str(ROOT / "run.ps1")
        ]
        return subprocess.run(command, cwd=self.scratch.name, env=env or self.env,
                              text=True, capture_output=True)

    def calls(self):
        return [json.loads(line) for line in self.log.read_text().splitlines()] if self.log.exists() else []

    def test_failed_build_or_test_never_starts_service(self):
        for launcher in self.launchers():
            with self.subTest(launcher=launcher):
                self.log.unlink(missing_ok=True)
                self.state.unlink(missing_ok=True)
                env = dict(self.env, FAKE_DOCKER_FAIL_BUILD="37")
                result = self.launch(launcher, env)
                self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
                self.assertEqual(len(self.calls()), 1)
                self.assertEqual(self.calls()[0][0], "build")

    def test_repeat_invocation_keeps_one_named_container_and_volume(self):
        for launcher in self.launchers():
            with self.subTest(launcher=launcher):
                self.log.unlink(missing_ok=True)
                self.state.unlink(missing_ok=True)
                for _ in range(2):
                    result = self.launch(launcher)
                    self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                calls = self.calls()
                self.assertEqual([args[0] for args in calls].count("build"), 2)
                self.assertEqual([args[0] for args in calls].count("run"), 2)
                self.assertEqual([args[:2] for args in calls].count(["container", "stop"]), 1)
                self.assertEqual([args[:2] for args in calls].count(["container", "rm"]), 1)
                self.assertIn("--no-cache", calls[0])
                for args in (args for args in calls if args[0] == "run"):
                    self.assertIn("127.0.0.1:19091:8080", args)
                    self.assertIn("task26-test-instance-data:/app/data", args)
                    self.assertIn("--env-file", args)
                state = json.loads(self.state.read_text())
                self.assertEqual(list(state["containers"]), ["task26-test-instance"])
                self.assertEqual(list(state["volumes"]), ["task26-test-instance-data", "task26-test-instance-keys"])

    def test_failed_rebuild_preserves_existing_container(self):
        for launcher in self.launchers():
            with self.subTest(launcher=launcher):
                self.log.unlink(missing_ok=True)
                self.state.unlink(missing_ok=True)
                self.assertEqual(self.launch(launcher).returncode, 0)
                self.log.unlink()
                result = self.launch(launcher, dict(self.env, FAKE_DOCKER_FAIL_BUILD="39"))
                self.assertNotEqual(result.returncode, 0)
                self.assertEqual([args[0] for args in self.calls()], ["build"])
                state = json.loads(self.state.read_text())
                self.assertTrue(state["containers"]["task26-test-instance"]["running"])

    def test_existing_unowned_container_is_not_stopped_or_replaced(self):
        for launcher in self.launchers():
            with self.subTest(launcher=launcher):
                self.log.unlink(missing_ok=True)
                self.state.write_text(json.dumps({"containers": {"task26-test-instance": {"label": "someone-else", "running": True}}, "volumes": {}}))
                result = self.launch(launcher)
                self.assertNotEqual(result.returncode, 0)
                self.assertNotIn(["container", "stop", "task26-test-instance"], self.calls())
                self.assertTrue(json.loads(self.state.read_text())["containers"]["task26-test-instance"]["running"])

    def test_existing_unowned_volume_is_never_mounted(self):
        for launcher in self.launchers():
            with self.subTest(launcher=launcher):
                self.log.unlink(missing_ok=True)
                self.state.write_text(json.dumps({"containers": {}, "volumes": {"task26-test-instance-data": "someone-else"}}))
                result = self.launch(launcher)
                self.assertNotEqual(result.returncode, 0)
                self.assertFalse(any(args[0] == "run" for args in self.calls()))

    def test_data_protection_keys_survive_container_replacement(self):
        for launcher in self.launchers():
            with self.subTest(launcher=launcher):
                self.log.unlink(missing_ok=True)
                self.state.unlink(missing_ok=True)
                for _ in range(2):
                    result = self.launch(launcher)
                    self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                runs = [args for args in self.calls() if args[0] == "run"]
                self.assertEqual(len(runs), 2)
                for args in runs:
                    self.assertIn("task26-test-instance-keys:/home/app/.aspnet/DataProtection-Keys", args)
                volumes = json.loads(self.state.read_text())["volumes"]
                self.assertEqual(volumes["task26-test-instance-keys"], "task26-test-instance")

    def test_invalid_instance_or_port_never_calls_engine(self):
        for launcher in self.launchers():
            with self.subTest(launcher=launcher):
                for instance, port in (("../../other", "0"), ("task26-test-instance", "18446744073709551617")):
                    self.log.unlink(missing_ok=True)
                    self.state.unlink(missing_ok=True)
                    result = self.launch(launcher, dict(self.env, WORLDALERTS_INSTANCE=instance, WORLDALERTS_PORT=port))
                    self.assertNotEqual(result.returncode, 0)
                    self.assertEqual(self.calls(), [])

    def test_missing_private_configuration_never_calls_engine(self):
        for launcher in self.launchers():
            with self.subTest(launcher=launcher):
                self.log.unlink(missing_ok=True)
                self.state.unlink(missing_ok=True)
                env = dict(self.env, WORLDALERTS_ENV_FILE=str(self.config) + "-missing")
                self.assertNotEqual(self.launch(launcher, env).returncode, 0)
                self.assertEqual(self.calls(), [])

    @staticmethod
    def launchers():
        return ("linux", "windows") if shutil.which("pwsh") else ("linux",)


if __name__ == "__main__":
    unittest.main()
