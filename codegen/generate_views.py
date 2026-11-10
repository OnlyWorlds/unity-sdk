#!/usr/bin/env python3
"""The distribution's views.json -> Runtime/Core/OWViews.Table.cs (OWViews.Banners, Layouts, Rows), the table
OWViews.ViewOf reads.

views.json (schema-dist serial 113 on) is presentation, as presentation.json is, and comes only from the pinned dist
vendored at codegen/schema-dist/ (check_drift.py holds it to the manifest). The file is checked here as the TypeScript
SDK's codegen checks it (codegen/generate_types.py load_views, 4.147.0, 4286e9b), and this exits 1 when it fails:
version 1 only; each row's keys the ones the reader reads (a new column is a new meaning); a kind that is a type, a
type with a supertype, or a profile's pair that profile's own reader matches (the room fixture's pairs for room, the
lens fixture's reading aid pairs for charter); no two rows holding one kind; every field a row names a field of its
type by the walk (schema.json, the base fields included); every layout one the file defines; distinct row ids.
`_meta` is not baked: its sentences are the file's rules, which ViewOf reads in code.

    python codegen/generate_views.py           check views.json, write the C#
    python codegen/generate_views.py --check   exit 1 if the C# is stale or the file fails a check

The C# is written LF-only, as the repo's .gitattributes keeps it.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parent.parent
DIST = ROOT / "codegen" / "schema-dist"
VIEWS = DIST / "views.json"
OUT = ROOT / "Packages" / "com.onlyworlds.sdk" / "Runtime" / "Core" / "OWViews.Table.cs"
ROW_KEYS = {"id", "kind", "from", "sentence", "lead", "fold", "layout", "banner"}
PROFILES = ("room", "charter")


def die(msg: str) -> None:
    raise SystemExit(f"views: {msg}")


def read_json(path: Path) -> Any:
    return json.loads(path.read_bytes().decode("utf-8"))


def lit(value: str) -> str:
    """A string as a C# literal: JSON's escapes, ASCII only, are C#'s for every character they write."""
    return json.dumps(value, ensure_ascii=True)


def strings(values: list[str]) -> str:
    return "Array.Empty<string>()" if not values else "new[] { " + ", ".join(lit(v) for v in values) + " }"


def check(doc: Any) -> dict[str, Any]:
    meta = doc.get("_meta") if isinstance(doc, dict) else None
    if not isinstance(meta, dict) or meta.get("version") != 1:
        die(f"`_meta.version` is {meta.get('version') if isinstance(meta, dict) else meta!r}, expected 1")
    if set(doc) != {"_meta", "banners", "layouts", "rows"}:
        die(f"the top-level keys are {sorted(doc)}, expected _meta, banners, layouts, rows")
    banners, layouts, rows = doc["banners"], doc["layouts"], doc["rows"]
    if (not isinstance(banners, list)
            or not all(isinstance(b, dict) and set(b) == {"mark", "defined_by"}
                       and isinstance(b["mark"], str) and isinstance(b["defined_by"], str) for b in banners)
            or len({b["mark"] for b in banners}) != len(banners)):
        die("`banners` must be a list of distinct {mark, defined_by}")
    if not isinstance(layouts, dict) or not layouts or not all(isinstance(v, str) for v in layouts.values()):
        die("`layouts` must be an object of layout names to their sentences")
    if not isinstance(rows, list) or not rows:
        die("`rows` must be a non-empty list")

    schema = read_json(DIST / "schema.json")
    base = {f["name"] for f in schema["base"]} - {"id", "world"}
    fields = {slug: {f["name"] for f in fs} | base for slug, fs in schema["types"].items()}

    def fold(s: str) -> str:
        return s.strip().lower()  # the pairs are ASCII, checked against the profiles' own lists

    room = {(fold(p["type"]), fold(p["supertype"])) for p in read_json(DIST / "profiles" / "room.fixture.json")["pairs"]}
    charter = {tuple(fold(x) for x in w.split("/"))
               for w in read_json(DIST / "profiles" / "lens.fixture.json")["reading_aid_pairs"]}

    ids: set[str] = set()
    kinds: set[tuple] = set()
    for row in rows:
        rid = row.get("id") if isinstance(row, dict) else None
        if not isinstance(rid, str) or not rid or rid in ids:
            die(f"a row's id is {rid!r}: each row needs its own non-empty id")
        ids.add(rid)
        stray = sorted(set(row) - ROW_KEYS)
        if stray:
            die(f"row {rid} carries {stray}, which OWViews does not read: a new column is a new meaning, read it in "
                "ViewOf first, then here")
        kind = row.get("kind")
        if isinstance(kind, dict) and set(kind) == {"profile", "pair"}:
            prof, pair = kind["profile"], kind["pair"]
            parts = pair.split("/") if isinstance(pair, str) else []
            if prof not in PROFILES or len(parts) != 2:
                die(f"row {rid}'s kind {kind!r} names a profile outside {PROFILES} or a pair not `type/Supertype`")
            if (fold(parts[0]), fold(parts[1])) not in (room if prof == "room" else charter):
                die(f"row {rid}'s pair {pair!r} is not one the {prof} profile's reader matches")
            slug = parts[0]
            key = ("profile", prof, fold(pair))
        elif (isinstance(kind, dict) and set(kind) in ({"type"}, {"type", "supertype"})
              and all(isinstance(v, str) for v in kind.values())):
            slug = kind["type"]
            key = ("type", fold(kind["type"]), fold(kind.get("supertype", "\0")))
        else:
            die(f"row {rid}'s kind {kind!r} is neither a type, a type with a supertype, nor a profile's pair")
        if slug not in fields:
            die(f"row {rid} names the type {slug!r}, not one of the 22 slugs as written")
        if key in kinds:
            die(f"row {rid} holds a kind another row holds; the file's `match` says no two rows do")
        kinds.add(key)

        def names(k: str) -> list[str]:
            v = row.get(k, [])
            if not isinstance(v, list) or not all(isinstance(x, str) for x in v):
                die(f"row {rid}'s `{k}` is {v!r}, expected a list of field names")
            return v

        named = names("sentence") + names("lead")
        folds = row.get("fold", [])
        if not isinstance(folds, list) or not all(isinstance(f, dict) and set(f) == {"field", "label"}
                                                 and isinstance(f["field"], str) and isinstance(f["label"], str)
                                                 for f in folds):
            die(f"row {rid}'s `fold` is {folds!r}, expected a list of {{field, label}}")
        named += [f["field"] for f in folds]
        layout = row.get("layout")
        if not isinstance(layout, dict) or layout.get("name") not in layouts or not set(layout) <= {"name", "by"}:
            die(f"row {rid}'s layout {layout!r} is not {{name, by?}} with a name the file defines")
        if "by" in layout:
            if not isinstance(layout["by"], str):
                die(f"row {rid}'s layout.by is {layout['by']!r}, expected a field name")
            named.append(layout["by"])
        unknown = sorted({n for n in named if n not in fields[slug]})
        if unknown:
            die(f"row {rid} names {unknown}, which {slug} does not have by the walk")
        banner = row.get("banner", [])
        if not isinstance(banner, list) or not all(isinstance(b, str) and b for b in banner):
            die(f"row {rid}'s `banner` is {banner!r}, expected a list of mark names")
        if not isinstance(row.get("from"), str):
            die(f"row {rid}'s `from` is {row.get('from')!r}, expected the page that defines the kind")
    check_cites(rows)
    return doc


def check_cites(rows: list[dict[str, Any]]) -> None:
    """views.fixture.json's cites (its third note): every field a row names (sentence, lead, fold, layout.by) is
    quoted from the page that defines the kind, at its line. Each quote is checked at its line of the vendored page,
    each cite names a field its row names, and each field a row names has a cite."""
    fixture = read_json(DIST / "views.fixture.json")
    named: set[tuple[str, str]] = set()
    for row in rows:
        for f in row.get("sentence", []) + row.get("lead", []) + [x["field"] for x in row.get("fold", [])]:
            named.add((row["id"], f))
        if "by" in row["layout"]:
            named.add((row["id"], row["layout"]["by"]))
    cited: set[tuple[str, str]] = set()
    for c in fixture.get("cites", []):
        where = f"the cite of {c.get('row')}.{c.get('field')} ({c.get('page')}:{c.get('line')})"
        if (c.get("row"), c.get("field")) not in named:
            die(f"{where} names a field its row does not name")
        page = DIST / c["page"]
        if not page.is_file():
            die(f"{where}: the page is not in the vendored dist")
        lines = page.read_bytes().decode("utf-8").split("\n")
        if not (isinstance(c["line"], int) and 1 <= c["line"] <= len(lines) and c["quote"] in lines[c["line"] - 1]):
            die(f"{where}: the quote is not at that line of the page")
        cited.add((c["row"], c["field"]))
    if named - cited:
        die(f"fields a row names with no cite in views.fixture.json: {sorted(named - cited)}")


def render(doc: dict[str, Any], sha: str, serial: str) -> str:
    out = [
        "// <auto-generated>",
        f"// Generated by codegen/generate_views.py from schema-dist serial {serial}'s views.json (sha256 {sha}).",
        "// Do not edit by hand: the file changes in the distribution, the pin moves, and this is regenerated.",
        "// </auto-generated>",
        "",
        "using System;",
        "using System.Collections.Generic;",
        "using System.Collections.ObjectModel;",
        "",
        "namespace OnlyWorlds.Sdk",
        "{",
        "    public static partial class OWViews",
        "    {",
        "        /// <summary>views.json's format, <c>_meta.version</c>.</summary>",
        f"        public const int Version = {doc['_meta']['version']};",
        "",
        "        /// <summary>sha256 of the views.json these rows were baked from, as the pinned MANIFEST.json lists it.</summary>",
        f"        public const string Sha256 = {lit(sha)};",
        "",
        "        /// <summary>The marks every page shows at its head, in the file's order, each with what defines it.</summary>",
        "        public static readonly IReadOnlyList<OWViewBanner> Banners = Array.AsReadOnly(new[]",
        "        {",
    ]
    for b in doc["banners"]:
        out.append(f"            new OWViewBanner({lit(b['mark'])},")
        out.append(f"                {lit(b['defined_by'])}),")
    out += [
        "        });",
        "",
        "        /// <summary>The layouts the file defines, by name, each with its sentence.</summary>",
        "        public static readonly IReadOnlyDictionary<string, string> Layouts = new ReadOnlyDictionary<string, string>(",
        "            new Dictionary<string, string>",
        "            {",
    ]
    for name, sentence in doc["layouts"].items():
        out.append(f"                [{lit(name)}] =")
        out.append(f"                    {lit(sentence)},")
    out += [
        "            });",
        "",
        "        /// <summary>The rows, in the file's order.</summary>",
        "        public static readonly IReadOnlyList<OWViewRow> Rows = Array.AsReadOnly(new[]",
        "        {",
    ]
    for r in doc["rows"]:
        k = r["kind"]
        kind = (f"OWViewKind.OfProfile({lit(k['profile'])}, {lit(k['pair'])})" if "profile" in k
                else f"OWViewKind.OfType({lit(k['type'])}, {lit(k['supertype']) if 'supertype' in k else 'null'})")
        folds = r.get("fold", [])
        fold = ("Array.Empty<OWViewFold>()" if not folds else
                "new[] { " + ", ".join(f"new OWViewFold({lit(f['field'])}, {lit(f['label'])})" for f in folds) + " }")
        lay = r["layout"]
        layout = f"new OWViewLayout({lit(lay['name'])}, {lit(lay['by']) if 'by' in lay else 'null'})"
        out += [
            f"            new OWViewRow({lit(r['id'])}, {kind},",
            f"                {lit(r['from'])},",
            f"                sentence: {strings(r.get('sentence', []))},",
            f"                lead: {strings(r.get('lead', []))},",
            f"                fold: {fold},",
            f"                layout: {layout},",
            f"                banner: {strings(r.get('banner', []))}),",
        ]
    out += ["        });", "    }", "}", ""]
    return "\n".join(out)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--check", action="store_true", help="exit 1 if the C# is stale or the file fails a check")
    args = ap.parse_args()
    if not VIEWS.is_file():
        die(f"{VIEWS} is absent: the views come only from the distribution's views.json (serial 113 on)")
    data = VIEWS.read_bytes()
    sha = hashlib.sha256(data).hexdigest()
    listed = read_json(DIST / "MANIFEST.json")["files"].get("views.json")
    if listed != sha:
        die(f"views.json hashes {sha}, the manifest lists {listed}: re-vendor the dist")
    serial = next(line.split(":", 1)[1].strip() for line in (DIST / "VERSION").read_text(encoding="utf-8").splitlines()
                  if line.startswith("serial:"))
    want = render(check(json.loads(data.decode("utf-8"))), sha, serial)
    if args.check:
        have = OUT.read_bytes().decode("utf-8") if OUT.is_file() else ""
        if have != want:
            print(f"STALE: {OUT.name} does not match what the pinned views.json generates", file=sys.stderr)
            return 1
        print(f"ok: {OUT.name} matches views.json at serial {serial}")
        return 0
    OUT.write_bytes(want.encode("utf-8"))  # bytes: LF on every platform
    print(f"wrote {OUT.name} from views.json at serial {serial}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
