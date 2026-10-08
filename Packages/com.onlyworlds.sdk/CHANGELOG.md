# Changelog

All notable changes to the OnlyWorlds Unity SDK. Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); this project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- **The World Browser no longer demands a PIN to connect.** Prefixed keys read without one, and a legacy key needs one
  only for writes or a private world, so the demo keys that worked in code were locked out of the browser. The
  settings window now says when a PIN is needed for each kind of key.

### Changed

- **The READMEs open with a first run that needs no account**: the demo key `0000000001` reads Moppetopia. Agent seats
  lead for writes. The account token is no longer offered to the client.

## [0.3.0] - 2026-09-29

The 22 models generated from the pinned schema, a world-folder writer that matches the reference
implementation byte for byte, and the write path brought in line with two wire rulings. Verified
against a 207-test EditMode suite and every file of a 12,307-file real world.

### Added

- **All 22 element models, generated.** `codegen/generate_models.py` emits one model per element
  type plus the `OWElementTypes` registry from the vendored, hash-checked schema distribution, and
  the three hand-written proving models (Character, Pin, Marker) are gone. The generator imports the
  distribution's own walk rather than re-reading the YAML, refuses to emit on a schema construct it
  does not recognise, and `codegen/check_drift.py` fails when a committed generated file differs
  from a fresh generation. `OWMarkerOrdering` stays hand-written, outside the generated tree.
- **`OWElementTypes.StringFieldNames`, `BaseStringFieldNames`, `IsStringField`** — which keys are
  scalar strings, per type, generated beside `FieldNames`. The write path uses them (below); a
  caller building JSON by hand can too.
- **`OWElement.CreatedBy`** — keel's `created_by` (the membership that created the element on a
  shared world, or null), now on every v2 body and in every exported folder. Read-only, like
  `CreatedAt`; it is server-managed and not part of the standard.
- **`OWFolderWriter`** — write a world folder back to disk in the bytes the format specifies
  (spec v0.3.6), the companion to `OWFolderReader`. `WriteElement`, `Write`, `DeleteElement` and
  `WriteWorld`, plus the filename derivation (`ElementFilename`, `Slugify`, `IdTail`) as public API,
  because a caller that needs to know where an element will land should not re-derive the rule.
  The serialization is pinned rather than defaulted at five points — LF, UTF-8 with no BOM,
  two-space indent with one trailing newline, keys in the order they arrived, and numbers spelled as
  `JSON.stringify` spells them (below). Verified against all 12,307 files of a real world: the writer
  independently derives the filename each element file already has, and every file round-trips
  byte-identical except the one change the numbers ruling makes.
- **`OWFolderWriter.Parse` / `ReadJsonFile`** — parse JSON without letting the parser rewrite the
  values. Use these, not `JObject.Parse`, for anything destined to be written back.
- **`tools/gate.py`** — the pre-push gate: runs the EditMode suite headless through the Unity CLI and
  refuses a green that ran zero tests, or fewer than `--expect`. The CLI exits 0 with
  `result="Passed"` when a filter matches nothing, so the exit code alone is not a verdict.

### Changed

- **A null scalar string is written as `""`** (the schema distribution's ruling
  `string-empty-is-unset`). For a string, `""` IS the wire's unset: keel stores strings non-null,
  so there is no third state, and a client must not send a distinction the wire cannot carry.
  `CreateAsync`, `PatchAsync` and `BulkAsync` now send `""` wherever a scalar string holds `null`.
  Only keys the schema declares as scalar strings are touched: a single link keeps `null` (its unset;
  `""` is not an id), an integer keeps its three states, an `x_*` value is never reinterpreted, and an
  absent key stays absent (on a PATCH, absent means "leave it alone"). `OWEdit` no longer reports a
  scalar string moving between `null` and `""` as a change, so it cannot send a PATCH that rewrites a
  value onto itself.
- **Scalar string fields start at `""`, not `null`**, on every model. A fresh model now says what
  the wire says, and what Unity's serializer already turned a null string into after a domain
  reload, so the two no longer disagree. Behaviour change: a caller testing `model.Description ==
  null` on a fresh or partially-read model now sees `""`; test with `string.IsNullOrEmpty`.
- **`OWFolderWriter` spells numbers as `JSON.stringify` does** (folder format spec §5, ruled
  2026-09-28). An integral float is `1`, not `1.0`; `1e20` is `100000000000000000000` and `1e21`
  is `1e+21`; `-0` is `0`; and every other number takes the shortest digits that read back as the
  same double, found in exact integer arithmetic because Unity's Mono misrounds both formatting and
  parsing at extreme exponents (checked against 45,024 values, 0 differences). Integer literals,
  strings and timestamps are written exactly as read. A folder written before this takes one diff:
  on the Sikelia world, 196 of 12,307 files, each a single `1.0` becoming `1`.
- **`OWFolderWriter` refuses NaN and infinity** with `OWFolderFormatException`. JSON has no form for
  them; Newtonsoft wrote a quoted `"NaN"` and `JSON.stringify` writes `null`, and both change the
  value.
- **Schema pin moved to `v0.30.1-dist.15`** (from `dist.11`). Two presentation icon slugs changed
  (institution, marker); the schema changed in descriptions only, so the regenerated models differ in
  doc comments.

### Fixed

- **`JObject.Parse` silently rewrote every timestamp on the write path.** Newtonsoft defaults
  `DateParseHandling` to `DateTime`, so a plain parse does not return the string in the file: it
  recognises an ISO-8601 timestamp, converts it to the machine's **local timezone**, and
  re-serializes it there. Measured on the real world, `"2026-09-04T20:36:14.605251+00:00"` came back
  as `"2026-09-04T22:36:14.605251+02:00"` in every file that had a timestamp — the same instant,
  still-valid JSON, and a whole-repository diff whose content depends on which machine ran the
  write. Every value assertion passes while it happens, which is what made it worth finding in
  bytes.
- **The same trap closed everywhere the SDK parses JSON it may hand back.** `OWJson.ParseObject`
  is the one verbatim parse (`DateParseHandling.None`, `FloatParseHandling.Double`);
  `OWFolderWriter.Parse` delegates to it, and `OWFolderReader`, `OWEdit`'s snapshot and
  `OWClient`'s response envelope now use it. A folder read with the reader and written with the
  writer is byte-identical on any machine, in any timezone. Behaviour change for readers of
  `OWFolderElement.Body`: timestamp values are now `JTokenType.String`, never `Date`. Values
  are unchanged; only their token type is, and only for callers that inspected it.

## [0.2.0] - 2026-07-29

Reading worlds from disk, writing them back safely, and a guard on the vendored schema. Verified
against a 142-test EditMode suite and, for the first time, a real stripped IL2CPP build.

### Added

- **`OWFolderReader` / `OWFolderLoader`** — read an OnlyWorlds world folder from disk (spec v0.3.5)
  and load it into a cache keyed `(Folder, worldId, folderPath)`. **Reading is not editing**: no
  directories, dot-folders or marker files are created, and no file is ever rewritten — including to
  normalise a key spelling. Files that cannot be used are reported, never deleted; one malformed
  file never costs you the rest of the world. Verified against a second implementation's
  conformance fixture.
- **`OWBrowserWindow` → Open Folder…** — the reader, reachable.
- **`OWEdit`** — typed edits that PATCH only what changed. Diffs a baseline snapshot, so it needs
  nothing from the model and works for generated types that do not exist yet. Sends nothing when
  nothing changed, because a no-op PATCH still bumps `updated_at` and shows up as a phantom edit in
  everyone else's change feed.
- **`OWClient.BulkAsync`** with `OWBulkResult` — bulk writes whose partial failures are hard to
  ignore. **A bulk request returns HTTP 200 even when individual slots failed**, so the per-slot
  status is the only place the truth lives.
- **`OWSchemaPin` + drift guard** — the vendored presentation sidecar is hash-checked against the
  pinned distribution on every test run. The pin is `(tag, manifest hash)`, never the tag alone: a
  tag is mutable, and moving one regenerates a self-consistent manifest that cannot detect its own
  tag having moved.

### Fixed

- **Extension fields survived JSON but not Unity's own serializer.** `OWElement` did not implement
  `ISerializationCallbackReceiver`, so the methods carrying the bag across a Unity serialization
  pass were never called. An element held in a `MonoBehaviour` or `ScriptableObject` field lost
  every `x_*` field.
- **Every field was serialized twice** — `name` *and* `Name`. Newtonsoft defaults to OptOut, so
  public properties went to the wire beside their attributed fields. The server ignores the
  PascalCase copies, which is why it survived a day of green tests.
- **Incremental sync could advance its cursor past changes it never applied**, if the changes feed
  reported more pages but gave no cursor to reach them — silently losing them forever.
- **The rewind diagnostic reported the wrong cursor**, rendering "Cursor 100 was ahead of server
  head 100". The value it existed to report was overwritten before the message was built.
- **`OWMainThread.Run` queued work into silence** when no pump existed: the task never completed and
  never faulted, hanging the caller with an empty console. It throws now, and says what to do.
- **`OWWorldCache.Upsert` left an element in its old type bucket** when its type changed — reachable
  by the folder reader on the first file whose body contradicts its directory.
- `HasChangesAsync`'s two-directional truth is documented rather than surprising.

### Validated

- **The nullable converter survives IL2CPP stripping.** Against a real stripped Android build: five
  closed `SerializableNullable<T>` instantiations, three closed converter instantiations, the
  factory reflectable in `global-metadata.dat`, and `ReadJson`/`WriteJson` as executable methods.
  Runtime behaviour on device is still unproven — the probe's verdict only prints on hardware.

## [0.1.0] - 2026-07-28

First cut. Everything below is verified against the live v2 API and an 85-test EditMode suite.

### Added

**Core**
- `SerializableNullable<T>` — a Unity-serializable nullable keeping unset, deliberate-zero and
  absent distinct. Implicit conversion *from* `T` only; reading requires acknowledging the maybe.
- Custom PropertyDrawer rendering unset as `--`, never `0`. Works for any `T : struct`.
- `OWJson` — one serializer configuration. `NullValueHandling` and `DefaultValueHandling` both
  pinned to `Include`; unset writes as explicit `null`, never omission, never `0`.
- Converter refuses silent coercion — `2.7` does not become `3`, `"0"` does not become `0`.

**Bridge**
- `OWClient` over an `IOWTransport` seam, with `UnityWebRequestTransport` as the default.
- Auth shape from the key prefix: `ow_a_` uses Bearer, others use `API-Key`/`API-Pin`. Read keys
  (`ow_r_`) send no PIN.
- Five server-owned fields stripped from every write. Blacklist, never a whitelist — a whitelist
  would silently destroy other tools' `x_*` state.
- Client-minted UUIDs plus `Idempotency-Key`, so a retry cannot duplicate.
- `EditLinksAsync` for atomic relationship edits.
- Cursor pagination driven by `has_more` with a guard against a cursor-less "more" response.
- Typed `OWApiError` carrying `status`/`code`/`param`/`docUrl`, distinct from `OWTransportError`.
  Survives a non-JSON gateway body.
- `OWMainThread` — marshals requests onto Unity's main thread, so a paged call issued after an
  `await` still lands correctly.

**Cache**
- `OWWorldCache`, a ScriptableObject world keyed by `(source, worldId)` — never world id alone.
- Elements stored as raw JSON: extension fields survive by construction, and the cache stays valid
  across a model regeneration.
- id→index dictionary, so link resolution is a lookup rather than a scan.
- `OWSync` — baseline (walks all 22 types) and incremental (`/changes`) cache population. The tip is
  read *before* the walk, or changes landing mid-walk sit below the cursor forever. A cursor found
  ahead of the server's head means a restore-from-backup, so the cache re-baselines rather than
  trusting it. Frozen snapshots (`writable: false`) are refused before any network call.
- `OWCacheAsset` — cache assets on disk, named from `(source, worldId)`.

**Viewer**
- Three-panel world browser (**Window → OnlyWorlds → World Browser**) with per-page load progress.
- Sync button and Offline toggle, wired to `OWSync` and the cache.
- Settings window storing credentials in EditorPrefs, masked by default, never in the project.

**Samples**
- `Samples~/QuickStart`, surfaced through the Package Manager.

**Models**
- `OWElement` base with an automatic extension-field bag.
- `OWCharacter`, `OWPin`, `OWMarker` — hand-written proving models covering the worst nullable case,
  the only generic-link, and the ordering convention. **Expected to be replaced by generated
  models**; do not build on their hand-written-ness.
- `OWMarkerOrdering` — explicit `order` first, else `created_at`.

### Known limitations

- Models are hand-written and cover 3 of 22 element types.
- The extension bag does not survive **Unity's own** serializer — an `OWElement` held in a
  `MonoBehaviour` or `ScriptableObject` field loses its `x_*` fields. The JSON path and the cache
  are unaffected. Being fixed.
- No folder-world reader.
- No bulk operations.
- No write path in the viewer — it is read-only by design so far.
- The vendored `ow-presentation.json` has no automated drift guard against the published schema
  dist; re-vendoring is manual today.
- IL2CPP stripping of the generic converter is unverified on a real stripped build.
