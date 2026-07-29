# Changelog

All notable changes to the OnlyWorlds Unity SDK. Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); this project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
