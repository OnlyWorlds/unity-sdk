#!/usr/bin/env python3
"""Drift guard: the committed Generated/ tree vs a fresh generation.

    python codegen/check_drift.py      # exit 0 clean, 1 drifted, 2 unreadable schema

Two checks, and the second is the one that catches the interesting failure:

  1. HASH the vendored schema distribution against its own MANIFEST.json. A
     generator whose INPUT was hand-edited produces perfectly self-consistent
     output that agrees with nothing else in the ecosystem. Note this proves the
     vendored tree is internally consistent only -- the pin against a MOVED tag
     is OWSchemaPin.ManifestSha256's job, checked in the engine by
     OWSchemaPinTests, because a moved tag brings its own agreeing manifest.

  2. REGENERATE into memory and diff against what is committed. This is what
     fails when someone edits a file under Generated/ by hand: the edit survives
     until the next regeneration and then vanishes without a merge conflict, so
     a guard that never runs is a guard that has never fired.

Wire it into CI. npm's own codegen:check existed for months as a script CI never
ran, which is the same as not having it -- verified directly in that repo's
workflow file on 2026-07-28.
"""

from __future__ import annotations

import hashlib
import json
import subprocess
import sys
from pathlib import Path

CODEGEN = Path(__file__).resolve().parent
DIST_DIR = CODEGEN / "schema-dist"

# Files in MANIFEST.json that this SDK deliberately does not vendor here.
# presentation.json lives at Runtime/Resources/ow-presentation.json instead --
# it is a shipped runtime asset, not a codegen input, and OWSchemaPinTests
# already hashes it there. .gitattributes is repo plumbing of the dist itself.
NOT_VENDORED = {".gitattributes", "presentation.json"}


def verify_manifest() -> int:
    manifest_path = DIST_DIR / "MANIFEST.json"
    if not manifest_path.exists():
        print(f"FAIL: no MANIFEST.json at {manifest_path}")
        return 1

    files = json.loads(manifest_path.read_text(encoding="utf-8"))["files"]
    problems = 0
    checked = 0

    for rel, expected in files.items():
        if rel in NOT_VENDORED:
            continue
        path = DIST_DIR / rel
        if not path.exists():
            print(f"FAIL: manifest lists {rel}, which is not vendored")
            problems += 1
            continue
        actual = hashlib.sha256(path.read_bytes()).hexdigest()
        if actual != expected:
            print(f"FAIL: {rel} does not match its manifest entry")
            print(f"      expected {expected}")
            print(f"      actual   {actual}")
            print("      Re-vendor from the pinned tag. Do NOT edit the expected hash.")
            problems += 1
        else:
            checked += 1

    # An UNTRACKED file inside a hash-verified tree is a finding, not noise: it
    # is how a local patch to the walk hides (and how a stray __pycache__ lands).
    #
    # MANIFEST.json is exempt because it does not hash ITSELF -- the dist's README
    # says so in as many words ("the tree holds one more file than the manifest
    # has entries; do not diff the manifest against a directory listing"). The
    # first run of this guard flagged it, which is the README being right.
    for path in DIST_DIR.rglob("*"):
        if not path.is_file():
            continue
        rel = path.relative_to(DIST_DIR).as_posix()
        if rel == "MANIFEST.json":
            continue
        if rel not in files:
            print(f"FAIL: untracked file inside the verified dist: {rel}")
            problems += 1

    if problems == 0:
        version = (DIST_DIR / "VERSION").read_text(encoding="utf-8").strip().replace("\n", ", ")
        print(f"dist OK: {checked} files match MANIFEST.json  ({version})")
    return problems


def verify_test_fixtures() -> int:
    """Each profile fixture the tests hold a copy of is the pinned dist's own bytes.

    A test pins its copy's sha256, so a repin that re-vendors codegen/schema-dist but not the copy leaves the test
    green on an old fixture: the lens fixture's copy sat at dist.88 through the repins to 89, 90 and 91.
    """
    fixtures = CODEGEN.parent / "Packages" / "com.onlyworlds.sdk" / "Tests" / "Editor" / "Fixtures"
    problems = checked = 0
    for copy in sorted(fixtures.rglob("*.fixture.json")):
        source = DIST_DIR / "profiles" / copy.name
        if not source.exists():
            continue  # a fixture of a package's own (the openers'), not the dist's
        checked += 1
        if copy.read_bytes() != source.read_bytes():
            print(f"FAIL: {copy.relative_to(CODEGEN.parent).as_posix()} is not the pinned dist's profiles/{copy.name}")
            print("      Copy the dist's bytes over it and move the test's sha256 with it.")
            problems += 1
    if problems == 0:
        print(f"test fixtures OK: {checked} copies are the pinned dist's bytes")
    return problems


def main() -> int:
    if verify_manifest():
        print("\nThe generator's INPUT is not what it claims to be. Not regenerating.")
        return 1
    if verify_test_fixtures():
        return 1

    print()
    root = CODEGEN.parent
    unicode_dir = DIST_DIR / "fixtures" / "owworld"
    failed = 0
    # The models, then the two Unicode tables OW-18's fold is built from (OWCaseFold, OWNfcData): each generated from
    # the same pinned dist, so a repin that moves one of their files shows here, not first in a reader.
    for args in (
        [str(CODEGEN / "generate_models.py"), "--check"],
        [str(root / "tools" / "gen_casefold.py"), "--file", str(unicode_dir / "CaseFolding-15.1.0.txt"), "--check"],
        [str(root / "tools" / "gen_nfc.py"), "--dir", str(unicode_dir), "--check"],
        # The tools list OWOpeners reads: pinned on its own (codegen/tools-pin.json), not by the dist.
        [str(CODEGEN / "generate_tools.py"), "--check"],
    ):
        failed |= subprocess.run([sys.executable] + args, cwd=str(root)).returncode
    return failed


if __name__ == "__main__":
    raise SystemExit(main())
