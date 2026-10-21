"""Regression coverage for the local admin password bootstrap and rotation."""
import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import sys

SCRIPT = Path(__file__).resolve().parents[2] / 'scripts' / 'init_env.py'
spec = importlib.util.spec_from_file_location('worldalerts_init_env', SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class AdminPasswordBootstrapTests(unittest.TestCase):
    def run_password(self, target, first, confirm, reset=False):
        args = ['init_env.py', str(target)] + (['--reset'] if reset else [])
        with patch.object(sys, 'argv', args), patch.object(sys.stdin, 'isatty', return_value=True), \
             patch.object(module.getpass, 'getpass', side_effect=[first, confirm]):
            module.main()

    def test_create_and_rotate_preserves_existing_channel_secrets(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / '.env'
            self.run_password(path, 'first_very_long_password', 'first_very_long_password')
            before = path.read_text()
            self.assertIn(module.KEY, before)
            path.write_text(before + 'WorldAlerts__Delivery__Smtp__Username=me@example.test\n')
            self.run_password(path, 'second_very_long_password', 'second_very_long_password', True)
            after = path.read_text()
            self.assertIn('WorldAlerts__Delivery__Smtp__Username=me@example.test', after)
            self.assertEqual(after.count(module.KEY), 1)
            self.assertNotEqual(before.split(module.KEY)[1].splitlines()[0], after.split(module.KEY)[1].splitlines()[0])
            self.assertEqual(path.stat().st_mode & 0o777, 0o600)

    def test_invalid_password_does_not_replace_file(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / '.env'
            path.write_text('KEEP=untouched\n')
            with self.assertRaises(SystemExit):
                self.run_password(path, 'long_password_one', 'long_password_two', True)
            self.assertEqual(path.read_text(), 'KEEP=untouched\n')

    def test_init_refuses_to_override_existing_config(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / '.env'
            path.write_text('KEEP=untouched\n')
            with self.assertRaises(SystemExit):
                self.run_password(path, 'first_very_long_password', 'first_very_long_password')
            self.assertEqual(path.read_text(), 'KEEP=untouched\n')


if __name__ == '__main__':
    unittest.main()
