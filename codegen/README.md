# codegen — the C# model emitter

Turns the OnlyWorlds schema distribution into the typed models under
`Packages/com.onlyworlds.sdk/Runtime/Models/Generated/`. Python 3, stdlib plus
PyYAML (the walk's one dependency).

```
python codegen/generate_models.py            # emit the 22 models + the registry
python codegen/generate_models.py --check    # diff only, exit 1 on drift
python codegen/check_drift.py                # verify the dist, THEN diff
```

## What is here

| Path | What it is |
|---|---|
| `generate_models.py` | The emitter. Reads the walk, applies the rulings, writes C#. |
| `check_drift.py` | CI guard: hashes the vendored dist, then re-generates and diffs. |
| `schema-dist/` | The vendored distribution, verbatim. **Never edit anything in it.** |

`schema-dist/` is vendored from `github.com/OnlyWorlds/schema-dist` at the tag
this package pins. The pin lives in C#, in `Runtime/Core/OWSchemaPin.cs`, as
`(tag, manifest sha256)` — never the tag alone, because a tag is mutable and a
moved tag regenerates a self-consistent manifest that agrees perfectly about the
wrong content. Recording the manifest hash on our side is what makes a moved tag
visible; that is the `go.sum` pattern.

Every file under `schema-dist/` was verified against `MANIFEST.json` at vendor
time, and `check_drift.py` re-verifies on every run.

## The walk is imported, never re-implemented

`schema-dist/walk/schema_walk.py` is THE decoder of what the schema YAMLs mean.
Ten copies of it were deleted across the ecosystem on 2026-07-28, and the
distribution's own README says: vendor it or port it, but keep its semantics. A
forked decoder is how one standard quietly becomes several.

If the emitter needs something the walk does not return, **ask upstream** — the
opt-in flags (`include_desc`, `include_sections`, `include_required`) were all
added exactly that way. Do not patch the vendored copy.

**The note sink is not decoration.** The walk SKIPS field types it does not
recognise and reports them through a sink that defaults to a no-op, so a
vendored walk pinned at an older dist, meeting a newer schema, drops the new
field in total silence. `generate_models.py` passes a real sink, prints every
note, and **refuses to emit** on any note it does not expect. Two notes are
expected at the pinned dist and both are known schema facts:

- `pin.element` is a `generic-link`, emitted as the `element_type`/`element_id`
  pair.
- `relation.events` is declared in two groups; the walk keeps the first.

## What the ruling table binds

Read the row, not the key. `schema-dist/walk/rulings.yaml` is the authority.

- **`nullable-by-default`** — only `name` is required. Every scalar integer is
  `SerializableNullable<int>`, never `int`. Nothing hard-fails on a `required:`
  list; pin's and marker's are schema intent, not wire enforcement, and are
  being dropped from canonical.
- **`string-empty-is-unset`** — strings are plain `string`. `""` IS the wire's
  unset for strings; there is no third state to model, and inventing one would
  round-trip a distinction the platform cannot carry.
- **`maximum-is-advisory-and-zero-means-unbounded`** — **no range validation is
  generated, ever.** The walk does not surface bounds, and that silence is
  load-bearing rather than a gap. Reading `minimum:`/`maximum:` straight out of
  the YAML here would be re-deriving schema semantics outside the one decoder,
  which the row forbids explicitly. The ordering is canonical, then walk, then
  emitters.
- **`extension-passthrough`** — inherited from `OWElement`'s
  `[JsonExtensionData]` bag. Nothing per type to emit.
- **`unknown-field-types-must-be-surfaced`** — the sink above.

## Regenerating on a schema-pin bump

1. Fetch the new tag from the distribution and read its `VERSION`.
2. Replace `codegen/schema-dist/` wholesale. Do not merge into it.
3. Verify: `python codegen/check_drift.py` reports the dist hashes first. If a
   file fails, **re-vendor — never edit the expected hash until it passes.**
4. Update `Runtime/Core/OWSchemaPin.cs`: `Tag`, `DistSerial`,
   `CanonicalVersion`, `ManifestSha256`, `PinnedOn`.
5. `python codegen/generate_models.py`, then run the EditMode suite in Unity.
6. Read the diff before committing. A pin bump that changes 300 lines of
   whitespace and one field name is a diff nobody reviews.

## CI

`check_drift.py` exits non-zero on any drift and is meant to run on every push.
npm's equivalent existed for months as a script CI never invoked, which is the
same as not having one: a guard that has never fired has not been shown to work.
This one was mutation-tested at build time — a hand edit to a generated file and
a one-byte change to a vendored YAML were each caught.

## Windows note

Generated files are written LF-only, matching the distribution's own
`.gitattributes` normalization. Byte-changing checkouts break hash verification,
and the resulting diagnostic reads as file corruption rather than as a line-ending
problem, which is why it is settled here rather than left to a developer's
`core.autocrlf`.
