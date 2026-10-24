#!/usr/bin/env python3
"""Generate the SDK's NFC tables from Unicode's UnicodeData.txt and CompositionExclusions.txt, at the version folder spec 4.7 pins.

Folder spec section 4.7 (OW-18, ruled at schema-dist serial 57) rules one fold for every `.owworld` reader: Unicode
NFC at 15.1.0, then simple case folding, and the NFC is built from Unicode's own files, never from a runtime.
`string.Normalize` is the runtime's own tables (Mono's, IL2CPP's and .NET's differ, and a runtime on Unicode 16
decomposes U+11383, which 15.1 leaves unassigned). So, as `tools/gen_casefold.py` does for the fold, this script
reads the two files the conformance fixture set vendors beside its fixtures, pinned here by sha256, and writes their
facts into C#: `Packages/com.onlyworlds.sdk/Runtime/Core/OWNfcData.cs`, written whole. It never fetches anything.

What it writes is the files' raw facts, not a derived table (the shape of the Python SDK's `_nfc.py`):

- the non-zero canonical combining classes (UnicodeData field 3), as sorted code points and their classes;
- every canonical decomposition mapping (field 5 without a `<tag>`: a tagged mapping is compatibility), one level
  deep, as sorted code points with a first and a second part (-1 for a singleton);
- the code points CompositionExclusions lists uncommented (script specifics and post composition version).

`OWNfc` derives the rest at its first call: full decompositions, Full_Composition_Exclusion (the listed ones plus
what the file's comments derive from UnicodeData: singletons and non-starter decompositions), and the primary
composites. Hangul is arithmetic in `OWNfc` (UnicodeData lists no syllable's decomposition).

The generator derives the same sets independently (as `tools/owworld-fixtures/run_py.py`'s `load_nfc` does) and
writes their counts and sha256 digests as constants: `OWNfcTests` derives them through the C# code and must reach the
same digests, so the C# derivation is checked against this one, and NormalizationTest-15.1.0.txt checks the result.
It also writes `StableBelow`: every code point below it has class 0 and NFC_QC=Yes (no exclusion, no composite's
second part), so a string of such code points is its own NFC by UAX #15's quick check, and is returned untouched.

Usage:
    python tools/gen_nfc.py --dir codegen/schema-dist/fixtures/owworld           # rewrite
    python tools/gen_nfc.py --dir codegen/schema-dist/fixtures/owworld --check   # diff only

When folder spec 4.7 moves the version, change VERSION and both SHA256s together, with the fixture set's copies.
"""
import argparse
import hashlib
import os
import sys

VERSION = "15.1.0"
UNICODE_DATA = "UnicodeData-15.1.0.txt"
UNICODE_DATA_SHA256 = "2fc713e6a31a87c4850a37fe2caffa4218180fadb5de86b43a143ddb4581fb86"
EXCLUSIONS = "CompositionExclusions-15.1.0.txt"
EXCLUSIONS_SHA256 = "59d2d9e3dfdf0a999cf9dae11d594f053631222679a2f5710315ea07f7fe82af"
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..",
                   "Packages", "com.onlyworlds.sdk", "Runtime", "Core", "OWNfcData.cs")

HANGUL_L, HANGUL_V, HANGUL_T, HANGUL_S = 0x1100, 0x1161, 0x11A7, 0xAC00
HANGUL_V_COUNT, HANGUL_T_COUNT, HANGUL_S_COUNT = 21, 28, 11172


def read_pinned(folder, name, sha):
    raw = open(os.path.join(folder, name), "rb").read()
    got = hashlib.sha256(raw).hexdigest()
    if got != sha:
        sys.exit("%s has sha256 %s, not the pinned %s (Unicode %s)" % (name, got, sha, VERSION))
    return raw.decode("utf-8")


def parse_unicode_data(text):
    """Field 3 (non-zero classes) and field 5 (canonical mappings) of UnicodeData.txt. A `<..., First>`/`<..., Last>`
    range must carry class 0 and no mapping (it does at 15.1: CJK, Hangul syllables, Tangut, ...), so the ranges add
    nothing here; that is checked, not assumed."""
    classes, mappings = {}, {}
    for line in text.splitlines():
        if not line:
            continue
        f = line.split(";")
        cp = int(f[0], 16)
        if f[1].endswith(", First>") or f[1].endswith(", Last>"):
            if f[3] != "0" or f[5]:
                sys.exit("the range at U+%04X carries a class or a mapping" % cp)
            continue
        if f[3] != "0":
            classes[cp] = int(f[3])
        if f[5] and not f[5].startswith("<"):
            to = [int(h, 16) for h in f[5].split()]
            if not 1 <= len(to) <= 2:
                sys.exit("U+%04X: a canonical mapping of %d code points" % (cp, len(to)))
            mappings[cp] = to
    for cp in mappings:
        if HANGUL_S <= cp < HANGUL_S + HANGUL_S_COUNT:
            sys.exit("UnicodeData lists a Hangul syllable's mapping (U+%04X); OWNfc decomposes them by arithmetic" % cp)
    return classes, mappings


def parse_exclusions(text):
    """The uncommented code points (and ranges) of CompositionExclusions.txt."""
    first = text.splitlines()[0]
    if VERSION not in first:
        sys.exit("%s says %r, not Unicode %s" % (EXCLUSIONS, first, VERSION))
    listed = set()
    for line in text.splitlines():
        line = line.split("#", 1)[0].strip()
        if line:
            lo, _, hi = line.partition("..")
            listed.update(range(int(lo, 16), int(hi or lo, 16) + 1))
    return listed


def derive(classes, mappings, listed):
    """Full_Composition_Exclusion and the primary composites, as run_py.py's load_nfc derives them."""
    excluded = set(listed)
    for cp, d in mappings.items():
        if len(d) == 1 or classes.get(cp, 0) or classes.get(d[0], 0):
            excluded.add(cp)
    composites = {(d[0], d[1]): cp for cp, d in mappings.items() if len(d) == 2 and cp not in excluded}
    return excluded, composites


def stable_below(classes, excluded, composites):
    """The lowest code point whose NFC quick check (UAX #15 section 9) is not Yes with class 0: a non-zero class, a
    Full_Composition_Exclusion (NFC_QC=No), a primary composite's second part or a Hangul V or T (NFC_QC=Maybe). A
    string whose every code point has NFC_QC=Yes and class 0 is its own NFC, by that section's quick check."""
    unstable = set(classes) | set(excluded) | {b for (_, b) in composites}
    unstable |= set(range(HANGUL_V, HANGUL_V + HANGUL_V_COUNT)) | set(range(HANGUL_T + 1, HANGUL_T + HANGUL_T_COUNT))
    return min(unstable)


def digest_set(codes):
    """sha256 of one `XXXX\n` line per code point, ascending (uppercase hex, at least four digits)."""
    return hashlib.sha256("".join("%04X\n" % c for c in sorted(codes)).encode("ascii")).hexdigest()


def digest_composites(composites):
    """sha256 of one `XXXX XXXX;YYYY\n` line per primary composite, ascending by the composite."""
    lines = sorted((cp, a, b) for (a, b), cp in composites.items())
    return hashlib.sha256("".join("%04X %04X;%04X\n" % (a, b, cp) for cp, a, b in lines).encode("ascii")).hexdigest()


def rows(values, width=8):
    out = []
    for i in range(0, len(values), width):
        out.append("            " + " ".join(v + "," for v in values[i:i + width]))
    return "\n".join(out)


def hexes(values):
    return ["0x%04X" % v for v in values]


def render(classes, mappings, listed, excluded, composites):
    class_keys = sorted(classes)
    map_keys = sorted(mappings)
    return TEMPLATE.format(
        version=VERSION, data=UNICODE_DATA, data_sha=UNICODE_DATA_SHA256, excl=EXCLUSIONS, excl_sha=EXCLUSIONS_SHA256,
        stable="0x%04X" % stable_below(classes, excluded, composites),
        class_count=len(class_keys), map_count=len(map_keys), listed_count=len(listed),
        excluded_count=len(excluded), excluded_digest=digest_set(excluded),
        composite_count=len(composites), composite_digest=digest_composites(composites),
        class_cps=rows(hexes(class_keys)), class_values=rows([str(classes[k]) for k in class_keys], 16),
        map_cps=rows(hexes(map_keys)), map_first=rows(hexes([mappings[k][0] for k in map_keys])),
        map_second=rows([("0x%04X" % mappings[k][1]) if len(mappings[k]) == 2 else "-1" for k in map_keys]),
        listed=rows(hexes(sorted(listed))))


TEMPLATE = '''// <auto-generated>
// Generated by tools/gen_nfc.py from Unicode {version} {data} (sha256 {data_sha})
// and {excl} (sha256 {excl_sha}).
// Do not edit by hand: run `python tools/gen_nfc.py --dir ...`.
// </auto-generated>

namespace OnlyWorlds.Sdk
{{
    /// <summary>
    /// The facts Unicode NFC is built from, as UnicodeData.txt and CompositionExclusions.txt state them at the version
    /// folder spec §4.7 (OW-18) pins: the canonical combining classes, the canonical decompositions one level deep,
    /// and the listed composition exclusions. <see cref="OWNfc"/> derives the rest at its first call.
    /// </summary>
    internal static class OWNfcData
    {{
        /// <summary>The Unicode version the tables were generated from (folder spec §4.7 pins it).</summary>
        internal const string UnicodeVersion = "{version}";

        /// <summary>sha256 of the UnicodeData.txt the tables were generated from.</summary>
        internal const string UnicodeDataSha256 = "{data_sha}";

        /// <summary>sha256 of the CompositionExclusions.txt the tables were generated from.</summary>
        internal const string CompositionExclusionsSha256 = "{excl_sha}";

        /// <summary>
        /// Every code point below this has class 0 and NFC_QC=Yes (no Full_Composition_Exclusion, no primary
        /// composite's second part), so a string of them is its own NFC (UAX #15 §9, the quick check). Computed by the
        /// generator from the files.
        /// </summary>
        internal const int StableBelow = {stable};

        /// <summary>Full_Composition_Exclusion's size: the listed code points, singletons, non-starter decompositions.</summary>
        internal const int ExcludedCount = {excluded_count};

        /// <summary>
        /// sha256 of one <c>XXXX\\n</c> line per Full_Composition_Exclusion code point, ascending (uppercase hex, at
        /// least four digits), derived by the generator from the files.
        /// </summary>
        internal const string ExcludedDigest = "{excluded_digest}";

        /// <summary>How many primary composites there are (two-part canonical decompositions not excluded).</summary>
        internal const int CompositeCount = {composite_count};

        /// <summary>
        /// sha256 of one <c>XXXX XXXX;YYYY\\n</c> line per primary composite (its two parts, then itself), ascending by
        /// the composite, derived by the generator from the files.
        /// </summary>
        internal const string CompositeDigest = "{composite_digest}";

        /// <summary>The {class_count} code points with a non-zero canonical combining class (field 3), ascending.</summary>
        internal static readonly int[] ClassCodePoints =
        {{
{class_cps}
        }};

        /// <summary>The class of each code point in <see cref="ClassCodePoints"/>.</summary>
        internal static readonly byte[] Classes =
        {{
{class_values}
        }};

        /// <summary>The {map_count} code points with a canonical decomposition (field 5, no tag), ascending.</summary>
        internal static readonly int[] DecompositionCodePoints =
        {{
{map_cps}
        }};

        /// <summary>The first part of each decomposition in <see cref="DecompositionCodePoints"/>.</summary>
        internal static readonly int[] DecompositionFirst =
        {{
{map_first}
        }};

        /// <summary>The second part of each decomposition, or -1 for a singleton (a decomposition of one).</summary>
        internal static readonly int[] DecompositionSecond =
        {{
{map_second}
        }};

        /// <summary>The {listed_count} code points CompositionExclusions.txt lists uncommented, ascending.</summary>
        internal static readonly int[] ListedExclusions =
        {{
{listed}
        }};
    }}
}}
'''


def main():
    a = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    a.add_argument("--dir", required=True,
                   help="the folder holding %s and %s, the copies the conformance fixture set vendors"
                        % (UNICODE_DATA, EXCLUSIONS))
    a.add_argument("--check", action="store_true", help="compare with the committed tables, write nothing")
    args = a.parse_args()

    classes, mappings = parse_unicode_data(read_pinned(args.dir, UNICODE_DATA, UNICODE_DATA_SHA256))
    listed = parse_exclusions(read_pinned(args.dir, EXCLUSIONS, EXCLUSIONS_SHA256))
    excluded, composites = derive(classes, mappings, listed)
    source = render(classes, mappings, listed, excluded, composites)
    summary = "Unicode %s, %d classes, %d decompositions, %d listed exclusions (%d in all), %d composites" % (
        VERSION, len(classes), len(mappings), len(listed), len(excluded), len(composites))

    if args.check:
        with open(OUT, encoding="utf-8", newline="") as f:
            same = f.read() == source
        print("%s: %s (%s)" % (os.path.basename(OUT), "matches" if same else "DIFFERS", summary))
        sys.exit(0 if same else 1)

    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write(source)
    print("wrote %s: %s" % (OUT, summary))


if __name__ == "__main__":
    main()
