"""Safety-focused, fast local-demo launcher regressions (no Docker required)."""
import base64
import hashlib
from pathlib import Path
import re
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]


class DemoScripts(unittest.TestCase):
    def test_same_baked_pbkdf2_admin_hash_both_launchers(self):
        linux = (ROOT / 'run.sh').read_text()
        win = (ROOT / 'run.ps1').read_text()
        a = re.search(r'local_demo_hash="([^"]+)"', linux).group(1)
        b = re.search(r"\$demoHash = '([^']+)'", win).group(1)
        self.assertEqual(a, b)
        cycles, salt, key = a.split(':')
        self.assertGreaterEqual(int(cycles), 100_000)
        self.assertEqual(hashlib.pbkdf2_hmac('sha256', b'admin', base64.b64decode(salt), int(cycles)), base64.b64decode(key))
        for text in (linux, win):
            self.assertIn('WorldAlerts__Admin__PasswordHash=', text)
            self.assertNotIn('Python-Password', text)
            self.assertNotIn('admin-password', text)
        self.assertIn('127.0.0.1:$port:8080', linux)
        self.assertIn('127.0.0.1:${port}:8080', win)

    def test_linux_pid_cleanup_requires_project_and_angular(self):
        import sys
        sys.path.insert(0, str(ROOT / 'scripts'))
        from stop_dev_server import is_project_dev_server
        with tempfile.TemporaryDirectory() as td:
            project = Path(td) / 'sonrisa_homework'
            (project / 'src/web').mkdir(parents=True)
            proc = Path(td) / 'proc' / '777'
            proc.mkdir(parents=True)
            (proc / 'cwd').symlink_to(project / 'src/web')
            (proc / 'cmdline').write_bytes(b'node\x00/prod/sonrisa_homework/src/web/node_modules/@angular/cli/bin/ng.js\x00serve')
            self.assertTrue(is_project_dev_server(777, project, proc.parent))
            (proc / 'cmdline').write_bytes(b'node\x00unrelated-server.js')
            self.assertFalse(is_project_dev_server(777, project, proc.parent))
            (proc / 'cwd').unlink()
            (proc / 'cwd').symlink_to(td)
            (proc / 'cmdline').write_bytes(b'node\x00ng.js\x00serve')
            self.assertFalse(is_project_dev_server(777, project, proc.parent))

    def test_docs_reference_new_flow(self):
        doc = (ROOT / 'docs/startup.md').read_text()
        self.assertIn('admin` (jelszó: `admin`)', doc)
        self.assertIn('clean-dev', doc)
        self.assertNotIn('./run.sh init', doc)


if __name__ == '__main__':
    unittest.main()
