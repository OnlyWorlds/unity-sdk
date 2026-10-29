#!/usr/bin/env python3
"""generate_tools.py --check: green on the tree as committed, red on a one-byte change to the vendored list or the C#.

Each case copies the generator, the pin, the vendored list and the generated C# into a temporary tree of the same
shape and runs --check there, so the repo's own files are never touched.

    python codegen/test_generate_tools.py
"""

from __future__ import annotations

import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
FILES = (
    "codegen/generate_tools.py",
    "codegen/tools-pin.json",
    "codegen/tools/tools.json",
    "Packages/com.onlyworlds.sdk/Runtime/Core/OWTools.cs",
)


class CheckTest(unittest.TestCase):
    def setUp(self) -> None:
        self._dir = tempfile.TemporaryDirectory(prefix="l773-tools-")
        self.tree = Path(self._dir.name)
        for rel in FILES:
            (self.tree / rel).parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(ROOT / rel, self.tree / rel)

    def tearDown(self) -> None:
        self._dir.cleanup()

    def check(self) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            [sys.executable, str(self.tree / "codegen" / "generate_tools.py"), "--check"],
            capture_output=True, text=True, cwd=str(self.tree),
        )

    def flip(self, rel: str, needle: bytes, to: bytes) -> None:
        path = self.tree / rel
        data = path.read_bytes()
        self.assertIn(needle, data)
        path.write_bytes(data.replace(needle, to, 1))

    def test_the_committed_tree_is_green(self) -> None:
        run = self.check()
        self.assertEqual(run.returncode, 0, run.stderr)

    def test_one_byte_in_the_vendored_list_is_red(self) -> None:
        self.flip("codegen/tools/tools.json", b'"Atlas"', b'"Atlaz"')
        run = self.check()
        self.assertEqual(run.returncode, 1)
        self.assertIn("the pin says", run.stderr)

    def test_one_byte_in_the_generated_table_is_red(self) -> None:
        self.flip(FILES[3], b'"Atlas"', b'"Atlaz"')
        run = self.check()
        self.assertEqual(run.returncode, 1)
        self.assertIn("STALE", run.stderr)


if __name__ == "__main__":
    unittest.main()
