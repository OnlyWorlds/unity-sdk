#!/usr/bin/env python3
"""Watch tools/verify_promise.py fail, once for each thing it holds, and pass on its controls.

Each plant runs the check on a copy of what it reads (the package's Runtime and Editor source, package.json,
CHANGELOG.md and tools/public-names.json), never on the real files, with one change made to the copy. The reader is
built once (dotnet build tools/PublicNames) and handed to every run.

Usage: python tools/mutants_promise.py
Exit 0 when every plant failed as named and every control passed, 1 otherwise, 2 when the reader does not build.
"""

from __future__ import annotations

import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
PACKAGE = Path("Packages") / "com.onlyworlds.sdk"
CHECK = HERE / "verify_promise.py"
MARKED = "OnlyWorlds.Sdk.OWClient.AskAsync"
CLIENT = PACKAGE / "Runtime" / "Core" / "OWClient.cs"
ASK_MARK = "Since 0.37.0. Leaves in 1.0. Use AiAsync(new OWAiAskBody"


def build_reader() -> str:
    project = HERE / "PublicNames"
    r = subprocess.run(["dotnet", "build", str(project), "-c", "Release", "-nologo", "-v", "q"], capture_output=True, text=True)
    dll = project / "bin" / "Release" / "net8.0" / "PublicNames.dll"
    if r.returncode != 0 or not dll.exists():
        print(f"mutants-promise: the reader did not build:\n{r.stdout[-1500:]}")
        sys.exit(2)
    return str(dll)


def copy() -> Path:
    d = Path(tempfile.mkdtemp(prefix="ow-promise-"))
    for sub in ("Runtime", "Editor"):
        shutil.copytree(ROOT / PACKAGE / sub, d / PACKAGE / sub, ignore=shutil.ignore_patterns("*.meta"))
    for f in ("package.json", "CHANGELOG.md"):
        shutil.copy2(ROOT / PACKAGE / f, d / PACKAGE / f)
    (d / "tools").mkdir()
    shutil.copy2(HERE / "public-names.json", d / "tools" / "public-names.json")
    return d


def edit(d: Path, rel: Path | str, fn) -> None:
    p = d / rel
    p.write_text(fn(p.read_text(encoding="utf-8")), encoding="utf-8", newline="")


def once(text: str, old: str, new: str) -> str:
    if text.count(old) < 1:
        raise RuntimeError(f"plant anchor not found: {old}")
    return text.replace(old, new, 1)


def names(d: Path, fn) -> None:
    edit(d, "tools/public-names.json", lambda t: json.dumps(fn(json.loads(t)), indent=1) + "\n")


def ghost(since: str | None):
    def put(f):
        f["names"]["OnlyWorlds.Sdk.GhostName"] = {"deprecated": {"since": since, "leaves": "1.0"}} if since else {}
        return f
    return put


def major1(d: Path, date: str) -> None:
    """The copy made a 1.0.0, dated `date`: its marks then name 2.0, as a real major's would, or rule 1 calls them stale."""
    edit(d, PACKAGE / "package.json", lambda t: re.sub(r'"version": "[^"]+"', '"version": "1.0.0"', t, count=1))
    edit(d, PACKAGE / "CHANGELOG.md", lambda t: once(t, "## [Unreleased]", f"## [Unreleased]\n\n## [1.0.0] - {date}"))
    for cs in (d / PACKAGE).rglob("*.cs"):
        text = cs.read_text(encoding="utf-8")
        if "Leaves in 1.0" in text:
            cs.write_text(text.replace("Leaves in 1.0", "Leaves in 2.0"), encoding="utf-8", newline="")


def unlist(f):
    del f["names"]["OnlyWorlds.Sdk.OWClient"]
    return f


def unrecord(f):
    f["names"][MARKED] = {}
    return f


def moved_member(f):
    f["names"]["OnlyWorlds.Sdk.OWMintedKey.Id"] = {}  # declared on OWWorldKeyRow, its base: still reachable
    return f


PLANTS = [
    ("a listed name gone within the major", lambda d: names(d, ghost(None)), 1, r"\(2\) OnlyWorlds\.Sdk\.GhostName: listed at"),
    ("a public name the file does not list", lambda d: names(d, unlist), 1, r"\(2\) OnlyWorlds\.Sdk\.OWClient: public in .* not in public-names\.json"),
    ("a mark the file does not record", lambda d: names(d, unrecord), 1, r"\(2\) OnlyWorlds\.Sdk\.OWClient\.AskAsync: the source marks it"),
    ("a mark without its since", lambda d: edit(d, CLIENT, lambda t: once(t, ASK_MARK, "Leaves in 1.0. Use AiAsync(new OWAiAskBody")), 1,
     r'\(1\) OnlyWorlds\.Sdk\.OWClient\.AskAsync: its message says no "Since X\.Y\.Z"'),
    ("a mark that leaves in a minor", lambda d: edit(d, CLIENT, lambda t: once(t, ASK_MARK, "Since 0.37.0. Leaves in 0.60. Use AiAsync(new OWAiAskBody")), 1,
     r"\(1\) OnlyWorlds\.Sdk\.OWClient\.AskAsync: it leaves in 0\.60, not in a major above 0"),
    ("a mark since a release after the next one", lambda d: edit(d, CLIENT, lambda t: once(t, ASK_MARK, "Since 9.9.9. Leaves in 1.0. Use AiAsync(new OWAiAskBody")), 1,
     r"\(1\) OnlyWorlds\.Sdk\.OWClient\.AskAsync: marked since 9\.9\.9, after this release"),
    ("a major that drops an unmarked name", lambda d: (major1(d, "2027-12-01"), names(d, ghost(None))), 1,
     r"\(3\) OnlyWorlds\.Sdk\.GhostName: gone in major 1 without a mark first"),
    ("a major that drops a name under six months after its mark", lambda d: (major1(d, "2027-05-09"), names(d, ghost("0.37.0"))), 1,
     r"\(3\) OnlyWorlds\.Sdk\.GhostName: marked since 0\.37\.0 \(2026-11-10\), gone 2027-05-09, before 2027-05-10"),
    ("control: a major that drops a name six months after its mark", lambda d: (major1(d, "2027-05-10"), names(d, ghost("0.37.0"))), 0, r"^PASS"),
    ("control: a listed member moved into a base class", lambda d: names(d, moved_member), 0, r"^PASS"),
    ("control: the copy unchanged", lambda d: None, 0, r"^PASS"),
]


def main() -> int:
    reader = build_reader()
    bad = 0
    for what, plant, want, line in PLANTS:
        d = copy()
        try:
            plant(d)
            env = {**os.environ, "OW_PROMISE_ROOT": str(d), "OW_PROMISE_NAMES": str(d / "tools" / "public-names.json"), "OW_PROMISE_READER": reader}
            r = subprocess.run([sys.executable, str(CHECK)], env=env, capture_output=True, text=True, timeout=600)
            out = r.stdout + r.stderr
            ok = r.returncode == want and re.search(line, out, re.MULTILINE) is not None
            bad += not ok
            print(f"{'as named' if ok else 'NOT AS NAMED'}: {what} (exit {r.returncode}, wanted {want})" + ("" if ok else f"\n{out[:1500]}"))
        finally:
            shutil.rmtree(d, ignore_errors=True)
    plants = sum(1 for p in PLANTS if not p[0].startswith("control"))
    controls = len(PLANTS) - plants
    print(f"mutants-promise: FAIL: {bad} of {len(PLANTS)} not as named" if bad
          else f"mutants-promise: PASS: {plants} plants each failed as named; {controls} controls passed")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
