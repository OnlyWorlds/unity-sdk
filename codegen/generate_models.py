#!/usr/bin/env python3
"""OnlyWorlds schema -> C# Unity models (the emitter).

Reads the VENDORED, hash-verified schema distribution under codegen/schema-dist/
and emits one C# file per element type into
Packages/com.onlyworlds.sdk/Runtime/Models/Generated/, plus a slug<->Type
registry that OWPin's generic link needs to resolve a target.

    python codegen/generate_models.py            # emit
    python codegen/generate_models.py --check    # exit 1 if the tree would change

THE WALK IS IMPORTED, NEVER RE-IMPLEMENTED. schema_walk.py is the one decoder of
what the YAMLs mean; the dist's own README says "vendor it or port it, but keep
its semantics", and ten copies of it were deleted on 2026-07-28 for exactly the
reason this file does not make an eleventh. If this emitter needs something the
walk does not return, the fix is upstream in schema-dist, not a local patch.

WHAT THE RULING TABLE BINDS HERE (walk/rulings.yaml, read the row not the key):

  nullable-by-default  -- ONLY `name` is required. Every scalar int is emitted as
      SerializableNullable<int>, never int. No `required:` list is enforced in
      construction; pin/marker's lists are schema intent, not wire enforcement,
      and Captain ruled them dropped from canonical.
  string-empty-is-unset -- strings are plain `string`. "" IS the wire's unset;
      keel stores them blank=True and never nullable, so there is no third state
      and the emitter must not invent one by making strings nullable.
  maximum-is-advisory-and-zero-means-unbounded -- NO range validation is emitted.
      The walk does not surface bounds at all, and that silence is load-bearing.
      Reading `minimum:`/`maximum:` directly out of the YAML here would be
      re-deriving schema semantics OUTSIDE the one decoder, which the row
      forbids in as many words.
  extension-passthrough -- handled by OWElement's [JsonExtensionData] bag, which
      every generated class inherits. Nothing to emit per type.
  unknown-field-types-must-be-surfaced -- a real note sink is passed on every
      walk call and every note is printed AND written into the generated header.
      The walk's default sink is a no-op, so a pinned older walk meeting a newer
      schema drops fields in silence; --check treats an unexpected note as a
      hard failure.
  required-world-wire-caveat -- `world` is on OWElement as a read-only property.
      Not emitted per type.

CONVENTIONS REPRODUCED FROM THE THREE HAND-WRITTEN PROVING MODELS
(Runtime/Models/OWCharacter.cs, OWPin.cs, OWMarker.cs -- they are the contract):

  * [Serializable] + [JsonObject(MemberSerialization.OptIn)] on EVERY class.
    OptIn is load-bearing, not tidiness: Newtonsoft's default is OptOut, which
    serializes public properties beside the attributed fields, sending every
    field to the wire twice ("name" AND "Name"). The server ignores the
    PascalCase copies, so it survived a day of green tests and a real
    1,087-element world. Newtonsoft does NOT inherit [JsonObject], so the line
    must be repeated on every subclass, and a member reaches the wire only if it
    carries [JsonProperty].
  * [SerializeField] private backing field, camelCase with a leading underscore.
  * Public property, PascalCase. Scalars and single-links get get/set; LISTS ARE
    GET-ONLY and initialized inline, matching OWCharacter -- reassigning a list
    Unity is serializing is how you lose a reference silently.
  * Field ORDER is the schema's document order, which IS display order.
  * Links are bare UUIDs (v2 wire truth): single-link -> string,
    multi-link -> List<string>, no _ids suffix (dead v1 dialect), no link stubs.
  * generic-link (pin.element only) -> the element_type/element_id STRING PAIR,
    not one field named `element`. The walk's own note says so.
"""

from __future__ import annotations

import argparse
import difflib
import sys
from pathlib import Path

CODEGEN = Path(__file__).resolve().parent
REPO = CODEGEN.parent
DIST_DIR = CODEGEN / "schema-dist"
SCHEMA_DIR = DIST_DIR / "schema"
OUT_DIR = REPO / "Packages" / "com.onlyworlds.sdk" / "Runtime" / "Models" / "Generated"

# Bytecode writing OFF before the import. Importing from the vendored tree makes
# CPython drop walk/__pycache__/*.pyc INSIDE a hash-verified directory, and a
# verifier that refuses untracked files there goes red for a file the import
# itself created. (Kael hit exactly this on the npm side, 2026-07-29.)
sys.dont_write_bytecode = True
sys.path.insert(0, str(DIST_DIR / "walk"))
import schema_walk as walk  # noqa: E402  (path must be set first)

ELEMENT_TYPES = walk.ELEMENT_TYPES

# ---------------------------------------------------------------------------
# Notes from the walk. Two are EXPECTED at the pinned dist; anything else is a
# schema the emitter cannot fully read, and emitting a partial type from a
# schema you cannot fully read is the failure the ruling row exists to prevent.
# ---------------------------------------------------------------------------
EXPECTED_NOTE_PREFIXES = (
    "pin.element: `generic-link`",
    "relation.events: field declared more than once",
)

notes: list[str] = []


def note(msg: str) -> None:
    notes.append(msg)


def unexpected_notes() -> list[str]:
    return [n for n in notes if not n.startswith(EXPECTED_NOTE_PREFIXES)]


# ---------------------------------------------------------------------------
# Naming
# ---------------------------------------------------------------------------
def class_name(slug: str) -> str:
    """`character` -> `OWCharacter`. Matches the three hand-written models."""
    return "OW" + slug.capitalize()


def pascal(field: str) -> str:
    """`hit_points` -> `HitPoints`; `STR` -> `Str`.

    The all-caps case is the schema's six TTRPG stats, and the hand-written
    OWCharacter names them Str/Dex/Con/Int/Wis/Cha -- so the JSON key stays
    `STR` while the C# property reads as a name rather than a shout. `Int` is
    legal as a property name (only lowercase `int` is the keyword).
    """
    if field.isupper():
        return field[0] + field[1:].lower()
    return "".join(part.capitalize() for part in field.split("_"))


def camel(field: str) -> str:
    """Backing-field name, without the leading underscore. `STR` -> `str`."""
    p = pascal(field)
    return p[0].lower() + p[1:]


def escape_xml(text: str) -> str:
    return text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


# ---------------------------------------------------------------------------
# Field emission
# ---------------------------------------------------------------------------
def cs_type(kind: str) -> str:
    return {
        "scalar_str": "string",
        "scalar_int": "SerializableNullable<int>",
        "single": "string",
        "multi": "List<string>",
    }[kind]


def emit_field(spec: dict, lines: list[str]) -> None:
    """One backing field, with its doc comment, in the hand-written style."""
    kind = spec["kind"]

    if kind == "generic":
        # pin.element. The generic link is a (type, id) PAIR on the wire, so it
        # is two fields with two JSON names -- never one field called `element`.
        desc = spec.get("desc", "")
        lines.append("        /// <summary>")
        lines.append(f"        /// Element type slug the link points at, e.g. <c>character</c>.")
        lines.append("        /// </summary>")
        lines.append("        /// <remarks>")
        lines.append("        /// The schema's ONLY <c>generic-link</c>: it points at any element type, carried on")
        lines.append("        /// the wire as an (<c>element_type</c>, <c>element_id</c>) pair rather than a bare")
        lines.append("        /// UUID. Resolve the slug through <see cref=\"OWElementTypes\"/>.")
        if desc:
            lines.append(f"        /// <para>Schema: {escape_xml(desc)}</para>")
        lines.append("        /// </remarks>")
        lines.append('        [JsonProperty("element_type")]')
        lines.append("        [SerializeField] private string _elementType;")
        lines.append("")
        lines.append("        /// <summary>UUID of the element the link points at.</summary>")
        lines.append('        [JsonProperty("element_id")]')
        lines.append("        [SerializeField] private string _elementId;")
        lines.append("")
        return

    name = spec["name"]
    desc = spec.get("desc", "")
    if desc:
        lines.append(f"        /// <summary>{escape_xml(desc)}</summary>")
    if kind in ("single", "multi"):
        target = spec.get("target")
        arity = "UUID" if kind == "single" else "UUIDs"
        if target:
            lines.append(
                f"        /// <remarks>Link: {arity} of {target}. Bare id{'s' if kind == 'multi' else ''}"
                " -- resolve through the cache.</remarks>"
            )
        else:
            # A link the walk could not resolve. Say so in the generated file
            # rather than emitting a confident-looking untargeted link.
            lines.append(
                f"        /// <remarks>Link: {arity} with NO resolved category in the schema.</remarks>"
            )

    lines.append(f'        [JsonProperty("{name}")]')
    decl = f"        [SerializeField] private {cs_type(kind)} _{camel(name)}"
    if kind == "multi":
        decl += " = new List<string>()"
    lines.append(decl + ";")
    lines.append("")


def emit_property(spec: dict, lines: list[str]) -> None:
    kind = spec["kind"]

    if kind == "generic":
        lines.append("        public string ElementType { get => _elementType; set => _elementType = value; }")
        lines.append("        public string ElementId { get => _elementId; set => _elementId = value; }")
        lines.append("")
        lines.append("        /// <summary>True when the generic link points somewhere resolvable.</summary>")
        lines.append(
            "        public bool HasTarget => "
            "!string.IsNullOrEmpty(_elementType) && !string.IsNullOrEmpty(_elementId);"
        )
        lines.append("")
        return

    name = spec["name"]
    prop = pascal(name)
    backing = f"_{camel(name)}"

    if kind == "multi":
        # Get-only, exactly as the hand-written models do it. A settable list
        # property lets a caller swap out the instance Unity is serializing.
        lines.append(f"        public List<string> {prop} => {backing};")
    else:
        lines.append(
            f"        public {cs_type(kind)} {prop} "
            f"{{ get => {backing}; set => {backing} = value; }}"
        )


# ---------------------------------------------------------------------------
# File emission
# ---------------------------------------------------------------------------
def provenance() -> list[str]:
    version = (DIST_DIR / "VERSION").read_text(encoding="utf-8").strip().splitlines()
    return [f"// {line.strip()}" for line in version]


def emit_type(slug: str, fields: list[dict], doc_desc: str | None) -> str:
    cls = class_name(slug)
    lines: list[str] = []

    lines.append("// GENERATED from the OnlyWorlds schema distribution -- DO NOT EDIT.")
    lines.append("// Regenerate: python codegen/generate_models.py   (drift guard: codegen/check_drift.py)")
    lines.append("//")
    lines += provenance()
    lines.append("")
    lines.append("using System;")
    lines.append("using System.Collections.Generic;")
    lines.append("using Newtonsoft.Json;")
    lines.append("using UnityEngine;")
    lines.append("")
    lines.append("namespace OnlyWorlds.Sdk")
    lines.append("{")

    lines.append("    /// <summary>")
    if doc_desc:
        lines.append(f"    /// {escape_xml(doc_desc)}")
    else:
        lines.append(f"    /// The <c>{slug}</c> element type.")
    lines.append("    /// </summary>")
    lines.append("    /// <remarks>")
    lines.append("    /// <para>")
    lines.append("    /// Only <c>name</c> is required (rulings.yaml: <c>nullable-by-default</c>). Every scalar")
    lines.append("    /// integer is a <see cref=\"SerializableNullable{T}\"/> because unset and 0 are different")
    lines.append("    /// claims, and no range validation is generated -- <c>maximum:</c> is advisory and the")
    lines.append("    /// walk does not surface bounds at all.")
    lines.append("    /// </para>")
    lines.append("    /// <para>")
    lines.append("    /// Field order is the schema's document order, which is also display order.")
    lines.append("    /// </para>")
    lines.append("    /// </remarks>")

    # OptIn must be repeated on every subclass -- Newtonsoft does not inherit
    # [JsonObject]. The hand-written models carry this comment; so does every
    # generated one, because the emitter is where the rule now lives.
    lines.append("    // OptIn must be repeated on every subclass -- Newtonsoft does not inherit [JsonObject],")
    lines.append("    // so a model without it silently serializes every public property alongside the")
    lines.append("    // attributed fields, duplicating each one on the wire.")
    lines.append("    [Serializable]")
    lines.append("    [JsonObject(MemberSerialization.OptIn)]")
    lines.append(f"    public class {cls} : OWElement")
    lines.append("    {")

    current_section = None
    for spec in fields:
        section = spec.get("section")
        if section != current_section:
            current_section = section
            if section:
                dashes = "-" * max(4, 60 - len(section))
                lines.append(f"        // -- {section} {dashes}")
                lines.append("")
        emit_field(spec, lines)

    lines.append("        // -- Accessors --------------------------------------------------------")
    lines.append("")
    current_section = None
    for spec in fields:
        section = spec.get("section")
        if section != current_section and current_section is not None:
            lines.append("")
        current_section = section
        emit_property(spec, lines)

    lines.append("    }")
    lines.append("}")
    return "\n".join(lines) + "\n"


def _chunk(items: list[str], size: int):
    for i in range(0, len(items), size):
        yield items[i:i + size]


# The four fields that exist on every wire body and in no element YAML. They are
# server-managed and read-only on OWElement; the schema has no place to declare
# them, so they are the emitter's one legitimate literal.
SERVER_MANAGED_FIELDS = ["type", "created_at", "updated_at", "change_seq"]


def base_field_names() -> list[str]:
    """The base wire fields, DERIVED from base_properties.yaml rather than retyped.

    Two transformations the schema does not carry, both deliberate:

      * Schema keys are TitleCase (`Id`, `Name`, `Image_URL`); the wire is
        snake_case. Lowercasing is the whole mapping -- `Image_URL` -> `image_url`.
      * `world` is KEPT here even though the v2 API rejects it in a request body
        (rulings.yaml: required-world-wire-caveat). This list answers "does a model
        know this key when READING", and world is present on every body the server
        returns. Stripping it on write is OWPayload's job, not this list's.

    Reading the YAML rather than restating it means a base_properties change
    reaches this list on the next regeneration instead of never.
    """
    doc = walk.load_yaml(SCHEMA_DIR, "base_properties")
    names = [key.lower() for key in (doc.get("properties") or {})]
    return names + SERVER_MANAGED_FIELDS


def emit_registry(per_type: dict[str, list[dict]]) -> str:
    lines: list[str] = []
    lines.append("// GENERATED from the OnlyWorlds schema distribution -- DO NOT EDIT.")
    lines.append("// Regenerate: python codegen/generate_models.py   (drift guard: codegen/check_drift.py)")
    lines.append("//")
    lines += provenance()
    lines.append("")
    lines.append("using System;")
    lines.append("using System.Collections.Generic;")
    lines.append("")
    lines.append("namespace OnlyWorlds.Sdk")
    lines.append("{")
    lines.append("    /// <summary>")
    lines.append("    /// Every element type in the standard, and the C# class that models it.")
    lines.append("    /// </summary>")
    lines.append("    /// <remarks>")
    lines.append("    /// <para>")
    lines.append("    /// GENERATED, so the membership list cannot drift from the models beside it. This is the")
    lines.append("    /// resolver <see cref=\"OWPin\"/>'s generic link needs: the wire hands over an")
    lines.append("    /// <c>element_type</c> slug and an <c>element_id</c>, and the slug has to become a type")
    lines.append("    /// before anything can be deserialized into it.")
    lines.append("    /// </para>")
    lines.append("    /// <para>")
    lines.append("    /// Both directions are exposed because both are needed and neither is derivable from the")
    lines.append("    /// other at a glance: <see cref=\"TypeFor\"/> for reading the wire,")
    lines.append("    /// <see cref=\"SlugFor\"/> for writing to it.")
    lines.append("    /// </para>")
    lines.append("    /// </remarks>")
    lines.append("    public static class OWElementTypes")
    lines.append("    {")
    lines.append("        /// <summary>Every element type slug, in the standard's own order.</summary>")
    lines.append("        public static readonly string[] Slugs =")
    lines.append("        {")
    # Wrap the slug list at a readable width.
    row: list[str] = []
    width = 0
    for slug in ELEMENT_TYPES:
        token = f'"{slug}",'
        if width + len(token) + 1 > 88:
            lines.append("            " + " ".join(row))
            row, width = [], 0
        row.append(token)
        width += len(token) + 1
    if row:
        lines.append("            " + " ".join(row))
    lines.append("        };")
    lines.append("")
    lines.append("        private static readonly Dictionary<string, Type> _bySlug =")
    lines.append("            new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)")
    lines.append("        {")
    for slug in ELEMENT_TYPES:
        lines.append(f'            {{ "{slug}", typeof({class_name(slug)}) }},')
    lines.append("        };")
    lines.append("")
    lines.append("        private static readonly Dictionary<Type, string> _bySlugReverse = BuildReverse();")
    lines.append("")
    lines.append("        private static Dictionary<Type, string> BuildReverse()")
    lines.append("        {")
    lines.append("            var map = new Dictionary<Type, string>();")
    lines.append("            foreach (var pair in _bySlug) map[pair.Value] = pair.Key;")
    lines.append("            return map;")
    lines.append("        }")
    lines.append("")
    lines.append("        /// <summary>")
    lines.append("        /// The model type for a slug, or <c>null</c> when the slug is not in the standard.")
    lines.append("        /// </summary>")
    lines.append("        /// <remarks>")
    lines.append("        /// Returns null rather than throwing: an unknown slug means a world written by a")
    lines.append("        /// NEWER standard than this package was generated against, which a reader should")
    lines.append("        /// report and skip, not crash on. Matching is case-insensitive because the wire's")
    lines.append("        /// casing is not something a caller should have to know.")
    lines.append("        /// </remarks>")
    lines.append("        public static Type TypeFor(string slug)")
    lines.append("        {")
    lines.append("            if (string.IsNullOrEmpty(slug)) return null;")
    lines.append("            return _bySlug.TryGetValue(slug, out var type) ? type : null;")
    lines.append("        }")
    lines.append("")
    lines.append("        /// <summary>The slug for a model type, or <c>null</c> if it is not a generated model.</summary>")
    lines.append("        public static string SlugFor(Type type)")
    lines.append("        {")
    lines.append("            if (type == null) return null;")
    lines.append("            return _bySlugReverse.TryGetValue(type, out var slug) ? slug : null;")
    lines.append("        }")
    lines.append("")
    lines.append("        /// <summary>The slug for a model type.</summary>")
    lines.append("        public static string SlugFor<T>() where T : OWElement => SlugFor(typeof(T));")
    lines.append("")
    lines.append("        /// <summary>True when the slug names a type in the standard.</summary>")
    lines.append("        public static bool IsKnown(string slug) => TypeFor(slug) != null;")
    lines.append("")
    lines.append("        /// <summary>")
    lines.append("        /// The JSON field names each generated model carries, per slug -- NOT including the")
    lines.append("        /// shared base fields.")
    lines.append("        /// </summary>")
    lines.append("        /// <remarks>")
    lines.append("        /// Generated alongside the models so a test can ask what a model knows without")
    lines.append("        /// reflecting over private fields, and so a folder reader can report an unknown key")
    lines.append("        /// rather than swallowing it. <c>pin</c> carries <c>element_type</c> and")
    lines.append("        /// <c>element_id</c> here, which is what the wire carries -- there is no field named")
    lines.append("        /// <c>element</c>.")
    lines.append("        /// </remarks>")
    lines.append("        public static readonly IReadOnlyDictionary<string, string[]> FieldNames =")
    lines.append("            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)")
    lines.append("        {")
    for slug in ELEMENT_TYPES:
        names: list[str] = []
        for spec in per_type[slug]:
            if spec["kind"] == "generic":
                names += ["element_type", "element_id"]
            else:
                names.append(spec["name"])
        joined = ", ".join(f'"{n}"' for n in names)
        lines.append(f'            {{ "{slug}", new[] {{ {joined} }} }},')
    lines.append("        };")
    lines.append("")
    lines.append("        /// <summary>Field names on <see cref=\"OWElement\"/>, shared by every type.</summary>")
    lines.append("        /// <remarks>")
    lines.append("        /// A CONSTANT in the emitter rather than a walk result: these are")
    lines.append("        /// <c>base_properties.yaml</c>'s fields (lowercased to their wire spelling) plus the")
    lines.append("        /// four server-managed fields that live in no element YAML at all. Change it in")
    lines.append("        /// <c>codegen/generate_models.py</c> beside <see cref=\"OWElement\"/>, never here.")
    lines.append("        /// It sits next to the generated per-type lists so a caller can ask \"does this")
    lines.append("        /// model know this key?\" in one place.")
    lines.append("        /// </remarks>")
    lines.append("        public static readonly string[] BaseFieldNames =")
    lines.append("        {")
    for chunk in _chunk(base_field_names(), 6):
        lines.append("            " + " ".join(f'"{n}",' for n in chunk))
    lines.append("        };")
    lines.append("    }")
    lines.append("}")
    return "\n".join(lines) + "\n"


README_TEXT = """\
GENERATED CODE -- DO NOT EDIT ANY FILE IN THIS DIRECTORY.

Every .cs file here is emitted by codegen/generate_models.py from the vendored,
hash-verified OnlyWorlds schema distribution under codegen/schema-dist/.

Regenerate on a schema-pin bump:

    python codegen/generate_models.py

Check for drift (CI, and before any push that touches the pin):

    python codegen/check_drift.py

An edit made here is lost on the next regeneration, silently and without a
conflict. If a model needs to be different, the emitter changes; if the SCHEMA
needs to be different, that is a Council motion, and if a semantic CONVENTION
needs to change, that is a row in walk/rulings.yaml -- not a hand edit here.

Hand-written code that extends a generated model belongs in a partial class in
Runtime/Models/, outside this folder.
"""


# ---------------------------------------------------------------------------
# Driver
# ---------------------------------------------------------------------------
def build() -> dict[str, str]:
    """Returns {relative filename: content} for the whole generated tree."""
    per_type: dict[str, list[dict]] = {}
    descriptions: dict[str, str | None] = {}

    for slug in ELEMENT_TYPES:
        doc = walk.load_yaml(SCHEMA_DIR, slug)
        descriptions[slug] = doc.get("description")
        per_type[slug] = walk.flatten_fields(
            doc,
            slug,
            note=note,
            include_desc=True,
            include_sections=True,
        )

    out: dict[str, str] = {}
    for slug in ELEMENT_TYPES:
        out[f"{class_name(slug)}.cs"] = emit_type(slug, per_type[slug], descriptions[slug])
    out["OWElementTypes.cs"] = emit_registry(per_type)
    out["README.md"] = README_TEXT
    return out


def write(tree: dict[str, str]) -> None:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    for name, content in tree.items():
        # newline="\n" on write with no read: nothing here is a round trip, every
        # file is produced whole, so LF is chosen once and stays chosen. The dist
        # is LF-normalized for the same reason -- bytes that change on checkout
        # break hash verification.
        (OUT_DIR / name).write_text(content, encoding="utf-8", newline="\n")


def check(tree: dict[str, str]) -> int:
    """Diff the tree we WOULD write against what is committed. 0 = clean."""
    problems = 0

    existing = {p.name for p in OUT_DIR.glob("*") if p.is_file() and p.suffix != ".meta"}
    expected = set(tree)

    for name in sorted(expected - existing):
        print(f"MISSING from Generated/: {name}")
        problems += 1
    for name in sorted(existing - expected):
        print(f"UNEXPECTED file in Generated/ (not emitted by this generator): {name}")
        problems += 1

    for name in sorted(expected & existing):
        on_disk = (OUT_DIR / name).read_text(encoding="utf-8", newline="")
        wanted = tree[name]
        if on_disk == wanted:
            continue
        problems += 1
        print(f"DRIFT in {name}:")
        diff = difflib.unified_diff(
            on_disk.splitlines(), wanted.splitlines(),
            fromfile=f"committed/{name}", tofile=f"generated/{name}", lineterm="",
        )
        for line in list(diff)[:40]:
            print("  " + line)

    return problems


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--check", action="store_true",
        help="do not write; exit 1 if the committed tree differs from a fresh generation",
    )
    args = parser.parse_args()

    tree = build()

    # Notes first, always, and before anything is written. The walk's default
    # sink is a no-op; a vendored walk meeting a newer schema drops fields in
    # total silence, so an unexpected note means this emitter cannot fully read
    # the schema it was pointed at and must not pretend otherwise.
    for n in notes:
        print(f"note: {n}")
    bad = unexpected_notes()
    if bad:
        print()
        print("UNKNOWN SCHEMA CONSTRUCT -- refusing to emit a partial model.")
        print("rulings.yaml: unknown-field-types-must-be-surfaced.")
        for n in bad:
            print(f"  {n}")
        return 2

    if args.check:
        problems = check(tree)
        if problems:
            print(f"\n{problems} file(s) differ. Run: python codegen/generate_models.py")
            return 1
        print(f"\nclean: {len(tree)} generated files match a fresh generation.")
        return 0

    write(tree)
    total_fields = sum(
        len(walk.flatten_fields(walk.load_yaml(SCHEMA_DIR, s), s)) for s in ELEMENT_TYPES
    )
    print(f"\nwrote {len(tree)} files to {OUT_DIR}")
    print(f"{len(ELEMENT_TYPES)} element types, {total_fields} schema fields")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
