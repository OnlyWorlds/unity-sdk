# Changelog

All notable changes to the OnlyWorlds Unity SDK. Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); this project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Joining a world by link** (keel #212, #353): `OWJoin.ParseLink(text)` finds the `ow_j_` code in a pasted link or
  a bare code; `OWJoin.PreviewAsync(code, transport)` (POST `/join/preview`) says who invites, to which world, for
  which reader, without spending the code; `OWJoin.RedeemAsync(code, transport, name?, agentName?)` (POST `/join`)
  spends it. A player's code answers with a read key (`ow_r_`, no PIN) and the Character it reads as; an agent's with
  a seat's write key and secret. Both send no credential, so they sit beside `OWClient`, which cannot be built
  without a key; npm's `previewJoin` and `redeemJoin` are the same wire. Every bad code is keel's one 404
  `not_found` (`OWApiError.IsNotFound`). The wire check records both calls (12 now).
- **The one-file world export** (`format: onlyworlds-world-export`, spec §8c), the file an owner downloads from a
  world's page: `OWEnvelopeReader.Read(path)` / `Parse(json)` give a folder read's result (`OWEnvelopeReadResult`,
  with the file's `Format`, `SchemaVersion` and `ExportedAt`), and `OWEnvelopeLoader.LoadInto(cache, path)` fills a
  read-only cache. The reader keys on `format` alone: `schema_version` is kept, never checked (the spec and keel
  differ on what it versions). An element with no id is skipped and reported, as in a folder.

## [0.6.0] - 2026-10-17

A world folder that Atlas wrote now opens whole: its pins' and markers' links, and the older `spatial/` layout.
A minor release because it adds public API (below).

### Fixed

- **A world folder's pins and markers keep their links when Atlas spelled them `map_id` and `zone_id`.** The spec
  says a reader accepts both spellings, and Atlas writes the `_id` forms. `OWFolderLoader.LoadInto` gave the cache the
  body as written, so a pin from an Atlas folder had no `Map` and a marker no `Map` or `Zone`. The cache now gets
  `OWFolderReader.WithSchemaLinkNames(body)`: the schema's name, in the old one's place, when the schema's name is not
  already there. The read result's bodies and the files on disk are unchanged.
- **One id, one file (folder spec v0.3.8 §4).** Where an older Atlas folder holds one id in both `elements/` and
  `spatial/`, `OWFolderReader.Read` serves `elements/` and logs the `spatial/` copy in `Skipped` (it used to be loaded
  last, so the older copy won in the cache). `OWFolderWriter.WriteElement` writes a map, pin, zone or marker that
  lives under `spatial/<type>/` there, in its own file, so an edit never leaves a second copy; a new element goes to
  `elements/`. `OWFolderExport` stamps `format_version` `"0.3.8"`, as the Python package does since its 0.5.0.

### Added

- `OWFolderReader.WithSchemaLinkNames(body)`: a body's links under the schema's names (`map_id` as `map`, `zone_id`
  as `zone`), a copy when anything differs; what the cache is given.
- `OWFolderReadResult.TimeCurrent`: `time_current`, else the wire's `time_range_current` (both accepted, as the
  Python package reads them). `OWFolderReadResult.ServerWorldId`: a linked folder's `api.world_id`, else its `id`.
  `WorldId` stays the folder's own id, which keys the cache.
- The reader's notes name the folder spec it follows: v0.3.8's reading rules (they said v0.3.5).

## [0.5.0] - 2026-10-16

### Added

- **World calendars**: `OWCalendar` reads a world's calendar (the `atlas_calendar` field Atlas writes on a calendar
  Construct) and formats, breaks down and parses dates on the world's integer time line: eras, months, weeks, leap and
  intercalary days. It is a port of the npm SDK's calendar kernel (`@onlyworlds/sdk` 4.23.0), checked against the same
  test vectors, so a date reads the same in Unity as in Atlas and Lantern. `OWCalendarLens.WorldCalendars` and
  `ResolveLens` find the calendars a world holds. A missing or malformed calendar reads as null, never an error.
- **The door of every write** (OnlyWorlds keel #299; the npm SDK's 4.26.0 has the same). `Agent = true` on
  `OWClientConfig` sends `X-OW-Agent` on every element write (create, patch, delete, link edit, bulk), never on a read,
  and the server records those writes as agent-made. It is off by default and belongs only in a client an agent
  drives.
- Every element reads the server-managed `ChangedBy` (the membership whose write last changed it) and `ChangedVia` (that
  write's door: `seat`, `mcp`, `declared`, `api`, `v1`, `sync` or `server`). A `/changes` delete reads `DeletedBy` and
  `DeletedVia`. `OWWriteDoor.IsAgent(door)` is true for `seat`, `mcp` and `declared`.
- **`OWApiError.RetryAfter`**: the seconds to wait from the response's `Retry-After` header (a delay, or an HTTP date
  turned into one), or null. The server sends it on 503 `server_busy`, 429 and 409 `idempotency_in_flight`. It is read
  as the npm SDK reads it, and the client still never retries on its own.
- **Export an account world to a world folder**: `OWFolderExport.ExportAsync(client, folder)` reads the world and all 22
  element types (GETs only) and writes the folder the onlyworlds Python package's `export_world` writes, a port of its
  rules: element files byte for byte, the collision rule's id-ascending names, `time_current` on disk,
  `format_version` `"0.3.7"`. On Moppetopia's 310 elements it matched the package's output on the same wire bodies
  with no file different. Each Map's image is kept as Atlas keeps local media (`media/<map id>/base-image/`, as served,
  with its `.meta.json` sidecar); an image that will not fetch is a line in the report, never a failure. The folder
  is built beside its destination and moved into place, so a cancel or an error leaves no half-written world, and it
  must not exist or be empty. `OWFolderExport.WorldForDisk` is the `world.json` step on its own. The key and PIN go
  into no file and no message. On Windows a folder too deep for the files a world needs is refused before the first
  write: Unity's Mono does not take long paths.

### Changed

- **Schema pin moved to `v0.32.0-dist.24`** (from `dist.20`): canonical unchanged. The element `id` is described as a
  UUID, new ids minted as v7 and earlier v4 ids still valid. The generated models changed only in their stamps.
- **New ids are UUIDv7**, as the standard now says and as the server, the npm SDK (4.2.0 on) and the Python package
  mint them: `CreateAsync` and `BulkAsync` mint one with `OWId.NewId()` when an element has no id and no key is
  given, where they used a v4 `Guid`. Ids this client mints a millisecond or more apart sort by creation; a world still
  holds v4 ids, which stay valid, so order elements by `created_at`, never by id. A caller-supplied id is still sent as
  given.
- **Faster folder reads**: `OWFolderReader.Read` (and so `OWFolderLoader.LoadInto`) reads a world's files on up to 8
  threads, then parses them one at a time in the same order as before. On a 20,570-element world it took about 2.0 s
  instead of 3.1 s, with the same elements, order, types and skipped files. Parsing stays on one thread on purpose: under
  Unity's Mono GC a parallel parse measured slower. WebGL players read one file at a time.

### Fixed

- **The folder writer escapes strings as `JSON.stringify` does.** `OWFolderWriter.Serialize` wrote U+2028, U+2029 and
  U+0085 as escapes, in values and in keys, where the reference implementation and the Python package write them
  raw, so a name holding one was a byte difference in every folder. It now matches the package's bytes on a body
  holding them, the short escapes, the other controls and a surrogate pair. A lone surrogate is written as its escape.
- **`OWFolderWriter.Write` settles a collision id-ascending**, as the spec's writer rule requires: a batch is written
  lowest id first, so of two elements whose short filenames match (compared case-insensitively, on every filesystem)
  the lower id keeps the short form whatever order they came in. Into an empty folder it now names files exactly as
  the Python package does. Every body of a batch is checked before the first is written, and a refused batch writes
  nothing. `WriteElement` still never renames an element already in the folder.
- **`ChangesAsync` pages the feed.** It sent its cursor as `cursor=`, and the changes feed takes `since`, refusing any
  other parameter with a 422. So `OWSync.IncrementalAsync` failed whenever a world had changed since its baseline. It
  now sends `since`.
- **A wire check against the API's own document.** `OWWireContractTests` records every request `OWClient` can make and
  checks each against a snapshot of the server's published OpenAPI document: a path it serves, a method it takes
  there, and only query parameters and headers it declares. It catches both bugs above, which had passed their own
  tests since 0.1.0. `tools/refresh_wire.py` refreshes the snapshot (`--check` says whether the server's document has
  moved).
- **`EditLinksAsync` reaches the server.** Since 0.1.0 it posted to `/{type}/{id}/editLinks/`, a route the API never
  served, so every call failed with 404. It now posts `add` and `remove` to `/{type}/{id}/links/{field}`. Its test had
  asserted the wrong URL; it now asserts the server's.
- **The folder writer's round-trip tests run in any host**: they find their fixture through the package's own resolved
  path, so a project that lists the SDK in `testables` and references it by `file:` from outside its own folder runs
  all three instead of skipping them.
- **`CreateAsync` sends `Idempotency-Key` only when you pass one**, as the npm SDK does. Since 0.1.0 it sent the
  element's id as the key on every create. The server keeps a keyed create's first answer for 24 hours and answers an
  identical repeat with it, writing nothing, so re-creating a deleted element under its old id (an undo) got the old
  201 back and the element stayed deleted. A retry without a key is safe when the element carries its own id: it
  answers 409 `id_conflict`, and the element is there. An element without one gets a fresh id on every call, so an
  unkeyed retry of it creates a second element, as in the npm SDK and the Python package; a caller who retries passes
  `idempotencyKey`, and the stored answer is replayed.
- **A keyed retry replays.** With an `idempotencyKey` and no id, `CreateAsync` and `BulkAsync` minted a fresh id on
  every call, so a retry sent a different body and the server answered 409 `idempotency_error` instead of its stored
  answer. The id is now derived from the key and the element's position (`OWId.Keyed`, a UUIDv5 in the Python
  package's namespace), so a retry sends the same body. The Python package and the npm SDK (4.27.1) derive the same
  id, so a key mints one id in all three clients. Without a key, ids are minted as before.
- **`OWSync.IncrementalAsync` resumes from the server's own cursor**, kept as is in the cache's new `CursorToken`,
  where it built one from the last `change_seq`. The server answers a guest key's bare number with 409
  `resync_required`, so a guest key's incremental sync failed on its first real change. A cache synced by an earlier
  version has no token and resumes from its `change_seq`, as before. A 409 `resync_required` now rebuilds the cache
  from the start, with a `RebaselineReason` saying why: a guest is never sent deletes, so only a rebuild drops what it
  can no longer see.
- **The sync reads the feed's `epoch`** (`OWChangesPage.Epoch`, kept as `OWWorldCache.Epoch`). A guest's view can
  change with the head unmoved, so a cache at the head is current only when the epoch is unchanged too, and
  `HasChangesAsync` says so.
- **Each upsert is applied from the element it carries** (`OWChange.Element`), where the sync fetched every changed
  element again: one request per page instead of one per change. If the feed reports more pages but gives no cursor,
  the cache keeps its `change_seq` and the last cursor the server gave, where it recorded the highest `change_seq` it
  had read; a bulk import gives many changes one `change_seq`, so that number could claim changes never read. An
  empty world's cache no longer rebuilds on every sync.
- **`ChangesAsync` sends `limit`**: the client's `PageSize`, at most 1000, above which the feed answers 422. The
  server's default page was 500; the npm SDK asks for 100.
- **`OWApiError.IsRetryable` reads the error code**, where it was any 429 or 5xx. A 409 `idempotency_in_flight` (the
  same key still running: send it again and it replays the first answer) now reads as retryable. A 429
  `quota_exceeded` (a daily allowance, used up until midnight UTC) and a 503 `ask_unavailable` or `media_unavailable`
  (a feature switched off) no longer do.
- **`OWApiError.IsAuthError` reads the error code**, as the npm SDK does: `invalid_credentials`, `key_revoked` (and
  the reserved `world_gone`), where it was any 401 or 403. A 403 such as `not_author` or `owner_only` comes with a
  valid key, and the World Browser no longer tells you to check the key and PIN for one: it says the element is
  someone else's, that only the owner can do this, or that the key's scope does not allow it. `IsNotFound` is a 404
  with the code `not_found`, so a proxy's 404 is not mistaken for a missing element. New: `IsNotAuthor`,
  `IsOwnerOnly`, and `IsResyncRequired`.
- **`OWKey.CanWrite(OWKeyKind.Account)` is false**, as the npm SDK's `kindCanWrite` says: an account key is for the
  server's `/account` routes, where it mints world keys, and writes no world itself.
- **`OWBulkSlot.Succeeded` documents the rolled-back batch**: after an `atomic: true` request with `Errors` true,
  nothing was written, yet the slots that would have succeeded still say 201 with the ids they would have had. Do not
  record those ids as created.

## [0.4.0] - 2026-10-11

### Added

- **List filters**: `OWListParams.Name`, `NameContains`, `Supertype` and `Subtype` send the API's `name`,
  `name__icontains`, `supertype` and `subtype` filters. `ListAllAsync` now carries every filter onto every page.

### Fixed

- **The World Browser no longer demands a PIN to connect.** Prefixed keys read without one, and a legacy key needs one
  only for writes or a private world, so the demo keys that worked in code were locked out of the browser. The
  settings window now says when a PIN is needed for each kind of key.
- **The editor's key, PIN and base URL are kept per project.** They lived in one machine-wide EditorPrefs entry,
  so every project on the machine shared one key, and a key rotated for one world went stale in all the others.
  They are now scoped by the project's `PlayerSettings.productGUID` (clones and worktrees of one project share
  them), still in EditorPrefs, still never in the project. A project with no key of its own offers the
  machine-wide key an earlier version saved ("Use it here" in the settings window); it is never read silently.
- **The World Browser loads the whole world on connect**, in the background, so every type's count and every
  link's name show before a type is clicked; before, a link read as a raw id and a count stayed blank until its
  type was opened. A type clicked during the load still opens at once. Unnamed markers read as their zone and
  order ("<zone> · 3") instead of "(unnamed)", and type names are capitalised.
- **The World Browser shows a folder world or a cache with no key.** A key is needed to reach the API, not to
  read what is already on disk; with none set, the browser showed only the setup prompt, even over an open
  folder world, which needs no account at all.

### Changed

- **Schema pin moved to `v0.32.0-dist.20`** (from `dist.15`, by way of `dist.18` and `dist.19`): canonical
  `00.32.00`. The World gains `length_unit`, `mass_unit` and `distance_unit` (the world units) and `created_at`,
  `updated_at` and `public_read`, and loses `api_key`, `user` and `version` from its definition; `GetWorldAsync`
  returns the World as a `JObject`, so all of it arrives with no code change (`world["length_unit"]`). The element models changed only in
  their doc comments: the unit fields now name the World field they are measured in. In `presentation.json`,
  Zone's icon is now `pentagon` and Marker's `polyline`, and the World Browser draws both.
- **The World Browser reads like an editor list**: the types in alphabetical order, each with its icon in its
  family's colour and its count; selectable rows with a hover state in place of button columns; the element's
  name, type, supertype and subtype as a header; and a link field shows the linked element's name, which opens
  it. A link resolves to any element the window has loaded, which, with the world loaded on connect, is every
  element; an id outside the world stays an id. The family legend is gone: the colours stay, the grouping is not shown. The 22 icons are Google's Material
  Symbols (Apache 2.0, licence in `Editor/Icons/`), rendered by `tools/render_type_icons.py` from the names in
  `ow-presentation.json`.
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
