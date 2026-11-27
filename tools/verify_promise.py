#!/usr/bin/env python3
"""Hold the package to its written promise (the docs' Stability page, "The standard, the packages and this site").

A patch release fixes and a minor release adds; neither removes nor renames a public name. A name that will go is
marked first, as `[Obsolete("Since X.Y.Z. Leaves in 1.0. Use Y.")]`; the mark says since which release and the
release it leaves in; the name stays at least six months after that release and goes only in a major (1.0 for this
0.x package). Three rules, against the names file `tools/public-names.json`:

1. Marks. Every `[Obsolete]` on a public name has a message that says `Since X.Y.Z` (a release at or before this one,
   or the next one while the CHANGELOG has an Unreleased section) and `Leaves in N.0`, N a major above this one.
2. Names. Within the major the file was written at, every name it lists is still public (declared, or reachable
   through an in-tree base class), every public name is listed, and the file records each name's mark as the source
   has it. A release that adds names or marks runs `--write`.
3. Six months. When the major has moved, a listed name gone from the source was marked in the file, and the release
   its mark names is dated (the CHANGELOG's `## [x.y.z] - YYYY-MM-DD`) at least six months before this release.

A public name: a type declared public, its public members, the protected members of a public class that is neither
sealed nor static, interface and enum members (read from the Runtime and Editor source by tools/PublicNames, Roslyn's
parser, with no Unity install). Overloads share a name, and a name counts as marked when every declaration of it is.
Not checked: whether a mark's words are true, a call's parameters, a change of behaviour under an unchanged name.
Watched failing: tools/mutants_promise.py.

Usage:
  python tools/verify_promise.py           check the source against the names file
  python tools/verify_promise.py --write   add new names and update the marks; a name leaves the file only when the
                                           major moved and rule 3 lets it go
Needs the .NET SDK (8 or later) on the PATH: the reader is built with `dotnet build` the first time.
Exit 0 PASS, 1 FAIL (one line per break), 2 BLIND (the reader, the version, the CHANGELOG or the names file could not
be read): never a pass.
"""

from __future__ import annotations

import argparse
import calendar
import datetime
import json
import os
import re
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = Path(os.environ.get("OW_PROMISE_ROOT") or HERE.parent)
NAMES_FILE = Path(os.environ.get("OW_PROMISE_NAMES") or ROOT / "tools" / "public-names.json")
PACKAGE = ROOT / "Packages" / "com.onlyworlds.sdk"
CHANGELOG = PACKAGE / "CHANGELOG.md"
SINCE = re.compile(r"\bsince (\d+\.\d+\.\d+)\b", re.IGNORECASE)
LEAVES = re.compile(r"\bleaves? in (\d+)\.(\d+)\b", re.IGNORECASE)
HEADING = re.compile(r"^## \[(\d+\.\d+\.\d+)\]\s*[-—]\s*(\d{4}-\d{2}-\d{2})", re.MULTILINE)
UNRELEASED = re.compile(r"^## \[Unreleased\]", re.MULTILINE | re.IGNORECASE)
ABOUT = (
    "Every public name of the OnlyWorlds Unity SDK and its deprecation: written by tools/verify_promise.py --write, "
    "read by it in CI. A name leaves this file only in a major."
)
MARK_WORDS = '[Obsolete("Since X.Y.Z. Leaves in 1.0. Use ...")]'


OPTIONS = argparse.Namespace()


def engine_options(parser: argparse.ArgumentParser) -> None:
    """This engine's own options: none."""


class Blind(Exception):
    """Something the check needs could not be read: exit 2, never a pass."""


# ---- this engine: the version and the public names -------------------------------------------------------------


def read_version() -> str:
    try:
        version = json.loads((PACKAGE / "package.json").read_text(encoding="utf-8"))["version"]
    except (OSError, ValueError, KeyError) as exc:
        raise Blind(f"package.json: no version ({exc})") from exc
    if not re.fullmatch(r"\d+\.\d+\.\d+", str(version)):
        raise Blind(f"package.json: version {version!r} is not X.Y.Z")
    return version


def reader_dll() -> list[str]:
    """The command that runs tools/PublicNames: OW_PROMISE_READER (a built dll), else a build of it here."""
    given = os.environ.get("OW_PROMISE_READER")
    if given:
        return ["dotnet", given]
    project = HERE / "PublicNames"
    try:
        built = subprocess.run(
            ["dotnet", "build", str(project), "-c", "Release", "-nologo", "-v", "q"],
            capture_output=True, text=True, timeout=600,
        )
    except (OSError, subprocess.TimeoutExpired) as exc:
        raise Blind(f"dotnet build of tools/PublicNames did not run: {exc}") from exc
    if built.returncode != 0:
        raise Blind(f"dotnet build of tools/PublicNames failed:\n{built.stdout[-1500:]}{built.stderr[-500:]}")
    dll = project / "bin" / "Release" / "net8.0" / "PublicNames.dll"
    if not dll.exists():
        raise Blind(f"{dll} is not there after the build")
    return ["dotnet", str(dll)]


def read_names() -> tuple[list[tuple[str, str | None]], set[str]]:
    """Every declaration of a public name with its mark (None when unmarked), and the names reachable through a base."""
    folders = [PACKAGE / "Runtime", PACKAGE / "Editor"]
    for folder in folders:
        if not folder.is_dir():
            raise Blind(f"{folder}: not a folder")
    try:
        run = subprocess.run(
            reader_dll() + [str(f) for f in folders], capture_output=True, timeout=600,
        )
    except (OSError, subprocess.TimeoutExpired) as exc:
        raise Blind(f"tools/PublicNames did not run: {exc}") from exc
    if run.returncode != 0:
        raise Blind(f"tools/PublicNames: {run.stderr.decode('utf-8', 'replace').strip()[:500]}")
    try:
        data = json.loads(run.stdout.decode("utf-8"))
    except ValueError as exc:
        raise Blind(f"tools/PublicNames printed no JSON: {exc}") from exc
    if not data.get("decls"):
        raise Blind("tools/PublicNames read no public name")
    return [(d[0], d[1]) for d in data["decls"]], set(data["reach"])


# ---- the rules (the same in the three engine packages) -----------------------------------------------------------


def parse_version(v: str) -> tuple[int, int, int]:
    a, b, c = (int(x) for x in v.split("."))
    return a, b, c


def six_months_after(day: datetime.date) -> datetime.date:
    month = day.month + 6
    year, month = day.year + (month - 1) // 12, (month - 1) % 12 + 1
    return datetime.date(year, month, min(day.day, calendar.monthrange(year, month)[1]))


def changelog() -> tuple[dict[str, datetime.date], bool]:
    try:
        text = CHANGELOG.read_text(encoding="utf-8")
    except OSError as exc:
        raise Blind(f"{CHANGELOG.name}: {exc}") from exc
    return {v: datetime.date.fromisoformat(d) for v, d in HEADING.findall(text)}, bool(UNRELEASED.search(text))


def judge_mark(text: str | None, version: str, unreleased: bool) -> tuple[str | None, str | None, list[str]]:
    """Rule 1 for one mark: (since, leaves, failures)."""
    fails: list[str] = []
    if text is None:
        return None, None, []
    if text.startswith("\u0000"):
        return None, None, [f"its message is not a string this check can read ({text[1:][:80]}): write it as a literal or a const string"]
    major = parse_version(version)[0]
    since = None
    sinces = SINCE.findall(text)
    if not sinces:
        fails.append(f'its message says no "Since X.Y.Z" ({text[:90]!r}); the words are {MARK_WORDS}')
    elif len(set(sinces)) > 1:
        fails.append(f"its message names more than one release it was marked in ({', '.join(sorted(set(sinces)))})")
    else:
        since = sinces[0]
        s, v = parse_version(since), parse_version(version)
        nexts = {(v[0], v[1], v[2] + 1), (v[0], v[1] + 1, 0)}
        if s > v and not (unreleased and s in nexts):
            fails.append(f"marked since {since}, after this release {version} and not the next one")
    leaves = None
    found = [(int(a), int(b)) for a, b in LEAVES.findall(text)]
    above = sorted(f for f in found if f[0] > major and f[1] == 0)
    if not found:
        fails.append(f'its message says no "Leaves in N.0" ({text[:90]!r}); the words are {MARK_WORDS}')
    elif not above:
        fails.append(f"it leaves in {found[0][0]}.{found[0][1]}, not in a major above {major} (N.0)")
    else:
        leaves = f"{above[0][0]}.0"
    return since, leaves, fails


def run(writing: bool) -> int:
    version = read_version()
    major = parse_version(version)[0]
    decls, reach = read_names()
    dates, unreleased = changelog()
    today = datetime.date.fromisoformat(os.environ["OW_PROMISE_TODAY"]) if os.environ.get("OW_PROMISE_TODAY") else datetime.date.today()

    fails: list[str] = []
    by_name: dict[str, list[str | None]] = {}
    for name, mark in decls:
        by_name.setdefault(name, []).append(mark)
    source: dict[str, dict] = {}
    for name, marks in sorted(by_name.items()):
        record: dict = {}
        judged = []
        for mark in marks:
            since, leaves, bad = judge_mark(mark, version, unreleased)
            fails += [f"(1) {name}: {b}" for b in bad]
            judged.append((mark, since, leaves))
        if all(m is not None for m, _, _ in judged):
            since, leaves = judged[0][1], judged[0][2]
            record = {"deprecated": {"since": since, "leaves": leaves}}
        source[name] = record
    present = set(source) | reach

    file = None
    if NAMES_FILE.exists():
        try:
            file = json.loads(NAMES_FILE.read_text(encoding="utf-8"))
        except ValueError as exc:
            raise Blind(f"{NAMES_FILE.name} is not JSON: {exc}") from exc
        if not (isinstance(file, dict) and isinstance(file.get("major"), int) and isinstance(file.get("names"), dict)
                and isinstance(file.get("version"), str)):
            raise Blind(f"{NAMES_FILE.name} has no version, major or names")
        if file["major"] > major:
            raise Blind(f"{NAMES_FILE.name} was written at {file['version']}, a later major than {version}")
    elif not writing:
        raise Blind(f"{NAMES_FILE} does not exist: run with --write once")

    released: set[str] = set()  # names a moved major may drop
    held_back = False
    if file:
        moved = file["major"] != major
        this_day = dates.get(version, today)
        for name, info in sorted(file["names"].items()):
            if name in present:
                continue
            if not moved:
                fails.append(f"(2) {name}: listed at {file['version']}, gone from {version}, and the major is still {major}")
                continue
            since = (info.get("deprecated") or {}).get("since")
            if not since:
                fails.append(f"(3) {name}: gone in major {major} without a mark first")
            elif since not in dates:
                fails.append(f"(3) {name}: marked since {since}, a release {CHANGELOG.name} does not date")
            elif six_months_after(dates[since]) > this_day:
                fails.append(f"(3) {name}: marked since {since} ({dates[since]}), gone {this_day}, before {six_months_after(dates[since])}")
            else:
                released.add(name)
                continue
            held_back = True
        if not writing:
            for name, record in source.items():
                if name not in file["names"]:
                    fails.append(f"(2) {name}: public in {version} and not in {NAMES_FILE.name}: run --write")
                elif file["names"][name] != record and not moved:  # a major rewrites the marks: --write
                    fails.append(f"(2) {name}: the source marks it {record or 'not at all'}, the names file {file['names'][name] or 'not at all'}: run --write")

    count, marked = len(source), sum(1 for r in source.values() if r)
    if writing:
        keep = {} if not file else {n: r for n, r in file["names"].items() if n not in released and n not in source}
        names = {**keep, **source}
        back = held_back and file is not None and file["major"] != major
        out = {"about": ABOUT, "version": file["version"] if back else version, "major": file["major"] if back else major,
               "names": dict(sorted(names.items()))}
        with NAMES_FILE.open("w", encoding="utf-8", newline="\n") as handle:
            handle.write(json.dumps(out, indent=1, ensure_ascii=True) + "\n")
        print(f"wrote {NAMES_FILE.name}: {len(names)} names, {marked} marked")
    for f in fails:
        print(f"  FAIL {f}")
    if fails:
        print(f"FAIL: {len(fails)} break(s) of the promise in {version}.")
        return 1
    print(f"PASS: {count} public names in {version}, {marked} marked deprecated, each with its since and the release it "
          f"leaves in; none gone within major {major}. Not checked: a mark's other words, a call's parameters, "
          f"behaviour under an unchanged name.")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=(__doc__ or "").splitlines()[0])
    parser.add_argument("--write", action="store_true", help="add new names and update the marks in the names file")
    engine_options(parser)
    args = parser.parse_args()
    vars(OPTIONS).update(vars(args))
    try:
        return run(args.write)
    except Blind as exc:
        print(f"BLIND: {exc}")
        return 2


if __name__ == "__main__":
    sys.exit(main())
