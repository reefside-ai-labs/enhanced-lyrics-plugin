import importlib.util
import tempfile
import unittest
from pathlib import Path
spec = importlib.util.spec_from_file_location('installer', Path(__file__).resolve().parents[1] / 'scripts/integrate-web.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

class InstallerTests(unittest.TestCase):
    def test_install_is_idempotent_and_remove_restores_original(self):
        with tempfile.TemporaryDirectory() as root:
            index = Path(root) / 'index.html'
            original = '<html><head><script defer src="runtime.js"></script></head></html>'
            index.write_text(original)
            module.update(index, False)
            self.assertLess(index.read_text().index('enhanced-lyrics:start'), index.read_text().index('runtime.js'))
            module.update(index, False)
            self.assertEqual(index.read_text().count('enhanced-lyrics:start'), 1)
            module.update(index, True)
            self.assertEqual(index.read_text(), original)
            self.assertEqual(index.with_name('index.html.enhanced-lyrics-backup').read_text(), original)

    def test_unknown_web_build_is_not_modified(self):
        with tempfile.TemporaryDirectory() as root:
            index = Path(root) / 'index.html'
            index.write_text('<html>Unknown</html>')
            with self.assertRaises(ValueError):
                module.update(index, False)
            self.assertEqual(index.read_text(), '<html>Unknown</html>')
