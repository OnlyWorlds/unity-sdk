# Changelog

All notable changes to the OnlyWorlds Unity SDK. Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); this project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `OWOpenerSurface.Online` (default true; `online` in `OWOpenerSurface.FromJson`, refused by name when not a
  boolean): with no connection, every row whose href is a web address (`http:` or `https:`, in any case, read from the
  row's own filled href) says `OWOpenerState.NeedsAConnection`, "Needs a connection", the site's own row included,
  which had no word before. The words in order, the first that holds: "You are here", then "Needs a connection", then
  the account, browser and phone words, then "Opens the world". A row whose link opens an app on the device keeps its
  own word, and every row keeps its href. The shared fixture moves to 54 cases (sha256 `5a9ebbc8…`), the same bytes as
  the TypeScript SDK 4.118.0's and the Python package 0.96.0's.
- `OWClient.GetMembersAsync` (GET /members): the world's roster, the rows a message reader takes as `members`
  (`OWMessage.SenderStatus`, `ReadMarkOf`), each row's id (null for the owner), kind, role, Character, status (removed
  rows stay), lenses and, where this key is served it, name. A world key reads its own world's, with no `world` and no
  PIN; an account token the world `World` names (`world=<id>`, its Bearer), refused before sending without `World`; a
  keyless client is refused, since the roster has no public reader. Mirrors the TypeScript SDK's `members()` (4.22.0;
  on an account token from 4.116.0).
- `OWClient.HealthAsync` (GET /health): keel's liveness answer, as served. Sent with no credential on every client (no
  key, PIN or Bearer, and no `world`), so an account token and a keyless client call it too. Mirrors the TypeScript
  SDK's `health()` (3.0.0; on a keyless client from 4.30.0).
- `OWClient.UpsertAsync<T>` (PUT /{type}/{id}/): creates the element under the caller's own id, or writes it whole.
  The body is cleaned as every element write's is (the read-only fields left out, a null scalar string sent as `""`),
  sent with the PIN, `X-OW-Client` and, on an agent client, `X-OW-Agent`; a known-of stub is refused before sending,
  and an agent seat's PUT that became a proposal (202) throws `OWProposedError`. Mirrors the TypeScript SDK's
  `upsert()` (3.0.0).
- `OWClient.AdoptAsync<T>` (POST /{type}/{id}/adopt `{member}`): changes who made an element, to a membership id of
  the world or `owner` (`created_by` null); returns the element as written. The owner's key or a co-builder's, with
  its PIN; an element write, so `X-OW-Client` goes with it and `X-OW-Agent` on an agent client. A null or empty member
  is refused before sending. Mirrors the TypeScript SDK's `adopt()` (4.43.0).
- The hidden mark on the client: `OWClient.HideAsync` (PUT /{type}/{id}/hidden, `{}`) and `UnhideAsync` (DELETE
  /{type}/{id}/hidden), the owner's key or a co-builder's with its PIN, a 204; and `ListHiddenAsync` (GET
  /world/hidden), every hidden element newest first as `OWHiddenRow`s (`Type`, `Id`, `SetAt`, `SetBy`, null when the
  owner hid it), a read of the owner's or a co-builder's key. keel's document declares no header on the hide or the
  unhide, so neither `X-OW-Agent` nor `X-OW-Client` goes there, where the TypeScript SDK sends `X-OW-Agent` on an
  agent client. Mirrors the TypeScript SDK's `hide()`, `unhide()` (4.45.0) and `listHidden()` (4.48.0).
- `OWClient.SetMyCharacterAsync` (PUT /me/character `{character}`): a person member picks the roster Character that is
  them in this world, or none (null, sent as `null`); answers GET /me's body. With the PIN, and without `X-OW-Agent`
  or `X-OW-Client`, which keel's document declares on neither (the TypeScript SDK sends `X-OW-Agent` on an agent
  client). An account token and a keyless client are refused before sending. Mirrors the TypeScript SDK's
  `setMyCharacter()` (4.50.0).

### Changed

- Schema pin to `v0.33.0-dist.97`: the address profile's four format cases on an empty string, one per part (an
  empty world, type, id or lens refuses by that part's own reason; only null or a missing key is absent), which
  `OWAddress` already answered so.

## [0.26.0] - 2026-10-30

Serials 88 to 96 in Unity, with the lens and graph readers on every reading the lens profile's owner ruled ahead of
them: a hidden element a guest only knows of is out of that guest's view, a lens whose anchors are empty strings is
a filter, a served body carries the hidden mark as the wire serves it, and a link reaches only an element of its
field's type. New: `OWOpeners.For` and `OWTools.All` (the rows of an "Open with" menu), `OWLinkListener` and an
opt-in Windows forwarder for `ow://` links, `OWListParams.Filter` (list by a single link) and `OWReadingAids` (the
Charter, Guide and Domains as the API serves them). A minor release because it adds public API (below).

### Changed

- Schema pin to `v0.33.0-dist.96`: two graph cases on a guest's own hidden creation (out of an unlensed graph with no
  count and no counted cut edge; a node of a lensed graph that keeps it, marked hidden, its edge to an element the
  lens never reached `outside_lens`), which `OWGraph.Read` already answered so.
- Schema pin to `v0.33.0-dist.95`: five new cases across the charter, lens and message fixtures, and `pairs.json`
  beside `presentation.json` (vendored with the rest; no reader for it here yet). `OWMessage.ReadMarkOf` takes
  `keyless`, as `IsToEveryone` does: a mark served without a credential is ignored, since the keyless public reader
  is served no `created_by` or `changed_by` on any body.
- Hidden beats `known_of`: a hidden element named in a guest's `known_of` that is not its own creation is not in that
  guest's view (`OWLens.Resolve`, `OWGraph.Read`, and the owner reading as that guest): no stub, no row, no node, no
  count and no mark; a link to it is left out of the guest's served lists, and the owner reading as the guest counts
  an edge to it `not_visible` and an anchor on it `anchor_not_visible`. A hidden id in a guest's `whole` stays its own
  creation, served whole and marked.
- Schema pin to `v0.33.0-dist.94`: the charter, lens and names fixtures' twenty-four new cases. `OWLens.Resolve` reads
  an anchor list's empty or White_Space-only string as no id, so a lens holding only such strings is a filter; a
  served body carries `hidden: true` where the hidden mark keeps the element (the owner's body, and below the owner
  a guest's own creation, never a stub), and no `hidden` key otherwise, a stored `false` dropped. `OWCharter`'s Domain
  hubs leave out an element the reader does not see as the field's type, so a proposed Object is no hub, the owner's
  view included.
- A single link stored as `""` is served as stored to every reader below the owner, in `OWLens.Resolve`'s rows, its
  mail rows and the bodies a graph reads: `""`, never null, and never named in `x_served_trimmed`, since no id was
  left out, so a Message whose `narrator` is `""` no longer claims a hidden sender.
- `OWLens.Resolve` (and `OWGraph.Read` with a lens) follows the lens profile owner's rulings on readings no fixture
  case decided yet, ahead of the fixture's next serial, which carries a case for each: a link reaches only an element
  of its field's target type, and a Pin's `element_type`/`element_id` pair only when its type is one of the 22 slugs
  exactly as stored, nothing trimmed or folded, so a stored `"Character"` links nothing and is served to a reader below
  the owner as an unset pair (rulings 1, 2); an anchor id no element of its field's type holds, a lens's own id under
  another type's field among them, is counted for the owner as `anchor_not_visible`, never skipped (3); a known-of
  stub keeps the base fields (`change_seq` too) and its type's public fields, a public link as the reader is served
  it, every other field at its unset value and no extension key (7); the mail part keeps two messages with one
  `created_at` in the order of the world's list, as the message profile rules, never by id (11, as the owner
  reversed it); a whole float in an `x_lens` key reads whole (`depth: 2.0` is 2), and only a fraction is refused
  (13). Rulings 4 and 5 (an umbrella's hop keeps a child it reaches before entering
  it; a cut lens id stays counted) were already this reader's. Each change is held by a test named for its ruling.
- `OWGraph.Read` follows the owner's rulings on the graph section's silences, ahead of the same serial: a Pin's
  generic pair is placement, never an edge (G2); a node's `name` is the stored value, null when absent, in `ToJson`
  (`OWGraphNode.Name` stays the string, or null) (G3); `OWGraphNode.Type` is as stored, never folded (G4); an edge's
  `Index` is the target's place in the link list as served to that reader, so a contributor's counts a link to a
  proposed element (G5). An edge to an anchor the reader cannot see already carried the lens's `anchor_not_visible`
  (G1). Ruling 1 reaches the edge list too: a schema link to an element of another type than its field's target (a
  Character's `friends` naming a Location) names nothing, a dangling link, neither an edge nor counted in `EdgesCut`.
- `OWCharter.CharterOf` reads the Charter's name, rules and skill text from its body whole, as the platform does, so a
  reader holding the Charter only as a known-of stub still gets its rules; and a skill id matches an Object whatever
  the case of either id, as the platform compares UUIDs (an Object stored with an uppercase id was dropped).
- Schema pin to `v0.33.0-dist.92`: a Zone leaves map space in the lens profile's graph. Zones are nodes by default,
  for a public reader too; Map, Pin and Marker stay out until `MapSpace` (`OWGraph.MapSpaceTypes`). The lens fixture
  in `Tests/Editor/Fixtures/lens/` moves from dist.88 to dist.92 byte for byte with it; seven of its graph cases were red
  on the old table and pass now.
- Schema pin to `v0.33.0-dist.93`: the lens fixture carries a case for each of the rulings above, nineteen new cases
  over a ninth world (171 in all: 86 resolve, 45 graph, 5 uses), and `profiles/lens.md` states each reading. The keys
  case `keys-an-integral-float-is-refused-where-the-text-shows` is retired for `keys-an-integral-float-reads-whole`.
  The fixture copy in `Tests/Editor/Fixtures/lens/` moves to dist.93 byte for byte; every case passes.
- The schema pin moves to dist.91 (`v0.33.0-dist.91`), and folders declare format 0.3.21 (`OWFolderExport.FormatVersion`).
  The folder lint now lists `doors`, the door history the API serves to a world's members, among the server-managed
  keys an element file may carry: a member's snapshot keeps it as served and never sends it up. The export already
  wrote a served body whole, so it kept `doors` before this change and a test now holds that, on a whole element and
  on a stub that carries it; the reader keeps it in the extensions bag. Nothing else in the schema or the readers
  moved. The folder export's expectations (the `world_for_disk` cases) were re-run against the Python package
  0.88.0: only `format_version` changed.
- The schema pin moves to dist.90 (`v0.33.0-dist.90`). The calendar profile now says in words what the reader
  already did: an intercalary entry on a calendar with no months is not a calendar, whatever its `after_month`, 0
  included. The calendar fixture grows to 76 cases (42 refusals, 209 steps) with one new refusal,
  `refuse-intercalary-no-months-after-month-0`, which `OWCalendar` passes unchanged. The lens profile's founder
  sentence (a lens names its founder only where the writer has a Character of their own) concerns writers; this
  SDK writes no lens. The `supertype` and `subtype` descriptions now say "within its type"; the generated models
  carry neither description, so only the serial stamps change.
- The schema pin moves to dist.89 (`v0.33.0-dist.89`), by way of dist.88. dist.88 moved nothing in the schema or
  the readers: the lens fixture gains a graph case whose elements carry an empty review mark, an object and a string,
  both nodes with no proposed mark, which `OWGraph` passes. dist.89 amends the calendar and message profiles (below);
  the calendar fixture grows to 75 cases (41 refusals, 209 steps, one case that finds calendars in a world's
  elements) and the message fixture to 87, and every case passes. Three of its rulings the readers already kept:
  `OWMessage` reads `created_at` as an instant (an offset and a fraction of a second counted) and takes the newest
  Note wherever it stands in the list, and a `format` or `format_intercalary` that is not a string is ignored, so
  the default template prints.
- `GetMeAsync`'s summary says what `name` is: your agent's or your player's name, served on those seats only, as
  the server serves it. Docs only; parity with the TypeScript SDK 4.102.4.

### Added

- `OWReadingAids.Of(answer)`: the world's reading aids as keel serves them, `reading_aids` beside `front_door` in a
  `GET /me` answer (`OWClient.GetMeAsync`): the Charter `{id, name, rules, skill}`, the Guide `{id, name}` and the
  Domains `{id, name, sentence, hubs}`, read as served for the key (keel's reading, not recomputed); null from an
  older keel. An agent's join answer carries the same packet as `OWJoined.ReadingAids`. `ToJson` writes it back
  equal as JSON to what was served. The npm package's `OwReadingAids` on `OwMe` and `OwAgentJoined`.
- `OWOpeners.For(tools, kind, address, surface)`: the rows of an "Open with" menu for a world (`"world"`) or one of
  its elements (`"element"`), a port of the TypeScript SDK's `openersFor` and the Python package's `openers_for`.
  The site's page comes first, then each listed tool with a link for that kind, in the list's order, each an
  `OWOpener` (`Id`, `Name`, `Href`, `State`, the row's word in sentence case or null). The address is an
  `OWAddressReading`, the parts as JSON, or a world's id with a type word and an id; `OWOpenerSurface` says what the
  surface knows (`Audience` required, `SignedIn`, `Browser`, `Phone`, `Tool`). The whole address is checked by the
  writer in its own form (the site's when it has none), so a malformed form, lens or moment refuses (`bad_form`,
  `bad_lens`, `bad_moment`) though neither rides on an href, as the TypeScript SDK 4.110.0 and the Python package
  0.91.0 do. A refused address throws `OWAddressException`; the rest throws `ArgumentException` naming the key.
  Tested by the packages' shared fixture (45 cases, held by its sha256).
- `OWTools.All`: the OnlyWorlds tools list (github.com/OnlyWorlds/tools, `tools.json` format 1) baked in at commit
  `86208c8`, the keys `OWOpeners` reads. The list is vendored at `codegen/tools/tools.json` and pinned in
  `codegen/tools-pin.json`; `codegen/generate_tools.py` writes the table and `--check` (also run by
  `check_drift.py`) fails when the two disagree. A tool listed after a release is offered from the next one.
- `OWTool.FromJson` and `OWOpenerSurface.FromJson` read a tools row and a surface as JSON, refusing a wrong key or
  type by name.
- `OWListParams.Filter`: list by any single link of the type, by field name, as the API now filters:
  `{"parent_location": id}` lists a place's rooms, and an empty value lists the rows with none. Fields go out in
  ordinal order after the named filters; a key that is not a field name (lowercase letters, digits, `_`) throws
  `ArgumentException` before anything is sent. The wire fixture follows the API document that declares them.
- `OWLinkListener`: a listener on 127.0.0.1, never another interface, for an address link clicked outside Unity.
  One connection carries one line, the link, read whole by `OWAddress.TryParseLink`; it answers `ok` or
  `refused <reason>` (the address profile's own word, or `too_long` past 4096 bytes, `no_line`, `no_receiver`) and
  hands an accepted `OWAddressReading` to your callback through `OWMainThread.Run`, or a dispatch you pass.
  `TryStart` reports a taken port as `port_taken` rather than throwing, so two Editors can run side by side; a
  domain unload stops every listener. `OWLinkListener.TrySend(port, link, timeout)` is the sender.
- `Tools~/ow-link`: an opt-in forwarder for Windows that the system runs for `ow://` links. It offers the link to an
  ordered list of ports from its config (a running Editor, then a running player) and starts the configured player
  with the link when none takes it. `install.ps1` registers it for the current user only (`HKCU`, no administrator)
  and `install.ps1 -Uninstall` removes it. Nothing is registered unless you run the installer.


### Fixed

- `OWGraph.Read` on bodies already served (`OWLensReader.Served`) places an edge's `Index` at the target's place in
  the list as given. A contributor's served list keeps a link to a proposed element whose body it is not served, and
  the reader counted only the bodies it held, so a later target's index came out one short of the contributor's own
  graph of the same world (ruling G5; dist.93's case `a contributor's index counts a proposed id its served list keeps`
  read through the served view).
- `OWCalendar.ReadConfig` refuses a calendar whose `grain` is empty or white space alone: the profile's grain is a
  string with something left after trimming Unicode's White_Space (the room profile's trim, as the marks read it),
  so the field reads as no calendar and a tool shows the bare step. Before, such a grain read as a calendar.
- `OWCalendar.ReadConfig` refuses a calendar with an intercalary day whose `after_month` names a month the year does
  not have: past the last month or below 0 (the profile, serial 89), and any intercalary day at all in a calendar
  with no months, `after_month` 0 included, since there is no place between months (ruled for the next serial). Such
  a day has no position. Before, it read as a calendar: the day counted in the year's length but sat in no month,
  and with no months it was ignored, or, with `every_years`, added a leap step.
- `OWCalendar.ReadConfig` refuses an era whose `start`, `end` or `year_base` is a whole number past the ones a
  double holds exactly (beyond 2^53 - 1 either way), as the TypeScript reader does. Before, anything within a
  `long` read.

## [0.25.0] - 2026-10-29

Serials 86 and 87 in Unity: the presentation sidecar names each type's colour group by its colour word, an empty
review mark is settled in every reader, and a colour word the sidecar cannot draw fails loudly with
`OWPresentationException` instead of drawing a default. `OWApiError.IsReviewMarkEmpty` names keel's refusal of an
empty mark. A minor release because it adds public API, and because `ColorOfFamily` now throws for a word outside
the order where it answered null (below).

### Changed

- The schema pin moves to dist.86 (`v0.33.0-dist.86`). The presentation sidecar names each type's colour group by
  its colour: `OWPresentation.FamilyOf` answers `blue`, `green`, `pink` or `amber` where it answered `agents`,
  `world`, `abstract` or `temporal`, and `FamilyOrder` lists the four colour words. Construct moves to pink and
  Ability to amber; the colours themselves are unchanged. A graph node's `Family` follows, as the lens profile's
  fixture now asserts. Code that compared a family to one of the old words compares to the colour word instead.
- The schema pin moves to dist.87 (`v0.33.0-dist.87`). No schema file moved; the review and lens profiles and the
  presentation sidecar's `_meta.groups` line say how a reader reads an empty review mark and an unknown colour word
  (below). The lens fixture gains a graph case holding one element of each of the 22 types, each node with its colour
  word and icon, which `OWGraph` passes.
- An empty review mark is settled (the review profile's rule 1 at dist.87: the mark is read by presence, as the room
  and charter profiles read theirs). `OWPayload.IsProposed` and `OWElement.IsProposed` no longer count `{}` as a
  mark, and `OWPayload.IsProposedOrInDoubt` and `OWElement.IsProposedOrInDoubt` no longer count `""`, a string of
  White_Space only, `[]` or `{}` as a doubt. Every reader built on them follows: `OWLens` and `OWGraph` keep an
  element whose mark is empty in every reader's view, and `OWMessage.NoteOf` takes a Note whose mark is empty as a
  Character's line. A number, a boolean, a list with an entry, and a string with anything left after the trim (a byte
  order mark alone included) stay doubts; an object with any key stays a mark. A caller can feel this: an element
  marked `{}` now reads as settled to both. `IsProposed` still answers true only for an object, so a caller that
  casts the mark to `JObject` after it stays safe; a string, a number or a list never counted and still does not.

### Added

- `OWApiError.IsReviewMarkEmpty`: keel refuses a write whose `x_review_proposed` is empty with 422
  `review_mark_empty`. Send the mark as an object, or `null` to clear it.
- `OWPresentationException`: a colour word not in the presentation sidecar's `colors.order` fails loudly, naming the
  word (`Word`) and the type it was given to (`Type`), and never draws a default colour (the lens profile and the
  sidecar's `_meta.groups` line at dist.87). The sidecar's load throws it for a type's word or a colour's key outside
  the order, and every later read of that sidecar (`FamilyOf`, `IconOf`, `ColorFor`, a graph node) throws it again
  until the sidecar is re-vendored and `OWPresentation.Reload` runs. `ColorOfFamily` throws it for a word outside the
  order, where it answered null; a null word still answers null, and `ColorFor` still draws an unknown type grey.
  The load throws it too for a word in the order whose colour is missing (no `colors.families` entry, or no `light`
  or `dark`) or is not a `#rrggbb` hex, naming the word, the hex and the types drawn in it, where a known type was
  drawn grey.

## [0.24.0] - 2026-10-29

Three of the schema's reading profiles in Unity: `OWAddress` (an element's address and the `ow://` links that name it),
`OWLens` with its graph reader `OWGraph` (a saved way of reading a world, served as keel serves each reader), and
`OWCharter` (a world's Charter, Guide and Domains). The server's lens routes, an account token that reads the world it
names, and the link target of every link field. The pin moves to dist.85 and the folder check to format 0.3.20. A minor
release because it adds public API (below).

### Added

- **`OWAddress`, the address profile** (schema-dist `profiles/address.md`, serial 77, ruled 2026-10-26): one element
  named by one triple, the world's id, the type word and the element's id, in three spellings. The human address
  `https://www.onlyworlds.com/w/<world id>/<type>/<id>`, `ow://<type>/<id>` inside a world and
  `ow://<world id>/<type>/<id>` across worlds; a world alone is `https://www.onlyworlds.com/w/<world id>` or
  `ow://<world id>`. Any of them may carry a moment (`@<step>`, a whole number in the world's time line, at the end of
  the last path segment) and a lens (`?lens=<id>`), the moment first. `OWAddress.ParseLink(text)` reads a string to an
  `OWAddressReading` (`Form`, `World`, `WorldIsSlug`, `Type`, `Id`, `Lens`, `Moment`, `Canonical`, `IsWorld`), or null;
  `TryParseLink(text, out reading, out refusal)` also says why a string is refused, by the profile's own words
  (`OWAddressRefusal`: `not_an_address`, `uppercase`, `segments`, `bad_world`, `unknown_type`, `bad_id`, `bad_moment`,
  `bad_lens`), checked in the profile's order so every reader refuses a string for the same reason.
  `OWAddress.ElementAddress(form, world, type, id, lens, moment)` writes the canonical string (lowercase, the world's
  id and never its slug, nothing else) and throws `OWAddressException` with the `Reason` (also `no_world` and
  `bad_form`); overloads take a reading, or the parts as JSON in the shape the TypeScript SDK and the Python package
  take (`elementAddress`, `parseOwLink`). Only `https` on `www.onlyworlds.com` is the human form: `http`, the bare host
  and any other host are not addresses. A field that holds an address is public text: never resolve it on a server.
  The type words are the generated list. The profile's fixture, 133 cases, is the test.
- **`OWLens`, the lens profile** (schema-dist `profiles/lens.md`, serial 78, ruled 2026-10-26): a saved way of reading
  a world, a Construct with supertype `Lens` whose links are its anchors and whose `x_lens` recipe says what to read
  (`depth`, `types`, `window`, `from`, `to`, `undated`, `show`, `budget`, `mail`, `notes`).
  `OWLens.Resolve(world, lens, reader, options)` is the profile's `resolveLens(world, lens, reader)`: one pure function
  over a world snapshot that returns an `OWLensResult` (`Kept`, the ids in the budget's order; `Rows`, as `show` makes
  them; `Cut`, the cut counted by reason; `NotesIgnored`; `Mail`, the mail part; `RefusedKey` for an unreadable
  recipe), and never writes. An overload takes the lens's id in the world. The reader is an `OWLensReader`
  (`Owner`, `Contributor`, `Guest(whole, knownOf)`, `Public`, `Served` for a key's own served bodies, and
  `WithOwnerCounts()` for the owner reading as another reader); `OWLensOptions` carries the reading's `AsOf`, the
  world's `Now`, the mail rule's `Clock` and the members list. The ten resolution rules as the profile writes them:
  visibility before resolution (hidden, proposed, room elements for the public reader), a hop both ways, a Relation
  as an edge to its own parties in the same hop, the umbrella's children each once, a hop that never crosses a room
  element, the budget's order (anchors in link order, then by type in the schema's order, newest first, then by id),
  an anchor the reader cannot see skipped and counted only for the owner, a filter lens and a template, the `as_of`
  fence on each element's own date (never a stub's missing one), and the mail rule read through `OWMessage` (unread
  by the reader's own read mark, the sender verified, thirty days, the newest kept). The cut reasons are one closed
  list, `OWLensCut` (`budget`, `types`, `window`, `undated`, `after_as_of`, `anchor_not_visible`, `not_visible`,
  `hops_not_served`); a reader that is not the owner never carries `anchor_not_visible` or `not_visible`, not even as
  zero. Beside it `IsLens`,
  `IsTemplate`, `CheckKeys(xLens)` (the first key refused, or null) and `LensesFor(membership, characterUses)` (the
  membership's choice wins, an empty choice is a choice). The hop reads the generated models' link fields in the
  schema's order. The profile's fixture, 84 cases, is the test.
- **`OWGraph`, the lens profile's graph reader** (schema-dist `profiles/lens.md`, serial 81, the graph section):
  `OWGraph.Read(world, reader, options)` is the profile's `worldGraph(world, reader, options)`, one pure function over
  the same world and the same `OWLensReader` as `OWLens.Resolve`, that reads the world as nodes and edges for that
  reader and returns an `OWWorldGraph` (`Nodes`, `Edges`, `EdgesCut`, `Cut`, `RefusedKey` for the profile's
  `unreadable`, and `ToJson()` in the profile's words); nothing is stored or written. A node (`OWGraphNode`) is the
  element's `Id`, `Type`, `Name`, and its type's `Family` and `Icon` from the presentation sidecar, with `Marks`
  (`room`, `hidden`, `proposed`) on an element a flag brought back and `KnownOf` on a guest's known-of stub, which has
  no edges out. An edge (`OWGraphEdge`) is one `Source`, `Field`, `Target` and `Index`, one per element, field and
  target, from the schema's link fields and the two declared extension links (`x_lens_leads_to`,
  `x_spine_depends_on`); `Index` is the target's place in the list as the reader is served it, and a link held on both
  ends is two edges. Nodes sort by id and edges by source, field and index, each by code point. Without a lens the
  nodes are every element the reader sees less room elements, hidden ones, proposed ones and map space (Map, Pin,
  Marker, Zone); `OWGraphOptions.Rooms`, `Hidden`, `Proposed` and `MapSpace` bring each back, and `Cut` counts what
  stayed out. With a lens (`Lens`, a body, or `LensId`, one the reader is served whole) the nodes are exactly the
  lens's kept, its cut rides along unchanged, and no flag adds to it; a lens whose `x_lens` cannot be read gives an
  empty graph naming the key. A link to an element that is not a node is counted in `EdgesCut` by its target's reason
  (`OWGraphCut`: the lens's cut reason, which wins over `not_visible`; with a lens, `outside_lens` for one the reader
  sees that the lens neither kept nor cut, counted for every reader; without one, the first of the four kinds not
  brought back; `not_visible` for one the reader does not see), and a reader that is not the owner never carries
  `anchor_not_visible`, `not_visible`, `hidden` or `proposed`; a link to an id not in the world is not counted.
  `Hidden` or `Proposed` from a reader that is not the owner, or any flag from the public reader, throws
  `OWGraphRefusedException` naming the flag (`Flag`) before the world is read; the owner reading as another reader is
  judged by that reader's rung. Parity with the TypeScript SDK 4.93.0, its test vectors and the fixture's graph cases:
  the 34 cases replay with the section's invariants checked on every answer.
- **`OWGraph.DegreeOf(graph)`**: each node's degree in a graph `OWGraph.Read` answered, its edges in or out, each
  counted once for every node it touches (a mirror pair counts 2 at each end, a link to itself once), every node a key
  with 0 included, enumerated in the graph's node order. It is "most linked" as a finder ranks it: the reader's own
  count, since what the reading left out has no edge. A null graph is an `ArgumentNullException`. Parity with the
  TypeScript SDK 4.95.0 (`degreeOf`) and the Python package 0.78.0 (`degree_of`), the TypeScript SDK's test vectors
  ported: its own graph, and every fixture graph case's answer against a direct count of its edges.
- **`OWGraph.OutDegreeOf(graph)`**: each node's out-degree in a graph `OWGraph.Read` answered, the number of distinct
  nodes its own link fields point at: a target listed in two fields counts once, a link to itself does not count, and a
  mirror pair counts 1 at each end. Every node is a key, 0 included, enumerated in the graph's node order. Beside
  `DegreeOf` ("most linked"), it says how far an element reaches out. A null graph is an `ArgumentNullException`.
  Parity with the TypeScript SDK 4.98.0 (`outDegreeOf`) and the Python package 0.80.0 (`out_degree_of`), the
  TypeScript SDK's test vectors ported: its own graph, and every fixture graph case's answer against a direct count of
  distinct targets.
- **`OWLens.DateOf(element)`**: an element's date as the lens profile's date table (`OWLens.DateFields`) reads it,
  the whole number in its type's date field, or null for a timeless type, a body with no known `type` (trimmed and
  folded), a date field that is absent, null or not a whole number, and anything that is not an element body
  (`JObject`). It reads the body it is given, so a known-of stub reads null. A timeline or a horizon can now date
  elements exactly as the lens reader's fence does, from one table. `OWLens.DateFields` is now read-only also through
  a cast. Parity with the TypeScript SDK 4.96.0 (`DATE_FIELD`, `dateOf`) and the Python package 0.79.0 (`DATE_FIELD`,
  `date_of`), the TypeScript SDK's test vectors ported (the table against the fixture's `date_table`, every type dated
  or timeless, each date field an integer field of the generated model).
- **`OWClientConfig.World`: an account token, or no key, reads the world it names.** A client built with an account
  token (`ow_a_`) and `World = <world id>` sends `world=<id>` (escaped, beside the call's own parameters, on every page
  of `ListAllAsync`) with its Bearer and no `API-Key` or `API-Pin` on `ListAsync`, `ListAllAsync`, `GetAsync` and
  `GetWorldAsync`, and the server answers as that account's own access in the world: the owner reads it whole, a
  member as its member key reads, any other account as the public reads (a world not open to the public is a 404). A
  client built with no key and `World` set sends the same four reads with no credential at all, the public reader;
  until now a client always needed a key. `OWClient.World` says which world. The account calls
  (`CreateAccountWorldAsync`) work on the same client as before. Refused before anything is sent: `World` with a world
  key (`ow_w_`, `ow_r_`, a seat's key, a 10-digit key) or empty or white space is an `ArgumentException` when the
  client is built, and a client with no key and no `World` is refused as before; on an account client, every call that
  needs a world key (element writes, bulk, link edits, the change feed, `GetMeAsync`, `PatchWorldAsync`, proposals) is
  an `InvalidOperationException`, where the server answered 401 before; on an account client without `World`, the four
  reads are one too; on a keyless client, everything but the four reads. One rule in the request path decides it, read
  from the request: an `/account` route, or a GET marked as a world read. The server serves search with `world=` too;
  the client has no search call. `OWFolderExport.ExportAsync` exports through such a client: it asks no `/me`, and
  the folder's `world.json` carries no `exported_as` (the slice is unknown). Parity with the TypeScript SDK 4.88.0.
- **The server's lens routes.** `OWClient.SetMyLensesAsync(lensIds)`: `PUT /me/lenses`, the lenses this key's holder
  starts with, lens ids in order (0 to 20, repeats dropped by the server; an empty list is a choice, not an absence),
  sent as `{"lenses": [...]}` with the key's PIN and without `X-OW-Agent` or `X-OW-Client`, which the server's document
  declares on neither; it answers GET /me's body as a `JObject`. Any write key of the world sets its own holder's list;
  the server refuses a read key (403), a lens the key cannot see (404) and a lens that is not one, a template or a
  hidden one (422 `not_a_lens`, `lens_is_template`, `lens_hidden`). A null list is an `ArgumentNullException` before
  anything is sent. `OWClient.WorldLensesAsync(worldId, resolve, readAs)`: `GET /world/{world_id}/lenses`, every lens
  in the world this key can see, each with its recipe echoed, the Titles it is the default for and whether this key
  chose it, answered whole as a `JObject` (`{data, read_as}`); `resolve` (`bool?`, sent as `true` or `false` when
  given) adds each lens's `reading`, and `readAs` (the owner only, with `resolve`; sent as `read_as`, escaped) reads as
  another reader. A read, so no PIN. Both take a world key, and an account token as the next entry says; on a keyless
  client each is an `InvalidOperationException` and nothing is sent. `GetMeAsync`'s answer now carries
  `lenses`, the holder's choice in order, which `OWLens.LensesFor` takes as the membership's. Parity with the
  TypeScript SDK 4.92.0, its test vectors ported.
- **The lens routes on an account token, and a row's lenses.** `WorldLensesAsync(worldId)` on an `ow_a_` token sends
  its Bearer alone, the world in the path and no `world=`, with `World` set or not. `SetMyLensesAsync(lensIds)` on an
  account token sets the account's own list in the world `World` names, sending `world=<id>` and the Bearer, never a
  PIN; an account client without `World` is an `InvalidOperationException` before sending. On a world key both send
  as before. `OWClient.SetMemberLensesAsync(worldId, memberId, lensIds)`:
  `PATCH /account/worlds/{world_id}/members/{member_id}` with `{"lenses": [...]}` alone (never a role, Character or
  sponsor), both ids escaped, `memberId` a membership id or `owner` for the owner's own list; it answers the row as
  served, a `JObject` (`{id, role, kind, lenses}`, and `x_served_trimmed` naming `lenses` when the list keeps a lens
  you cannot see). A member sets its own row, a seat it answers for, or any seat as a co-builder; the owner any seat.
  An account token's call: any other key, or no key, is an `InvalidOperationException` and a null list an
  `ArgumentNullException`, each before anything is sent. The server's codes are those of `SetMyLensesAsync`, and a 422
  when the owner names a person's row. The client's one request rule now reads a third mark beside the `/account`
  routes and the world reads: a route the server's document declares the account token on. Parity with the TypeScript
  SDK 4.94.0, its test vectors ported.
- **The lens profile at serial 82: `changed_within` and the reading aids.** `x_lens` takes `changed_within`, a whole
  number of days from 1: an element a hop or a filter reaches whose `updated_at` is older than that many days before
  the reading's wall clock (`OWLensOptions.Clock`, or `OWGraphOptions.Clock`) is cut and counted under the new reason
  `OWLensCut.Stale` (`stale`), after `window` and before the budget; it never applies to an anchor, and an element with
  no time is never cut. A folder element is read by the newer of its `server_updated_at` and `local_updated_at`
  where either reads as a time, else by its `updated_at`, as the TypeScript SDK 4.98.0 and the Python package 0.80.0
  read it; their test vectors are ported (the exact edge, an offset, a filter, the refused values, the two stamps).
  `CheckKeys` refuses anything else by name, between `undated` and `show`. **`OWRoom.IsCharterElement(element)`**
  tells the charter (a Law with supertype Charter), its guide (a Construct with supertype Guide) and its domains
  (Constructs with supertype Domain) by the pair, trimmed and folded, AND the mark `x_charter_of`
  (`OWRoom.ReadingAidMarkField`, the id of the world the aid belongs to), beside `IsRoomElement`; the pair without the
  mark is lore. The mark counts when it is present and not null, a string only when something is left after trimming
  White_Space: `7` is a mark, `"  "` is not (the room mark `x_room_of` now reads by the same rule; see Changed). Parity
  with the TypeScript SDK 4.98.0 (`isCharterElement`) and the Python package 0.80.0 (`is_charter_element`), their
  test vector ported.
  `OWRoom.ReadingAidPairs` holds the three pairs; read the test rather than keeping a list of them. In
  `OWLens.Resolve` a reading aid a hop reaches is kept, and hopped out of, only when `types` names its pair
  (`law/charter`, `construct/guide`, `construct/domain`, now legal `types` words); otherwise it is cut as `types`, and
  a lens with no `types`, or one naming only `law` or `construct`, keeps none. An anchor that is a reading aid is kept
  and followed like any anchor, and an aid a second hop would reach is counted `hops_not_served` only when `types`
  names its pair, as a room element is. In `OWGraph.Read` the reading aids are out of an unlensed graph, counted under the new
  reason `OWGraphCut.ReadingAid` (`reading_aid`), and no flag brings them back; a lens that names the pair keeps them.
  The fixture's 132 cases are the test.
- **`OWCharter`, the charter profile** (schema-dist `profiles/charter.md`, serial 83, ruled 2026-10-28): a world's
  reading aids, the Charter (the rules here, for people and AIs), the Guide (start here) and the Domains (the places
  in the world), each told by `OWRoom.IsCharterElement` (the pair and the mark `x_charter_of`), and the one packet
  every front door shows first. `OWCharter.CharterOf(world, reader, open, hiddenIds)` is the profile's
  `charterOf(world, reader)`: an `OWReadingAids` with `Charter` (`Id`, `Name`, `Rules`, `Skill`), `Guide` (`Id`,
  `Name`) and `Domains` (each `Id`, `Name`, `Sentence`, `Hubs`), and `ToJson()` in the profile's words, the shape keel
  serves as `reading_aids`. The Charter is the newest marked Charter the reader sees, by `created_at` as an instant
  (none sorts oldest) and then the higher id by code point; the Guide the same; the Domains in the Guide's
  `constructs` order, then by folded name and id. `Rules` is `x_checks_rules` as served (`OWCharter.RulesField`), an
  empty list when none; `Skill` the ids the Charter's text names in any `ow://` form, a site address or bare, each
  once, kept when it is an Object of this world the reader sees, at most five (`OWCharter.SkillMax`); a Domain's
  `Sentence` is its description's first line, trimmed, and its `Hubs` the distinct `(type, id)` its served link fields
  name, in the schema's order (`OWCharter.HubFields`), never itself. `OWCharter.DomainsOf` is the list alone. It reads
  through the lens reader's view and the same `OWLensReader` (owner, contributor, guest, public, served); the public
  reader reads nothing unless `open` is true. Written from the profile, its fixture and keel's reader; the TypeScript
  SDK 4.100.0 (`charterOf`, `domainsOf`) and the Python package 0.82.0 (`charter_of`, `domains_of`) replay the same
  fixture, and two of the TypeScript SDK's own vectors are ported (a Guide known only as a stub orders nothing; the
  owner reading as the public reads a closed world as nothing). The fixture's 22 cases are the test.
- **`OWElementTypes.LinkTargets`** and `OWElementTypes.LinkTarget(slug, field)`: the type each link field points at,
  generated from the schema (the target each model's doc comment already named). The Pin's generic pair is absent: its
  `element_type` names its target.

### Changed

- `OWRoom.IsRoomElement` reads the mark `x_room_of` by presence, the rule `IsCharterElement` reads `x_charter_of` by
  and keel's reader reads both by: any JSON value but null counts, and a string only when something is left after
  trimming White_Space; the value is never compared. So an element of a room pair marked `7` is a room element, one
  marked `"  "` is lore, and a mark of U+001F alone counts (it is not White_Space). It had been read as a non-empty
  string, untrimmed. The two marks go through one matcher, and `OWLens.Resolve` and `OWGraph.Read` ask that test
  everywhere they ask whether an element is a room element (the public reader's view, `types`, a Relation as an
  edge, the hop, `hops_not_served`, the graph's `room` kind). Serial 83's room fixture carries four of the shapes as
  its own cases (28 to 32: a number, a White_Space string, U+001F, a padded id); all five (and an empty string) are
  tests here, through each of those readers.
- The schema pin moves to dist.85 (`v0.33.0-dist.85`). Serial 77 adds the address profile (`profiles/address.md` and
  its 133-case fixture); serial 78 adds the lens profile (`profiles/lens.md` and its 84-case fixture); serial 79 adds
  four Note cases to the message profile's fixture (81 to 85: a Note has no recipients and is never unread even when
  its `characters` names the reader, is never in a thread, and a hidden mark that is not a boolean hides it like
  `true`), which `OWMessage` already answered, and moves the folder check to format 0.3.19; serial 80 adds four lens
  cases (84 to 88: the window's edge inclusive, a reading with no `as_of` and no world `now` whose window cuts nothing,
  notes trimmed with a hidden anchor, an empty-string `x_lens` value refused), which `OWLens` already answered; serial
  81 writes the lens profile's graph section (a reading of a world as nodes and edges for one reader, and
  `x_lens_leads_to`, the one link the profile declares) and adds the fixture's `graph` kind (88 cases to 122: 34 graph
  cases, each with its own small world) and the graph tables (`graph_links`, `graph_flags`, `owner_only_flags`,
  `edges_cut_reasons`, `owner_only_edges_cut_reasons`), and corrects the message profile's `noteOf` sentence (the id of
  the newest own-hand Note, which `OWMessage.NoteOf` already returns); serial 82 adds the lens profile's
  `changed_within` key and its cut reason `stale`, names the charter, its guide and its domains reading aids (by the
  pair and the mark `x_charter_of`, in the lens profile's rule 4 and the room profile's line beside its pairs), reads a
  lens served only as a known-of stub as not there, and adds ten fixture cases (122 to 132: seven resolve cases and
  three graph cases) with the `reading_aid_pairs` and `reading_aid_mark` tables; the address profile gains the line
  on what onlyworlds.com commits to (an id never reused, an address outliving a rename, a deleted element answering like
  a missing one), which asks nothing of `OWAddress`; serial 83 adds the charter profile (`profiles/charter.md` and its
  22-case fixture: the selection of the Charter, the Guide and the Domains, and the packet a front door shows; see
  Added), writes the room mark's presence rule into the room profile with four fixture cases (28 to 32), and adds
  fifteen lens cases (132 to 147: thirteen resolve cases on two new worlds, `reach` and `stubs`, and two keys cases)
  for an umbrella's child read by its own recipe and budget, a sibling lens a child's hop reaches, a stub child and a
  stub Message, nothing hopped out of what the fence cuts, the second hop's count, a note on anchors only, and a
  fraction or an integral float refused in `x_lens` (a `text_only` case, which this SDK passes: `OWJson` keeps `7.0`
  a float); the folder check moves to format 0.3.20; serial 84 adds four lens cases (147 to 151, on a new world,
  `watch`: a lens a hop reaches is served whole to the owner, and to a contributor, a guest and the public reader its
  row leaves out the hidden anchor and every note keyed to what that reader cannot see, with `x_served_trimmed` naming
  the fields as keel writes it) and a charter case (22 to 23: a Guide the reader holds only as a stub is still the
  Guide, `{id, name}`, and orders nothing), all five of which `OWLens` and `OWCharter` already answered, and writes a
  re-key line into the checks, room and spine profiles (an id a re-key moves is rewritten where a key holds it whole)
  and an editor line into the spine profile (`x_spine_` keys shown read-only), none of which asks anything of this
  SDK: it never re-keys an element, and its World Browser shows every field read-only. The fixtures' test copies are re-vendored.
  Serial 85 refreshes four of the standard's description lines (supertype and subtype a classification within the
  type; a Title's `grant_date` and `revoke_date` its own, a holder's span a Relation), which reach the generated
  models' doc comments on `OWTitle`, and rewords the charter and checks profiles; no fixture case changed.
- Folders declare format 0.3.20, two versions past 0.23.0's. Version 0.3.19 rules the served-trimmed mark in a folder
  (§4.4 rule 6): a file keeps `x_served_trimmed` exactly as the server served it, a reader reads a field the mark
  names as trimmed, never as empty, and a writer never sends the mark up. This SDK already kept the mark byte for
  byte on export and on read (`OWMessage.HasUnseenRecipients` reads it), and it has no push from a folder; a test now
  holds the export and the read-back to it. Version 0.3.20 rules the folder's `charter` shortcut in `world.json`
  (§4.6 (a)): it picks among marked Charters only (`x_charter_of`, the charter profile's mark) and never stands in
  for the mark. This SDK writes no `charter` key and its folder reader keeps `world.json` as read; `OWCharter` (see
  Added) selects by the mark alone. Nothing else moved: the Python package's `world_for_disk` (0.82.0), re-run on
  the six world cases the export tests hold, changed only `format_version`.
- `OWAddress.ElementAddress` reads three things as the TypeScript SDK 4.89.1 and the Python package 0.73.1 do, where
  the profile's fixture is silent and the two packages agree. When several parts are wrong, the writer names
  `bad_form` first, then `segments`, then `no_world`, then the reader's order (a type with no id and no world is
  `segments`, no longer `no_world`). A JSON moment with a zero fraction is a whole step (`3.0` writes `@3`), and one
  past 64 bits refuses as `bad_moment` instead of throwing. An empty string is present, not absent: only a JSON null or
  a missing key is absent, so `world: ""` refuses as `bad_world`, `lens: ""` as `bad_lens`, and `type: ""` with
  `id: ""` as `unknown_type`. The JSON overload throws `ArgumentException` for a key the shape does not hold (`wrold`,
  `Type`), as the packages throw a `TypeError`; a reading's `world_is_slug` and `canonical` are keys of it, ignored.
- The lens profile's shared edge cases, the file the TypeScript SDK 4.91.0 and the Python package 0.74.0 both
  hold byte for byte (ten cases: a known-of stub's links, the window's edge, `time_current` read as the now, no
  window without a now, a `to` after `as_of`, the fence on anchors, the members list in the mail rule, a message
  not repeated among the kept, the thirty days' edge, ties by id), now replay here against `OWLens`, the file's
  sha256 pinned to the release.
- A lens a guest knows only as a stub reads as nothing when it is named by its id, in `OWLens.Resolve` and so in
  `OWGraph.Read` (`LensId`), as the server's own reader answers it: a stub serves no links and no recipe, so the
  stored body's anchors are never followed. This SDK already looked a lens id up only among the elements the reader is
  served whole, so nothing moved; the TypeScript SDK 4.93.1's test vector (the Python package 0.77.1 holds the same)
  is ported and now holds it.

### Fixed

- A file the folder reader drops because another file serves its id now names that id in
  `OWSkippedFile.Id` (null for every other skip). Since the reader keeps the first file by code point, the repeat
  left `Elements` with only its path in `Skipped`, so a caller mapping ids to files could not see it, and deleting
  the served file would serve the dropped one.

- `OWLens.Resolve`'s rows are now the bodies keel serves the reader, for every reader below the owner resolving from
  the owner's data (a contributor, a guest, the public, the owner reading as one of them). A lens element a row
  carries had kept its `x_lens.notes` whole, so the notes' keys and sentences told that reader about anchors and
  elements it cannot see: now a note keyed to an element the reader cannot see is left out, an anchor's note goes
  with the anchor, and `x_served_trimmed` names `notes` beside the anchor field. A link to a visible element of
  another type than its field's target is left out, as keel checks each link against its target type. A Pin whose
  target is unseen, or not of its `element_type`, reads the unset pair (`element_type` `""`, `element_id` null); it had
  kept the hidden target's type word. `x_served_trimmed` names only a Message's `characters` and `narrator` (it had
  named every trimmed link field) and a lens's anchor fields, and is never written for the public reader. The owner
  reading as itself is served its bodies as stored: nothing trimmed, a link to a proposed element kept. The mail
  part's rows and the charter readers read the same served bodies. The 20 row-trim cases the
  TypeScript SDK 4.100.0 and the Python package 0.82.0 share, vendored byte for byte, are the test.
- `OWLens.Resolve`'s rows serve a link to a proposed element, and a lens's note keyed to one, to a contributor and a
  guest that sees it, and in a key's served bodies, as the server does: only the public reader's rows leave them out
  (with no mark). A proposed element is still in no reader's view, so it is never a row, never hopped through and never
  counted; only the served bodies changed, which had dropped both for every reader below the owner. And a body that
  stores `x_served_trimmed` serves none of it: the owner's row and the public reader's carry no mark, a contributor's
  and a guest's only what that reading trimmed. The stored mark had been kept, and merged with the reading's own. A
  key's served bodies (`OWLensReader.Served`) keep the server's mark, and a lens body given to `Resolve` is read with
  its mark as before. The row-trim file grew to 28 cases (the Python package 0.82.1's bytes), re-vendored byte for byte.
- `OWLensResult.NotesIgnored` counts as keel's reader does: only the notes keyed to an element that is not an anchor
  and that the reader sees. A note keyed to an anchor the reader cannot see, to an element it cannot see, or to an id
  in no element counts nothing. It had counted every note on an id that was not a visible anchor, so a guest's count
  told it a hidden anchor existed. The shared lens edge file holds this as its 15th case, the same bytes as the
  TypeScript SDK 4.98.0's and the Python package 0.80.0's.
- `OWLens.Resolve` and `OWGraph.Read` read a lens body served only as a known-of stub (`known_of: true`) as not there:
  it keeps and cuts nothing. It had been read as a lens with no anchors, a filter over the reader's whole view. A lens
  named by its id already read as nothing unless the reader is served it whole. The lens profile's rule 1, serial 82.
- `OWLens.Resolve` leaves a lens out of the result it resolves, as the lens profile's rule 3 says: a lens that names
  itself (or a lens already entered) as an anchor no longer keeps itself. Serial 83 rules what "its own result"
  means, and `OWLens.Resolve` reads it as keel's reader, the TypeScript SDK 4.100.0 and the Python package 0.82.0 do:
  the union drops an element only by its id, never because it was entered. A sibling lens a child's hop reaches is
  kept as an element even when the umbrella enters it as a later child (the fixture's
  `umbrella-a-siblings-lens-reached-by-a-hop-is-kept-as-an-element`), and an umbrella's own hop reaches its child
  lens as an element before entering it; either one, cut, is counted under its reason. A sibling entered before the
  hop is never reached.
- `OWLens.Resolve` no longer counts a link to an id that is in no element as `not_visible` in the owner's count: a
  dangling link is not a visibility fact, as the TypeScript SDK and the Python package read it.
- `OWLens.Resolve` skips an anchor id that is in no element without counting it: it had been counted
  `anchor_not_visible` for the owner. Read as the TypeScript SDK and the Python package read it.
- `OWLens.Resolve` serves a message or a lens whose links its reader's view trimmed with `x_served_trimmed` naming
  those fields, as the server marks them and the TypeScript SDK and the Python package serve them: a `full` row and a
  mail row now say a list was cut, and the message profile's readers (who a message is to) read the mark.
- `OWLensOptions.HiddenIds` and `OWGraphOptions.HiddenIds` take the world's own hidden list beside each element's own
  `hidden`, as the TypeScript SDK and the Python package read a world's `hidden` (the lens profile's fixture worlds
  carry one): those ids are out of a contributor's and the public reader's view, and a graph node among them carries
  the `hidden` kind. Before, a world-level list could not be given, and its ids were read as visible.
- `OWLens.Resolve` under `show: first_line` cuts the description at its first line break, CR or LF (it cut at LF
  only, so a `\r\n` line kept its `\r`), and writes `description` only when the body has a string one (it wrote `""`),
  as the TypeScript SDK and the Python package make the row.
- `OWLens.LensesFor` keeps ids that are not null or empty, each once, in the order given, as the TypeScript SDK's
  `lensesInUse` and the Python package's `lenses_in_use` do; it had returned the list as given.
- `OWLens.Resolve` reads an umbrella's child lens as keel's reader reads it (the lens profile's rule 3): each child is
  resolved by its own whole recipe, its own mail rule and `budget` among it, so a child keeps at most its budget and
  the rest is counted `budget`; the union then takes the umbrella's mail rule and budget on top. Rows are built by the
  umbrella's recipe alone: a row kept through a child is shown by the umbrella's `show`, and a child's `notes` never
  reach a row. A message a child's mail rule takes out of its kept list is neither served nor counted under the
  umbrella.
- `OWLens.Resolve` puts a note on a kept anchor's row only, as the lens profile's `notes` row says: a note keyed to a
  kept element that is not one of the lens's anchors (one a hop reached, or one kept through a child) is ignored and
  counted in `notes_ignored`. It had also been written on that element's row.
- `OWLens` serves a known-of stub as the TypeScript SDK and the Python package do: its base keys (`id`, `type`,
  `name`, `description`, `supertype`, `subtype`, `image_url`, `created_at`, `updated_at`) and its type's public fields
  that are not links (the presentation sidecar's `public`: a Character's `physicality`, `height`, `weight`,
  `reputation`), with `known_of: true`. It had kept `change_seq` and dropped the public fields.
- `OWLens` reads an element's date written with no fraction as whole, as the TypeScript SDK and the Python package
  do: a date of `7.0` reads as 7 (it read as undated), in `OWLens.DateOf` and the fence alike. A recipe stays strict,
  as keel's reader reads it: a float in an `x_lens` key (`depth`, `window`, `from`, `to`, `budget`, `changed_within`)
  is refused by name even with no fraction (`2.0`), and the lens is unreadable. A fraction, NaN or a number past 64
  bits is never whole.
- `OWLens.Resolve` reads three rules as the lens profile and both reference readers (the TypeScript SDK and the Python
  package) read them. An element whose `x_review_proposed` holds any value but null, a doubt as well as a mark, is in
  no reader's view (the review profile's rule for a reader that hands a world on); it had left out only an object. An
  entry whose type is not one of the standard's is not in the world; a filter lens had kept it. An element that is not
  a lens resolves to nothing, its `x_lens` unread; it had been read as a lens. The fixture's cases did not reach any of
  the three.
- `OWLens.Resolve` now breaks the kept elements' id tie by Unicode code point, as the lens profile's reference readers
  do (the TypeScript SDK and the Python package). It had compared ids by UTF-16 code unit, which diverges only for ids
  with a character outside the Basic Multilingual Plane (a surrogate pair sorted below U+FFFF, not above it); the hex
  ids a world holds are ASCII and were never affected.
- The folder writers settle a filename collision id-ascending by Unicode code point, as the folder format's tie-break
  and the reference writers do (§3.2: the lowest id keeps the short name), and `OWFolderReadResult.KnownOf` lists its
  ids in the same order. `OWFolderExport` and `OWFolderWriter.Write` had compared ids by UTF-16 code unit, so an id
  with a character above U+FFFF sorted below one with a character in U+E000 to U+FFFF and could take the other's short
  name; ASCII ids, which every id a world mints is, were never affected. One comparer now serves the folder code and
  `OWLens`, whose order is unchanged.
- `OWFolderReader` reads a folder in one order on every platform, and a repeated id serves the same file in every
  reader (folder format §4, clarified in v0.3.19). It visits the element files type by type, then by name, and the
  legacy `spatial/` lists, comparing names by Unicode code point. Before this, `Read` took the file system's listing
  order (case-blind on NTFS, so `a.json` came before `B.json` on Windows), and the in-memory read compared UTF-16 code
  units. Within a layout a repeated id now keeps its first file and lists each later one in `Skipped`. It had served
  every copy, so the cache kept the last one. Only a folder that breaks the one-id-one-file rule is affected.
- `OWFolderWriter.WriteElement` writes a renamed element's new body into the file it holds before it writes the
  new-named file, then removes the old file (folder format §4's writer note, clarified in v0.3.19). It had written the
  new file first, so an interrupted rename left two different bodies for one id. Under first-by-name, a new name
  that sorts after the old one would then have served the old body.
- The message profile's shared edge cases, the file the TypeScript SDK 4.89.1 and the Python package both hold (26 cases:
  what a Note is to every reader, the `created_at` order, an edit, a Character's line), now replay here, and the
  readings they caught are fixed. `HasUnseenRecipients` is false for a Note, keyless or not. `ThreadOf` walks a chain
  through a Note without listing it. A `created_at` is read by the profile's strict RFC 3339 rule (no lenient parse:
  a string that is not `YYYY-MM-DDTHH:MM:SS` with an optional fraction and zone sorts last). `NoteOf` passes over a
  Note without a string id. A Note's hidden mark is the one every reader reads (`OWPayload.IsHidden`: anything but
  absent, `null` or `false`; ruled 2026-10-27).

### Not added

- The TypeScript SDK 4.92.0's `draught(characterId, { lens })` and the frames it answers: this SDK has no client for
  the draught (`GET /character/{id}/context`), so it has no `lens` option either.
- `lenses` on a member row (the TypeScript SDK's `OwMember.lenses`): this SDK has no members list. The `lenses` key
  that `PATCH /account/worlds/{world_id}/members/{member_id}` takes is sent by `SetMemberLensesAsync`; the route's other
  keys (role, Character, sponsor) have no call here.
- The TypeScript SDK 4.94.0's `expiresAt` on `completeSignIn`'s result: this SDK has no sign-in exchange
  (`POST /auth/token`), so there is no result to carry it.
- No typed `/me`, lens-list or member-lenses model (the TypeScript SDK's `OwMemberLenses`): `GetMeAsync`,
  `SetMyLensesAsync`, `WorldLensesAsync` and `SetMemberLensesAsync` answer the body as served (`JObject`), as
  `GetMeAsync` always has.

## [0.23.0] - 2026-10-27

The message profile's serial 76 in `OWMessage`: a reader can tell a message with recipients it cannot see, a body
read with no key, a sender it cannot see, an edit by another hand, and a member's own Note, which is never a
message. The pin moves to dist.76. A minor release because it adds public API (below).

### Added

- **`OWMessage`, serial 76 of the message profile** (schema-dist `profiles/message.md`, rulings of 2026-10-26).
  `HasUnseenRecipients(message, keyless)` reads the server's served-trimmed mark (`x_served_trimmed`,
  `OWMessage.ServedTrimmedKey`, set on a body served to a key that may not see every Character, with those ids left
  out): true when it names `characters`, so someone the reader cannot see is a recipient (never who, never how many);
  null for a body read with no credential (`keyless: true`), which the server never marks. `OWSenderStatus.Unseen`: a
  missing `narrator` beside a mark naming `narrator` is a sender the reader cannot see; show "from someone you cannot
  see", never "from nobody". Without the mark it stays `Unverifiable`. `EditedBy(message, members)` answers an
  `OWEditedBy`: `Edited` when the message's `changed_by` is not its `created_by`, `By` that hand's Character on the
  members list, or null ("edited by another hand"); an edit never changes the sender's answer, and a body with no
  `changed_by` (a folder, an export) says nothing of edits. `NotePair`, `IsNote` and `NoteOf(narratives, characterId,
  members)`: a Note (a Narrative with supertype `Note`) is a member's own line in the world, never a message, and
  `NoteOf` gives the id of a Character's newest Note in its own hand (on the wire, a verified sender; in a folder, its
  `narrator`), neither hidden nor proposed. The profile's fixture, now 81 cases, is the test.
- `OWApiError.IsServedTrimmedNotWritable`: an element write that carried `x_served_trimmed` is refused as 422
  `served_trimmed_not_writable`. The client sends a body as it was read and leaves the refusal to the server (ruled
  2026-10-27, one rule for every package), so the refusal is the signal that a trimmed body is being written back:
  drop the mark and send again, and the trimmed list loses nobody.

### Changed

- `OWMessage.IsToEveryone` takes `keyless` (false by default): a body read with no credential is never "to everyone",
  because the server leaves hidden recipients out of it and does not mark it. An empty `characters` with the trimmed
  mark is not to everyone either: `IsAddressedTo` reads it as addressed to no Character the reader is given, and a
  list that still holds ids beside the mark is addressed to those ids. A Note is addressed to nobody, never unread and
  never in a thread, whatever its `characters` holds (`RecipientsOf` gives none for it).
- The schema pin moves to dist.76 (`v0.33.0-dist.76`): only the message profile moved, its page and its fixture (55
  cases to 81). No generated model changed.

## [0.22.0] - 2026-10-27

A read mark, named: `OWMessage.IsReadMark` says whether an element is a read mark, so a reader that weighs elements
by their links can leave one out. The Editor tests find the package's files through one helper. A minor release
because it adds public API (below).

### Added

- `OWMessage.IsReadMark`: whether an element is a read mark (type `relation`, supertype `Read`, trimmed and folded as
  `IsMessage` reads a message), whoever's mark it is. A reader that weighs elements by their links can leave a read
  mark out by it: a mark lists every message its reader has read, which is bookkeeping, not part of the world.

### Changed

- The Editor tests find the package's own files through one helper (`TestPackage.Root()`) instead of thirteen copies
  of it; outside the Editor, four more test classes now find their fixtures. The host project no longer carries the
  Unity template's readme (`Assets/Readme.asset`, `Assets/TutorialInfo/`).

## [0.21.0] - 2026-10-26

Messages between a world's members: `OWMessage` reads who a message is addressed to, whether its sender is proved
against the members list, a reader's read mark and unread messages, and a thread, held to the shared message
fixture's 55 cases. `OWFrontDoor.MySeats` lists the AI and player seats a key's person answers for, and
`OWApiError.IsCharacterPlayed` names a refused delete of a Character someone plays. A minor release because it adds
public API (below). The schema pin moves to dist.75.

### Added

- `OWApiError.IsCharacterPlayed`: a delete that names a Character someone plays on the world's roster (the owner's
  Character or a member's) is refused as 422 `character_played`, param `id`, and nothing is deleted. Re-point that
  member to another Character, or remove the member, then delete. The wire fixture is refreshed from the API's
  document (this code, and a public counts route this SDK does not call).
- `OWFrontDoor.MySeats`: the live AI and player seats the key's person answers for, agent seats first, each an
  `OWFrontDoorSeat` (`Id` the membership id, `Kind` agent or player, `Name`, `Character` the Character it plays or
  null). This is how a tool on a person's key tells which Character is that person's AI. The list is empty for a seat's
  own key, for anyone who answers for none, and on an API from before it. On `GET /me` and an agent's join answer. The
  wire fixture is refreshed (this key, and an account route this SDK does not call).
- **`OWMessage`, the message profile's readers** (schema-dist `profiles/message.md`, serials 72, 73 and 75): a message
  between a world's members is a Narrative with supertype `Message` (`OWMessage.Pair`, one of the room profile's
  pairs; a world's own message needs no `x_room_of`). `IsMessage` matches it trimmed and case-blind; `RecipientsOf`,
  `IsAddressedTo` and `IsToEveryone` read `characters` (empty, absent or null is everyone; a list of no valid id is
  nobody); `IsSentBy` reads `narrator`, the claim. `SenderStatus(message, members)` proves the claim against the
  membership in `created_by` on the world's members list and answers an `OWSender`: `Verified`, `Mismatch` (with
  `WrittenBy`, the author's Character) or `Unverifiable`; show anything but verified as a claim. `ReadMarkOf` finds a
  reader's read mark (a Relation with supertype `Read`, the reader as `actor`) counted only when every write on it was
  the reader's own hand (its `created_by` plays the reader, and a `changed_by` equals that `created_by`), the newest
  `updated_at` among several wherever it stands in the list. `UnreadFor(messages, characterId, readMark, members)`
  leaves out the reader's own messages: in a folder, where nothing carries `created_by`, those whose `narrator` is the
  reader; on the wire only those the members list proves the reader sent, so a claim never marks a message read.
  `UnreadFor` and `ThreadOf` list messages in `created_at` order, equal times keeping the list's order; a thread's walk
  stops at a `parent_narrative` that is not a string id. An empty Character id is addressed by nothing and has no mark.
  Bodies are `JObject`s, lists any sequence of them. The profile's 55-case fixture is its test, as it is the
  TypeScript SDK's and the Python package's. Parity with the TypeScript SDK 4.81.0 and the Python package 0.64.0 for
  serials 72 and 73.

### Changed

- The schema pin moves to dist.71 (`v0.33.0-dist.71`; only the calendar and room profiles' fixtures and pages
  moved). At serial 69 the calendar fixture's Gregorian case gains twelve steps around Leap Day in 1900, 2000 and
  2100, as LegendKeeper's API gave them for a user's own project on 2026-10-25, so a reader whose year layout ignores
  `except` while its year count keeps it now fails the shared fixture; this SDK reads all 198 steps, and its fixture
  test now counts the steps and refusals as well as the cases. At serial 71 the room profile says "trimmed" means
  Unicode White_Space at both ends, with three new cases (a tab and an ideographic space are trimmed; a leading byte
  order mark and a trailing U+001C are not); `OWRoom` already read all 28. Serial 70 is skipped: its manifest was
  superseded by 71's.
- The schema pin moves to dist.75 (`v0.33.0-dist.75`): serials 72 and 73 add the message profile (`profiles/message.md`
  and its fixture of 43 cases), vendored with the distribution; 74 changes only the text of the README and six
  profile pages; 75 rules who proves a reader's own message and mark (the fixture grows to 55 cases) and rewords the
  calendar fixture's two LegendKeeper notes. No generated model changed; `OWMessage` reads the message profile (above).

## [0.20.0] - 2026-10-25

Calendar version 2: a festival day's leap rule can now say "every 4 years, not every 100, yet every 400" and count
from a calendar with no year 0, so the Gregorian calendar and Harptos read exact, day for day, matching what
LegendKeeper's API answered for a user's own project on 2026-10-25. An era with no end now runs to the next counting-up
era whatever order the eras are listed in. The change feed says who hid or unhid an element, when, and through which
door, and the room profile counts a member's take on a work item. A minor release because it adds public API
(below). The schema pin moves to dist.68.

### Added

- **The room profile's ninth pair, relation/Take** (schema-dist serial 66): a member's take on a work item is the
  taker's own Relation with supertype `Take`, so `OWRoom.IsRoomElement` now counts it as a room element when it
  carries `x_room_of`. `OWRoom.Pairs` holds the nine pairs in the fixture's order; the fixture's 25 cases, the two new
  ones among them, run as tests. Parity with the TypeScript SDK 4.75.0 and the Python package 0.52.0.
- **`OWChange.ChangedAt`, `ChangedBy` and `ChangedVia`**: a hide's or an unhide's row in the `/changes` feed, as the
  readers who still see the element receive it, says when it was hidden or unhidden, by which membership (null for
  the world's owner) and through which door. All three are null on every other row; the row's `change_seq` is the
  act's place in the feed, while the element's own stays where it was. Parity with the TypeScript SDK 4.74.0.
- **`OWWriteDoor.Site`** (`"site"`): the owner's own page on onlyworlds.com, the door a hide made there names. It is
  not an agent's door.
- **Calendar version 2** (the calendar profile at schema-dist serial 68): an intercalary entry's leap rule may carry
  `except` (a list of divisors, each larger than and a multiple of `every_years` and of the one before, read in order,
  the last match deciding: every 4, not every 100, yet every 400) and `offset` (added to the year index, so the rule
  reads the year the calendar shows; 1 for a calendar with no year 0). `OWCalendar.ReadConfig` reads version 1 and 2
  and reads the two keys only under version 2; a chain it can't read, or either key without `every_years`, is no
  calendar. `CalendarIntercalary.Except` and `Offset` hold them, `OWCalendar.IntercalaryInYear` says whether an entry
  falls in a year, and `Validate` names a bad chain and the keys on a version-1 config. Year starts, lengths, the year
  search, weekdays and month starts all follow the rule. Parity with the TypeScript SDK 4.76.0: its LegendKeeper
  vector (466 days on five preset calendars, as LegendKeeper's API gave them for a user's own project on 2026-10-25)
  runs here as tests, and Gregorian and Harptos now read exact.

### Changed

- **An era with no end runs to the next up era, past any down era** (the calendar profile's rule 4 at serial 68). An
  up era starting at 0 listed before a down era with its pivot at 0 used to end where it began, decided by list order;
  it now holds every step up to the next up era's start. The fixture's `era-overlap-down-later-start` reads `11 Rise`
  from step 100, where it read no era before.

- The schema pin moves to dist.68 (`v0.33.0-dist.68`; canonical `00.33.00` unchanged, no schema YAML moved).
  `schema.json` and the walk now carry each integer field's `minimum` and `maximum` as the YAML has them, as advisory
  data for display; the platform enforces neither, and this SDK neither reads nor enforces them. The folder lint warns
  on a newer `format_version` stamp and no longer changes `sys.path` when imported; the spine and checks profiles'
  fixtures grow (the checks profile's two known-of stub cases), and the import map profile arrives (a table from
  another tool's words to the 22 types, for import doors); this SDK reads none of these. The calendar profile's
  version 2 is above. The generated models changed only in their serial stamps.

## [0.19.1] - 2026-10-24

The `.owworld` reader agrees with every other reader of the format: it refuses a deflate stream that does not end at
its declared size, and it folds names with Unicode 15.1's NFC built from Unicode's own files, so a newer runtime can
no longer change which names collide. It passes all 96 cases of the conformance set at dist.60, and a read of a big
world is about a quarter faster. The repository now says who maintains it and where questions, bugs and security
reports go. A patch release: no public API changed.

### Added

- The repository says who writes it and where a stranger goes: the README names its maintainer (an AI agent working
  for Titus, its replies posted from his account), questions and bugs go to this repository's issues
  (the package README pointed at Discussions on the standard's repository, which nobody read), a security problem
  goes through GitHub's private vulnerability reporting (`SECURITY.md`), `package.json` carries `homepage` and
  `bugs`, and the README links the OnlyWorlds code of conduct.

### Changed

- The schema pin moves to dist.60 (`v0.33.0-dist.60`; canonical `00.33.00` unchanged, no schema file moved). The
  `.owworld` conformance set the tests vendor grows to 96 cases (from dist.57 to 59: eight NFC pairs, the two deflate
  cases below and a pair of unassigned code points two Unicode versions tell apart), and the dist carries Unicode
  15.1's `UnicodeData` and `CompositionExclusions` beside its case folds. Before it, dist.55: the
  folder check takes `hidden` as a folder key, and the review profile says how a reader treats a mark that is not an
  object (a reader that hands a world on counts it as a doubt and leaves it out; a viewer shows the element as
  settled): `OWPayload.IsHidden`, `IsProposedOrInDoubt` and `IsProposed` already read both so. The checks profile
  gains `where.type` as a list, and a new spine profile describes a world that maps a software ecosystem; this SDK
  reads neither. The generated models changed only in their serial and date stamps.
- An `.owworld` read is about a quarter faster on a big world: each entry was inflated through a buffer of its own
  of 80 KiB, which Mono puts on its large-object heap, and that cost more than the inflating. The buffer is now the
  entry's size plus one byte (at most 80 KiB), so an entry that inflates past its size, an empty one included, still
  shows it on the first read. Sikelia's pack (20,791 entries), the deflate walk below included: a read's median
  4.4 to 4.7 s before, 3.2 to 3.6 s after (three runs each, alternated).

### Fixed

- **The `.owworld` reader's NFC is Unicode 15.1's, built from Unicode's own files, never the runtime's** (folder spec
  §4.7, OW-18, ruled at dist.57). The name fold called `string.Normalize`, which answers from the runtime's tables:
  the set passed only because Mono's predate Unicode 16, and a runtime at 16 decomposes U+11383 (unassigned at 15.1)
  and would refuse `pass-OW-18-nfc-unassigned` as a collision. NFC now comes from `UnicodeData-15.1.0.txt` and
  `CompositionExclusions-15.1.0.txt`, the dist's copies, as the case fold comes from `CaseFolding-15.1.0.txt`:
  `tools/gen_nfc.py` writes their facts into `OWNfcData` (internal: the classes, the canonical decompositions one level
  deep, the listed exclusions; 74 KB of source, about 30 KB of assembly), and `OWNfc` (internal) derives the rest at its
  first use (Full_Composition_Exclusion, 1,120 code points; 941 primary composites) and runs UAX #15: decompose, order
  the marks by class, recompose; Hangul by arithmetic. Unicode's NormalizationTest-15.1.0.txt, vendored in the tests'
  fixtures, proves it: 19,074 lines, every NFC invariant held, and every code point its Part 1 does not list is its own
  NFC. A name below U+0300 (any ASCII path) is returned untouched without building a table. A lone surrogate is folded
  by case alone on every runtime (Mono's `string.Normalize` composed around one, CoreCLR's refuses it). Sikelia's pack
  reads in the same time (its 20,791 names are ASCII); folding them is faster, and the first non-ASCII fold in a process
  costs about 7 ms once, where Mono's tables cost about 1.4 ms.
- **An `.owworld` entry whose deflate stream does not end at its declared compressed size refuses the archive**
  (`OWWorldArchive.Read`, cause `not_openable`; folder spec §4.7's size bullet, which speaks of every size an entry
  declares, compressed and uncompressed alike, ruled 2026-10-24). Mono's `DeflateStream` stops at a stream's end and
  ignores what follows, and at the end of its input it stops as if the stream were whole, so two broken entries read
  as good ones: a stream that ends before the size its headers declare, with bytes after it that are no part of it
  (the conformance set's `fail-OW-14-deflate-early-end`), and a stream with no end-of-block code at all
  (`fail-OW-14-deflate-no-end`), both with honest inflated sizes and CRCs. Every reader but Unity refused them. Each
  deflated entry's raw bytes are now walked after its size and CRC pass (`OWDeflateEnd`, internal: RFC 1951's stored,
  fixed and dynamic blocks, every symbol decoded and nothing copied) to the byte holding the final end-of-block code,
  which must be the last byte the entry declares (its bits after the code are padding). The walk reads through a file
  stream of its own in the archive's order, so it never seeks: on Sikelia's pack (20,790 deflated entries) it adds
  about 0.4 s to a read of about 4.5 s.

## [0.19.0] - 2026-10-23

The hidden mark, read and kept (folder format §4.8): `OWPayload.IsHidden` and `OWElement.IsHidden` say whether an
element is hidden from whoever the world is handed to, by the one rule the npm SDK and the Python package use, and a
world file made as a share leaves hidden elements out and lists them (`Pack`'s `includeHidden`). Folders declare
format 0.3.18. A minor release because it adds public API (below).

### Added

- **Reading the hidden mark** (folder format §4.8): `OWPayload.IsHidden(body)` and `OWElement.IsHidden` are true when an
  element's `hidden` is there with any value but `null` and `false`: the mark, or a doubt (`"true"`, `1`) counted as
  the mark, as the npm SDK and the Python package read it (Kael h58). One rule for every reader, so the word a viewer
  shows predicts what a share leaves out. A folder carries no viewer identity, so the mark means hidden from whoever
  the world is handed to, never from the person who opened it: a viewer shows the element with the word hidden on it,
  and a reader that hands the world on leaves it out unless asked. The key is kept and written back as it came.
- **A world file made as a share leaves hidden elements out** (folder spec §4.8, Skeld's ruling of 2026-10-23):
  `OWWorldArchive.Pack(worldFolder, archivePath, keepApiBlock, includePrivateRecords, includeHidden, hiddenLeftOut)`.
  A world file is a copy, so every overload keeps an element file carrying the mark byte for byte by default, as keel's
  own download keeps an owner's hidden elements. A tool making the file to hand to someone else passes
  `includeHidden: false`: every `.json` file (any case) directly in a type folder of `elements/` or the legacy
  `spatial/` whose JSON object `IsHidden` reads as hidden is left out whole, and `hiddenLeftOut`, when given, receives
  their paths inside the world in entry order (its count is the count). A file there that is not UTF-8 JSON, or not an
  object, is kept; nothing else is rewritten, so a visible element's link to a hidden one keeps its id. The two
  defaults differ on purpose: private records out unless asked, hidden kept unless the write is a share. The npm SDK's
  `writeWorldFile` (4.70.0) and the Python package's `write_world_file` (0.43.0) do the same.

### Changed

- Folders declare format 0.3.18. The version adds the producer `input` word `campfire-export` and rules
  the hidden mark (§4.8): an element file's `hidden: true` stays hidden, and a push never publishes it. This SDK
  writes `server`, reads every word as given and keeps the mark byte for byte; it has no push from a folder (a folder
  loads into a read-only cache), so nothing else moved.

## [0.18.1] - 2026-10-23

Folders declare format 0.3.17. A patch release: nothing else changed.

### Changed

- Folders declare format 0.3.17. The version only grew the list of a producer's `input` words (`kanka-export`,
  `legendkeeper-api`, `legendkeeper-export`); this SDK writes `server` and reads every word as given, so nothing else
  moved.

## [0.18.0] - 2026-10-23

Elements written as proposed, from Unity: the world's review queue lists new elements that carry the review mark, a
keeper settles one with `ReviewNewElementAsync`, and the mark's two readers say whether an element is settled
(schema-dist dist.52). A world can also be made on an account with its first key (`CreateAccountWorldAsync`). A
minor release because it adds public API (below).

### Added

- **The world's review queue lists new elements written as proposed**. `OWClient.WorldProposalsAsync`
  now returns two kinds of row in one order, newest first, told apart by the new `OWProposal.Kind`: a proposed change
  to an element (`proposal`), and a new element that carries the review mark `x_review_proposed` (`new_element`,
  `OWProposal.IsNewElement`). A new-element row is the element's own record: its `Id` is the element's id, and `By`,
  `Evidence` and `Expires` (a date) are the mark's words. A row from an older server has no kind and is a
  proposal. The page's `NextCursor` is opaque: pass it back as it came.
- **`OWProposal.Omitted`**: the fields a whole-replace proposal would empty (each also in `Fields` at its empty value).
  A proposal's `Fields` now holds only what differs from the element.
- **`OWClient.ReviewNewElementAsync(type, id, decision, reason)`**: a keeper (the owner or a co-builder, with the PIN)
  settles a new element written as proposed (`POST /{type}/{id}/review`). `"accept"` clears the mark and returns the
  element as written (`OWReviewed.Element`); `"decline"` deletes the element and returns its id (`OWReviewed.Deleted`).
  A reason goes with a decline only. Another decision, or a reason on an accept, is refused before anything is sent;
  an answer without the key its decision settles is an `OWApiError`. Sends `X-OW-Client`, never `X-OW-Agent`.
- **`OWApiError.IsNotProposed`**: 422 `not_proposed`, a review of an element that carries no mark (never marked, or
  already accepted).
- **Reading the review mark** (schema-dist's review profile): `OWPayload.IsProposed(body)` and
  `OWElement.IsProposed` are true when `x_review_proposed` is an object. That is the rule of the review queue and of a
  viewer, which shows such an element with its mark visible and apart from settled ones.
  `OWPayload.IsProposedOrInDoubt(body)` and `OWElement.IsProposedOrInDoubt` are true when the key is there with any
  value but `null`, a mark or a doubt. That is the rule of a reader that hands a world on, such as a context pack for
  an AI, which leaves those elements out unless asked. The mark is kept in the element's extensions and written back
  as it came, a cleared `null` included.
- Parity with the TypeScript SDK 4.66.0 and the Python package 0.35.0 (the queue, the review, `IsNotProposed`), and
  with the Python package 0.36.0 and the server's own two rules (the mark's readers); their test vectors ported.
- **`OWClient.CreateAccountWorldAsync(OWAccountWorldCreate)`**: makes a world on the account (`POST /account/worlds`)
  with its one write key, and returns `OWAccountWorldCreated`: the world's id, name, role and `PublicRead`, its key
  row (`OWAccountWorldKey`: name, last four, scope, revoked), and the full `ow_w_` key in `Key`, shown this once.
  `Name` is required; `Pin` only for an account with no PIN yet (it becomes the account PIN; either mistake is a 422
  with `Param` `pin` and nothing made); `KeyName` names the key; `Source` and `Producer` are the folder format's
  provenance lines, kept on the world and served back as its `origin`. Members left null are not sent. It takes an
  account token (`ow_a_`) only: any other key is refused before anything is sent. Parity with the TypeScript SDK
  4.66.0 (`createAccountWorld`), its test vectors ported; the answer is checked against the server's published
  `AccountWorldCreated`.

### Changed

- `OWClient.PatchWorldAsync` also leaves out `counts`, which the server now serves on a world read with no key and
  refuses on a world write (`OWPayload.WorldReadOnlyFields`), as the TypeScript SDK 4.68.0 and the Python package
  0.37.0 do.
- The schema pin moves to dist.52 (`v0.33.0-dist.52`; canonical `00.33.00` unchanged, no schema file moved). Only the
  review profile moved: its reader rule.

## [0.17.0] - 2026-10-23

The world's own settings, written from Unity: `PatchWorldAsync` sends a change to a world's settings, its origin and
its tools' extension keys, and folders carry the world's origin as the server keeps it (folder format
0.3.16). A minor release because it adds public API (below).

### Added

- **`OWClient.PatchWorldAsync(partial)`**, a write to the world's own settings (`PATCH /world`): only the keys sent
  change, and the call returns the world as the server holds it after. A `GetWorldAsync` body can be sent back whole:
  the keys the server refuses there are left out (`id`, `slug`, `public_page`, `public_read`, `agent_narrator_rule`,
  `agent_writes_proposed`, `origin`, now listed as `OWPayload.WorldReadOnlyFields`, and the element write's own
  read-only keys). A world's origin, where it first came from, is written as the plain keys `source` and `producer`
  (each an object, or `null` to clear it) and served back under `origin`. The world's extension keys
  (`x_*`, `atlas_*`, `shadow_*`, at the top level of its body, at most 64 KB in all) are sent as given, and `null`
  removes one. The owner's key may change any setting the server takes; a co-builder's only `time_range_current`,
  `source`, `producer` and extension keys, and anything more is a 403 (`OWApiError.IsOwnerOnly`). Sent with the PIN,
  like every write, and without `X-OW-Agent` or `X-OW-Client`. Parity with the TypeScript SDK 4.65.0 and the Python
  package 0.34.0, their test vectors ported.

### Changed

- **A folder export declares folder format 0.3.16** (`OWFolderExport.FormatVersion`), with the onlyworlds Python
  package's 0.33.0. The world's `origin` (its first import, as the server keeps it) is written into
  `world.json` exactly as `GET /world` serves it or as the one-file export carries it, beside the export's own `source`
  and `producer`; it is never made up from those two, and a world that has none gets none. The world's extension keys
  ride along the same way. Both readers (`OWFolderReader`, `OWEnvelopeReader`) keep `origin` and the extension keys in
  the world they read, so a folder rewritten from a read one carries them byte for byte. Nothing else in what an
  export writes changed.
- The schema pin moves to dist.51 (`v0.33.0-dist.51`; canonical `00.33.00` unchanged, no schema file moved). Only
  the example one-file export moved: its world block is now the World whole, with `origin` and an `x_names` map. The
  package's test copy of it moves with it.

### Fixed

- **The settings window's key note wraps**: the write key's line ("writes need one (a seat's secret, or the account
  PIN)") ran past the window's fixed edge and was cut. Found by pressing Test Connection in an Editor on three keys: a
  write key with its secret answers "Connected" through `GET /me`, the same key with a wrong PIN "Failed: 401
  invalid_credentials", a write key with no PIN "Connected" (reads need none).

## [0.16.0] - 2026-10-23

The server's newer conversation, caught up with the TypeScript SDK through 4.63.0. Writes name the client that made
them, a tip read can come back "unchanged", the PIN goes only where the server checks it, deletes, patches and link
edits can be made conditional on what you read, and a member can propose a change instead of making it. The sync's
reset no longer empties a copy unless the server says the elements left the key's view. Also the room profile's
reader, and a folder export at format 0.3.15. A minor release because it adds public API (below).

### Added

- **`OWRoom.IsRoomElement(element)`**, the room profile's reader (schema-dist `profiles/room.md`, serial 45): true
  when an element's type and supertype, each trimmed and compared without regard to case, match one of the profile's
  eight pairs (`OWRoom.Pairs`: a parlor's Message, a workshop's Work item) and it carries the mark `x_room_of` as a
  non-empty string. A lore view (a list, a count, a search, a map) leaves room elements out unless asked; a pair
  without the mark is lore, and a mark without a pair is not a room element. The profile's 23-case fixture is its
  test, as it is the TypeScript SDK's and the Python package's. Parity with the TypeScript SDK 4.55.0 and 4.57.2's
  trim, its tests ported.
- **The client's name on every element write**: `OWClientConfig.Client` names the software that
  writes, sent as `X-OW-Client` on `CreateAsync`, `PatchAsync`, `DeleteAsync`, `EditLinksAsync` and `BulkAsync`, never
  on a read. keel stores it on the element as `OWElement.ChangedClient` and on a `/changes` delete as the new
  `OWChange.DeletedClient`, so a reader can tell which tool made a change. Left null it is `unity-sdk`
  (`OWClient.DefaultClientName`); a game or tool passes its own (`"atlas"`). The rule is keel's, `[a-z0-9-]{1,32}`
  (lower case letters, digits and hyphens, no version): anything else, the empty string included, is an
  `ArgumentException` when the client is built, never trimmed. The name is the client's claim, not proof, and it
  changes no permission: an agent still sets `Agent`. Parity with the TypeScript SDK 4.56.0, its test vector ported.
- **The conditional tip read**: `ChangesAsync(headOnly: true)` keeps the last tip read's `ETag` and sends
  it back as `If-None-Match`, exactly as received. When nothing moved, keel answers 304 with no body (refunded against
  the request budget), and the call returns the last 200's head, cursor and epoch with the new
  `OWChangesPage.Unchanged` set; every 200 leaves it false. A 409 `resync_required` from any `ChangesAsync` call drops
  the tag, so the next tip read is a full 200. A 304 while no tag is held is an `OWApiError` with status 304, never a
  made-up head. The client never polls on its own. `OWSync.HasChangesAsync` and `IncrementalAsync` read the tip this
  way unchanged, so a poll that mostly finds nothing costs little. Parity with the TypeScript SDK 4.57.0, its test
  vector ported.
- **The world's front door**: `OWFrontDoor`, who a key is in a world, who answers for it and the house
  rules: `Sponsor` (membership id, kind and Character name of the person who answers for a seat; null for the
  owner's keys), `SponsorCharacter`, `MyCharacter`, `Charter` (the Law to read first) and `CharterSkill` (the Objects
  the Charter names). `OWFrontDoor.Of(me)` reads it from `GetMeAsync`'s answer, and an agent's join answer carries it
  as `OWJoined.FrontDoor` (null for a player). Parity with the TypeScript SDK 4.60.0 (which shipped the types with no
  test; these tests are this package's own, with a wire check that the model maps exactly the keys keel serves).
- **A conditional delete**: `DeleteAsync(type, id, ifMatch: changeSeq)` sends the element's `change_seq`
  as you read it in `If-Match`. The delete lands only if nothing changed since; otherwise it throws an `OWApiError`
  whose new `IsPreconditionFailed` is true (412 `precondition_failed`) and whose new `Current` is the element as it is
  now, and nothing is deleted. On an element already gone it is the idempotent 204. A negative `ifMatch` is an
  `ArgumentOutOfRangeException` before anything is sent. The parameter comes after `ct`, so existing calls compile
  as they were. Parity with the TypeScript SDK 4.61.0, its test vector ported.
- **Conditional patches and link edits**: `PatchAsync` and `EditLinksAsync` take `ifMatch` as
  `DeleteAsync` does, the element's `change_seq` as you read it, sent in `If-Match`. The write lands only if nobody
  changed the element since; otherwise nothing is written and the `OWApiError` has `IsPreconditionFailed` and the
  element as it is now in `Current`. A link edit moves the element too, so it is checked the same way. Last
  parameter, so existing calls compile as they were. Parity with the TypeScript SDK's `patch` and `editLinks`
  `ifMatch`, their test vectors ported.
- **The proposed change**: a member can propose a change to an element without making it, and the world's
  owner or a co-builder decides. `ProposeAsync(type, id, fields, evidence, expires, idempotencyKey)` (fields cleaned
  as a PATCH body is, a known-of stub refused), `ListProposalsAsync` and `WorldProposalsAsync` (pages of
  `OWProposal`, filtered by `OWProposalQuery`: state, since, element for the world queue, limit, cursor),
  `AcceptProposalAsync` (the row and the element as written, one ordinary write by the keeper),
  `DeclineProposalAsync` (with an optional reason) and `WithdrawProposalAsync`. In a world that takes an agent
  seat's writes as proposals, a seat's `PatchAsync` or `EditLinksAsync` answers 202: it throws the new
  `OWProposedError` (`IsProposed`, the row under `Proposal`) and the element did not change; a bulk item's 202 stays
  in its slot (`OWBulkSlot.IsProposed`, `OWBulkSlot.Proposal`). Five new `OWApiError` helpers: `IsFieldNotVisible`,
  `IsTooManyOpen`, `IsStaleProposal` (with the moved fields in the new `Conflicts` and the element in `Current`),
  `IsProposedOnly` and `IsOwnProposal`. `OWApiError` is no longer sealed, so `OWProposedError` can derive from it.
  Propose and accept send the client's name (`X-OW-Client`) and never `X-OW-Agent`, as keel's document lists. A
  world's `agent_writes_proposed` switch reads through `GetWorldAsync`; this package has no world write to strip it
  from. Parity with the TypeScript SDK 4.58.0, its test vector ported.
- **Why a view restarted** (served while the server's hidden-element feature is on): `OWChangesPage.EpochReason` says why the epoch
  last moved, `role` (the key's own role or membership moved: keep what a fresh walk no longer names, it is out of
  view, not gone) or `view` (something left the key's view: drop it); `OWApiError.ResyncReason` reads the same word
  from a 409 `resync_required`, and covers every move since the cursor. Both are null when keel sends none. A
  conditional tip read answered 304 carries no `EpochReason`, as in the TypeScript SDK. Parity with the TypeScript
  SDK's `epoch_reason` and `resyncReason`, their test vectors ported.

### Changed

- **A folder export declares folder format 0.3.15** (`OWFolderExport.FormatVersion`), with the onlyworlds Python
  package's 0.32.0. Nothing else in what an export writes changed: 0.3.15's clause is about sharing records, which an
  export from the server carries none of.
- **A private producer is matched to its directory by the archive's one fold** (folder spec §4.7, OW-8; OW-18's
  NFC, then simple case folding), so a manifest's `Fold` or a decomposed `café` leaves out `records/fold/` or the
  composed `records/café/`. 0.15.0 ignored case alone, which let a directory written in another Unicode form travel.
  Parity with the TypeScript SDK's 4.63.0 fix (363adc9), its test vector ported.
- **`Pack` refuses a records manifest it cannot read**, as the TypeScript SDK and the Python package do, unless
  private records go too: one that is not UTF-8 (a byte order mark is read past), holds an entry that is not an
  object, or marks private an entry whose `producer` is not one directory name (missing, empty, `.`, `..`, or with a
  `/` or `\`). 0.15.0 skipped such entries, so a private producer it could not name travelled.
- **`API-Pin` goes only where keel checks it**: on every write, on `GetMeAsync`, and on every request of a 10-digit
  legacy key; a prefixed key's other reads now go without it, and an account token never sent it. keel verifies a
  PIN wherever one is sent, at a real memory cost per request, so reads carrying one loaded the server for nothing.
  A wrong PIN therefore no longer fails a read: the settings window's Test Connection now asks `GET /me` for a key
  that takes a PIN, so it still catches one. Parity with the TypeScript SDK 4.59.0, its test vector ported.
- **A reset keeps what the server's reason says to keep**: when the change feed answers 409
  `resync_required`, `OWSync.IncrementalAsync` still walks the world again from the start, but no longer drops every
  element the new walk does not name. On reason `view` it drops them, as before; on `role`, or with no reason (a
  server older than 2026-10-20), it keeps them: they are out of this key's view now, not gone, and sharing is
  forward-only. `OWSyncResult.ResyncReason` and `OWSyncResult.Kept` say which happened, and `RebaselineReason` says
  it in words. A reset whose walk fails now puts the cache and its cursor back as they were and throws, where it left
  the cache emptied. This is the rule the TypeScript SDK and the Python package document for their callers (neither
  keeps a copy itself), with the no-reason case as the published changes documentation states it.
- **A cursor ahead of the server's head keeps the copy too**: when `OWSync.IncrementalAsync` finds the server rewound
  (restored from a backup), it walks the world again as before, but keeps what the cache held that the restored
  server no longer names (`OWSyncResult.Kept`), where it used to clear it, and a failed walk leaves the cache as it
  was. A restore rewinds the server, not what this copy read.

## [0.15.0] - 2026-10-23

Private records stay home. `OWWorldArchive.Pack` leaves a producer marked private out of an `.owworld` unless asked
(folder spec v0.3.15), and the schema pin moves to dist.49, whose conformance set carries the case. A minor release
because it adds public API (below).

### Added

- **Private records stay home** (folder spec v0.3.15, §4.5 rule 11; §4.7, OW-8): `OWWorldArchive.Pack` leaves out,
  whole, every `records/` producer whose `RECORDS.json` entry says `"private": true`, so a tool's own notes do not
  travel the first time a world is shared. A new overload, `Pack(worldFolder, archivePath, keepApiBlock,
  includePrivateRecords)`, carries them when asked. `RECORDS.json` goes in byte for byte either way, its private
  entries kept, so a reader can tell a producer was left out from one that was never there. A manifest that is not a
  JSON array refuses the pack unless private records go too, because which records are private cannot be told. The
  old signature is unchanged and leaves them out.

### Changed

- The schema pin moves to dist.49 (`v0.33.0-dist.49`; canonical `00.33.00` unchanged, no schema file moved). The
  `.owworld` conformance set gains the private case under OW-8: four writer fixtures on a second source world, 85 in
  all, none of the earlier 81 moved. It is re-vendored for `OWWorldArchiveConformanceTests`, which also packs the
  case's source and compares with both sides. The generated models changed only in their serial.

## [0.14.0] - 2026-10-22

A world's own words for the types. `OWNames` reads the names profile (schema-dist serial 48), and `OWElement`
reads the client's name on a write; the schema pin moves to dist.48, with no schema file moved. A minor release because
it adds public API (below).

### Added

- **A world's own words for the types** (the names profile, schema-dist serial 48): `OWNames.TypeWord(world, type,
  supertype, many)` reads the World's `x_names` and returns the word a tool prints for a type, such as *Employee* for a
  Character in a work world or *AI agent* for one with supertype `Agent`, or null for the standard's name. The
  supertype's word comes first, matched trimmed and without regard to case, then the type's; a plural is asked with
  `many`. A value that is not a names map reads as null everywhere, and a bad entry is ignored, never an error. A
  display lookup only: the API, folders, the schema and every id keep the standard's names. It is a port of the
  TypeScript SDK's `typeWord`, checked by the profile's conformance fixture, vendored (`OWNamesTests`: 23 cases).
- **`OWElement.ChangedClient`** (schema-dist serial 46): the name the client gave on an element's last
  write, such as `atlas`, or null. The client's claim about itself, never proof. Read-only like the other server
  fields; before, it landed among an element's extensions.

### Changed

- The schema pin moves to dist.48 (`v0.33.0-dist.48`; canonical `00.33.00` unchanged, and no schema file moved). The
  room profile gains its mark, `x_room_of` (45), and defines "trimmed and without regard to case" once for every
  profile (47); element bodies carry `changed_client` (46), so the example export the envelope tests read
  moved; two profiles join the dist, `checks` (47, house rules on a work world's Charter, which this SDK does not read)
  and `names` (48, a world's own words for the types, read by `OWNames` above). The generated models changed only in
  their serial.

## [0.13.0] - 2026-10-22

Schema 00.33.00: a Zone says how its markers join. `OWZone.Shape` (`polygon` or `line`) is generated from the schema's
new field, and the schema pin moves to dist.44 (the field at serial 43, the room profile at 44, which this SDK does not
read yet). A minor release because it adds public API (below).

### Added

- **`OWZone.Shape`** (schema `00.33.00`): how a zone's markers join, `polygon` or `line`, lowercase; empty, absent or
  any other value means the shape is not stated, and a reader draws a polygon. Generated from the schema like every
  field.

### Changed

- The schema pin moves to dist.43 (`v0.33.0-dist.43`; canonical `00.33.00`, a minor step: Zone gains `shape`). The
  example export the envelope tests read moved only its `schema_version`; the `.owworld` conformance set did not move.
  Then to dist.44 (`v0.33.0-dist.44`, canonical unchanged): the dist gained the room profile, which this SDK does not
  read yet.

## [0.12.1] - 2026-10-22

The world file Unity writes now carries its marker. `OWWorldArchive.Pack` places the `mimetype` entry first and
stored, as folder spec §4.7 asks of a writer and as the TypeScript and Python writers do, and writes zip64 from 65,535
entries. The schema pin moves to dist.42, whose two new conformance cases the reader already refused. A patch release:
no public API is added, and the list `Pack` returns now starts with `mimetype`.

### Added

- **`OWWorldArchive.Pack` writes the `mimetype` marker** (folder spec §4.7, OW-6): the first entry, stored (method 0),
  no extra field, its bytes exactly `application/vnd.onlyworlds.world+zip`, as the TypeScript SDK's and the Python
  package's writers place it. `Pack` now writes the zip itself, since Mono's `ZipArchive` cannot store an entry: every
  other entry is deflated (an empty one stored), each stamped 1980-01-01 00:00, in UTF-8 byte order as before; a
  non-ASCII name carries the UTF-8 flag; a classic zip below 65,535 entries and zip64 from there. The list `Pack`
  returns starts with `mimetype`. A Unity-packed world reads the same in the reference checker, the TypeScript SDK's
  `readWorldFile` and Windows' `tar`, and the reference finds no writer fault in it.

### Changed

- The schema pin moves to dist.42 (`v0.32.2-dist.42`; canonical `00.32.02` unchanged, and no decoded file moved).
  The `.owworld` conformance set gains two cases, which the package's own test now runs: a stored entry shorter than
  its declared size (`fail-OW-14-stored-short`, refused as `not_openable`) and a `mimetype` marker with a byte order
  mark before the media type (`fail-OW-6-marker-bom`, refused as `marker`), 81 zips. The reader already refused both
  for the cause the set names, so no read changed. The generated models changed only in their serial.

## [0.12.0] - 2026-10-22

The world file's last checks, and the folder's maker. An `.owworld` entry whose bytes fail their CRC-32 now refuses
the archive, every file checked, media and records too, as the TypeScript SDK and the reference do; the shared
conformance set (schema-dist `fixtures/owworld/`, dist.40) runs as the package's own test, 79 cases agreed. An
export declares folder format 0.3.14 and writes the producer line (§4.2): which tool made the folder, and from what.
The schema pin moves to dist.40 (canonical `00.32.02` unchanged). A minor release because it adds public API (below).

### Added

- **The `.owworld` conformance set runs as the package's own test** (schema-dist `fixtures/owworld/`, dist.40;
  folder spec §4.7): `OWWorldArchiveConformanceTests` reads all 79 zips with `OWWorldArchive.Read` and checks each
  verdict and each refusal's cause against the set's `verdicts.json`, which is pinned to the dist's manifest, every
  zip to its sha256. The set is vendored in a folder Unity does not import (`Tests/Editor/Fixtures/owworld~/`), so
  the package's own importer never opens a hostile file in it. `OW_OWWORLD_FIXTURES` runs another copy of the set.
- **The producer line** (folder spec §4.2, v0.3.14): a folder `OWFolderExport.ExportAsync` makes says which tool made
  it and from what, `"producer": {"tool": "com.onlyworlds.sdk", "version": "<this package's>", "input": "server"}`,
  after `source` and `exported_as`, where the Python package's `export_world` (0.16.0) writes its own. An export is a
  folder's birth, so the line is always the export's own, also when the server's world carries another folder's.
  `ExportFileAsync` carries the line keel's file holds verbatim, and writes none when it holds none: the file's maker
  made the folder. `OWWorldArchive.Pack` keeps it as written, and a reader takes any `input` word, one it does not
  know included. New public members: `OWFolderExport.ExportProducer`, `ProducerTool`, `ProducerVersion` (kept equal
  to `package.json`'s `version` by a test).

### Changed

- The schema pin moves to dist.40 (`v0.32.2-dist.40`; canonical `00.32.02` unchanged, and no decoded file moved:
  the schema, the walk, its rulings, `presentation.json` and the examples are byte for byte dist.35's). The dist
  gained the packet and review extension profiles, a folder lint rule, and the `.owworld` conformance set (folder
  spec §4.7; serial 40 adds its CRC case). The generated models changed only in their serial.
- An export declares folder format `0.3.14` (`OWFolderExport.FormatVersion`), as the Python package's 0.16.0 does.
  0.3.13 rules the `.owworld` archive (§4.7), which this SDK's reader follows on the whole conformance set; 0.3.14
  adds the producer line (above). A `format_version` a world already carries is still kept as it is.
- The wire check's snapshot of keel's API document is refreshed (2026-10-22): every route, query parameter, header,
  body key and error code the client uses is still one keel declares, and every key the join models read is one it
  serves.

### Fixed

- **An `.owworld` entry whose bytes fail its CRC-32 refuses the archive** (`OWWorldArchive.Read`, cause
  `not_openable`; folder spec §4.7's size bullet, the conformance set's `fail-OW-14-crc`). Mono's `ZipArchive` never
  compares the CRC it is given, so a flipped bit in an element's deflate stream was read as other text, an element
  with a broken id, without an error. The CRC is taken in the pass that copies the bytes and compared with the
  central directory's. In the same pass, an entry that inflates to fewer bytes than it declares, or whose deflate
  stream is broken, refuses the archive as `not_openable` (it was skipped like an unreadable file in a folder), and a
  `mimetype` marker that is a broken entry refuses as `not_openable`, not `marker`: the TypeScript reader's and the
  reference checker's causes. Every file in the archive is checked this way, not only the ones the read returns: a
  corrupt image under `media/`, a broken file under `records/` or beside the world refuses the archive too, as in the
  TypeScript reader. Those files are inflated through one buffer and never held, so the read's memory is unchanged.
- A folder read (`OWFolderReader.Read`, `ReadEntries`, and so an `.owworld` read and `LoadInto`) no longer takes a
  tool's litter for an element (folder spec §4): a dot-prefixed folder under `elements/` (`.atlas/`) is not read, and
  a dot-prefixed file in a type directory (an editor's swap file, a sync client's `._` twin) is skipped and listed in
  `Skipped`, even when it holds a valid body. Before, `elements/character/.tmp-a.json` was read as an element beside
  the real file with the same id. The Python package's rule and test vector, ported.

## [0.11.0] - 2026-10-22

World archives follow the folder spec's `.owworld` section (§4.7): `OWWorldArchive.Read` (and so `LoadInto` and the
importer) agrees with the shared conformance fixture set on every one of its 78 cases, as the TypeScript SDK 4.49.0
does. A refusal names its cause from the spec's list, a `mimetype` marker is read, the default budget is the spec's
floor (500,000 files, 2 GiB), entry names are strict, two names that one disk would merge are found by Unicode simple
case folding pinned at 15.1.0, a world's `api` block is stripped on read, and `Pack` writes in code point order. A
minor release because it adds public API (below). Also here: the archive budget, a records producer's own files kept
whole, and entries refused that would not land safely on every disk.

### Added

- **The `.owworld` marker** (folder spec §4.7, OW-6): `OWWorldArchive.MediaType` is the file's media type,
  `application/vnd.onlyworlds.world+zip`. `OWWorldArchive.Read` (and so `LoadInto` and the importer) accepts an
  archive whose root holds a `mimetype` entry with exactly those bytes, never requires one, and refuses one that
  holds anything else (another type, a trailing newline, a BOM).
- **An `.owworld` refusal names its cause**: `OWFolderFormatException.Reason` is set on every refusal from
  `OWWorldArchive.Read` (and so `LoadInto` and the importer) to one cause from folder spec §4.7's shared list,
  the constants of the new `OWWorldArchiveReason` (`not_openable`, `not_a_world`, `more_than_one`, `unsafe_path`,
  `too_big`, `too_many`, `duplicate`, `collision`, `link`, `method`, `name`, `marker`), the names the TypeScript
  reader and the conformance fixture set use. A folder's refusal leaves it null. The exception type is unchanged.

### Changed

- **An `.owworld` is read within a budget**: `OWWorldArchive.Read` and `LoadInto` (and so the importer) refuse an
  archive that holds more than 500,000 files or declares more than 2 GiB uncompressed, before inflating anything, and
  refuse one with an entry that inflates past the size it declares (a zip bomb's lie), so a hostile file cannot fill
  memory. The numbers are folder spec §4.7's floor (OW-15), the size every reader accepts, as
  `OWWorldArchive.DefaultMaxBytes` and `DefaultMaxEntries`; the new `OWWorldArchiveReadOptions` (`MaxBytes`,
  `MaxEntries`), passed to `Read` or `LoadInto`, lowers or raises them for one read. The TypeScript SDK's reader
  holds 1 GiB and 200,000 files today, below the floor. Its budget rule and test vectors are ported.
- **An `.owworld` entry name must be a plain relative path** (folder spec §4.7, OW-16, ruled strict):
  `OWWorldArchive.Read` (and so `LoadInto` and the importer) refuses an archive with an entry name that holds a
  backslash, or has an empty, `.` or `..` segment, whatever it would resolve to. Before, `w\x.json`,
  `w//x.json`, `w/./x.json` and `w/a/../x.json` were resolved and read; one name with two spellings is a file two
  readers can take for different entries. The TypeScript reader's rule.
- **A read strips an `.owworld`'s `api` block** (folder spec §4.7, OW-10): `OWWorldArchive.Read` (and so `LoadInto`
  and the importer) no longer returns it in `World`, so a world read from an archive is never `IsSyncable` and its
  `ServerWorldId` is its own id. The file is still read: refusing it would strand a person's own leaked copy. The
  TypeScript reader's behaviour. `Pack`'s `keepApiBlock: true` still keeps the block, and its doc now says what the
  spec does: such a file is a private copy, not a portable `.owworld`.

### Fixed

- `world.json`'s `id` must be a non-empty string, as the folder spec requires (§4.1): a folder read, an archive read
  and `OWWorldArchive.Pack` refuse an `id` that is a number, a boolean, a list or blank (`"  "`). Before, `7` was read
  as the id `"7"` and a blank id passed. The TypeScript reader refuses the same.
- `OWWorldArchive.Pack` keeps a producer's own `x.meta` beside its `x` in `records/`, byte for byte (§4.5 rule 6).
  It left out every `.meta` named after a file or folder beside it as Unity's; now only one that opens as Unity
  writes them (`fileFormatVersion:`, then a `guid:` line) is left out.
- `OWWorldArchive.Read` (and so `LoadInto` and the importer) refuses an `.owworld` whose entries would not land
  safely on every disk, anywhere in the archive, not only in the world's folder:
  - two entries of one name (before, only inside the world's folder);
  - two names that are one once case and Unicode normalization (NFC) are ignored, files or folders (`Ana.json`
    beside `ana.json`; `café` in NFC beside NFD; `Elements/` beside `elements/`): one file on Windows and macOS.
    The fold is the one folder spec §4.7 rules for every reader (OW-18): NFC, then Unicode simple case folding
    (CaseFolding.txt's C and S rows at Unicode 15.1.0, the version the spec pins, so every reader folds the same
    letters), from a table generated by `tools/gen_casefold.py` from the copy the conformance fixture set vendors,
    so it is the same on every runtime and never the runtime's culture data. So `ς` meets `σ`, `ſ` meets `s` and
    Cherokee's small letters meet its capitals, while `ß` stays apart from `ss` and the Turkic `İ` and `ı` from `i`;
    upper-then-lower casing, which Mono gets wrong on 484 code points at 15.1.0, is not used;
  - a path that is both a file and a folder (`w/media` beside `w/media/a.png`);
  - a symbolic link or special file, by the Unix mode in the entry's external attributes;
  - an encrypted entry, by its flag (ZipCrypto was refused only because its header made the size wrong), and an
    entry in a method other than stored or deflate (LZMA, deflate64, bzip2): before, an element the runtime could
    not inflate was skipped and the read went on, and deflate64 was read where the runtime could;
  - a Windows device name as a name's stem, with or without an extension (`CON.json`, `nul.txt`, `COM1`-`COM9`,
    `LPT1`-`LPT9`), a name ending in a dot or a space, the characters `<>:"|?*` or a control character;
  - a non-ASCII name without the zip's UTF-8 flag, whose spelling is a guess, and a name with the flag whose bytes
    are not UTF-8 (the runtime read them as U+FFFD, another name than the one written);
  - a stored entry whose stored size differs from the size it declares: holding more is the zip bomb's lie, refused
    from the directory alone, so in `media/` too, which is never read.

  And a second `world.json` inside the world (`w/inner/world.json`) is refused as a second world; one under
  `records/` stays a producer's own data. The TypeScript reader also refuses a file-and-folder path and a nested
  `world.json`; it has none of the other checks yet. Each refusal names the entry and why.
- `OWWorldArchive.Pack` writes entries in the order of their names' UTF-8 bytes (code point order), as folder spec
  §4.7 rules (OW-13). It sorted UTF-16 code units, which put a name past U+FFFF (an emoji) before one from U+E000
  to U+FFFF (a fullwidth letter), so two writers given one world wrote different bytes.

## [0.10.0] - 2026-10-21

Known-of elements (folder format v0.3.10, §4.4): onlyworlds.com hands a guest or player key some elements as stubs
(`known_of: true`), an element its Character only knows of. An export now writes a stub as it is, a reader can tell a
stub from a whole element, and the client refuses to write one back. An export declares folder format 0.3.12, and a
typed calendar era refuses a date it does not hold. A minor release because it adds public API (below).

### Added

- **A stub is written as it is**: `OWFolderExport.ExportAsync` and `ExportFileAsync` write a stub with the keys it
  keeps (`id`, `type`, `name`, `description`, `supertype`, `subtype`, `image_url`, `created_at`, `updated_at`,
  `change_seq`, `OWFolderExport.KnownOfKeeps`), every other field it was sent with a value (a Character's public
  `physicality` or `height`), and `known_of: true` last. The unset fields the server sends (`""`, `null`, `[]`) are
  left out: on disk they would read as a whole element with nothing filled in. `0` and `false` are values and are
  kept. A whole element is written as served, and an element served as a stub and then whole (or the other way) is
  written as it came last. The rule is `OWFolderExport.KnownOfForDisk(body)`, a port of the Python package's
  `known_of_for_disk`, checked against its bytes.
- **A reader can say "stub"**: `OWPayload.IsKnownOf(body)` (true only for `known_of: true`), `OWElement.IsKnownOf`
  on every typed model, `OWFolderElement.KnownOf`, `OWFolderReadResult.KnownOf` (the ids of the stubs a folder holds,
  sorted) and `OWWorldCache.IsKnownOf(id)`. A stub's absent or empty fields are unknown, not empty: a typed model of
  one reads `""`, `[]` or null for them exactly as a whole element with nothing filled in would, so check the flag.
  `IsKnownOf` is read from the model's extension fields, never a modelled bool, so a whole element never sends
  `known_of: false`.
- `OWApiError.IsKnownOfNotWritable` (422 `known_of_not_writable`), matching the npm SDK's helper.

### Changed

- **A stub is never written back**: `OWClient.CreateAsync`, `PatchAsync` and each item of `BulkAsync` throw an
  `ArgumentException` for a body (typed or raw) marked `known_of: true` before anything is sent; the bulk message
  names the item (`bulk: items[1]: ...`). Its empty fields are not the element's values, and writing it would blank
  them. `OWEdit.Begin` refuses a stub the same way.
- `known_of` joins `OWPayload.ReadOnlyFields` (six fields now, as the npm SDK 4.42.3's list), so a body carrying
  `known_of: false` has it stripped rather than refused by the server.
- An export declares folder format `0.3.12` (`OWFolderExport.FormatVersion`), as the Python package's 0.10.4 does.
  0.3.11 (an element's id replaced) and 0.3.12 (the `records/RECORDS.json` manifest) need nothing more here: the SDK
  never replaces an id, and it carries `records/` byte for byte. Folders stamped `0.3.9` stay valid.

### Fixed

- `OWCalendar.ParseDate` refuses a date its typed era does not hold ("not a date in Before Gate") instead of reading
  the number as an absolute year that lands in another era ("10 Thaw, 1 BG" read as 10 Thaw, 2 AG). The absolute
  reading stays for a step in no era, where `FormatStep` prints the absolute year. Parity with the TypeScript SDK
  4.42.4, its test vector ported.

## [0.9.0] - 2026-10-20

Party links: one join link seats a table of players, each picking a Character from the link's list (or typing a name
at a table that lists none), walked live on onlyworlds.com through a game's door; the wire check now reads request
and response bodies and error codes; the schema pin moves to dist.35. A minor release because it adds public API
(below).

### Added
- Party links (one join link for a table of up to 50 players): `OWJoinPreview` reads `MaxUses`, `UsesLeft`,
  `NameRequired` and `Characters` (`OWJoinPick`: id, name, image url, taken), and `IsTable` tells a party link from a
  link for one, which sends none of them.
- `OWJoin.RedeemAsync` takes `character`, the player's pick from the preview's list. It is the last parameter, so
  existing positional calls keep working.
- `OWApiError.IsCharacterTaken`, `IsCharacterInvalid` and `IsWorldFull`, matching the npm SDK's helpers.

### Changed
- The schema pin moves to dist.35 (`v0.32.2-dist.35`; canonical `00.32.02` unchanged). The portal's example download,
  vendored for the envelope tests, now carries `world.source` and `world.exported_as` as keel writes them, and the
  reader and the file export keep both. The generated models changed only in their serial.
- The wire check also compares request body keys, response keys and error codes with keel's OpenAPI document
  (`tools/refresh_wire.py`; the fixture refreshed from production). The SDK's join keys, the preview's and a join's
  answer keys, and every code an `OWApiError` helper names are checked against what keel declares.

## [0.8.0] - 2026-10-20

A world folder zipped as one `.owworld` file imports into Unity as a read-only world cache, measured in the Editor, a
desktop player, an Android IL2CPP player and a WebGL player; the calendar reader follows the new `atlas_calendar`
profile and its conformance fixture; the schema pin moves to dist.33. A minor release because it adds public API
(below).

### Added

- **World archives, `.owworld`** (a spec world zip, renamed: one world, as one top-level folder or with `world.json`
  at the root). Drop one into `Assets/` and it imports as a read-only `OWWorldCache`; a zip with no world or two is an
  import error, never a guess. The cache is keyed by the world's id and the archive asset's GUID
  (`OWWorldArchive.KeyFor(worldId, guid)`), so the asset carries no machine's path and keeps its key through a move.
  The archive is read in memory, never extracted, and an entry name that is absolute or climbs out of the world
  refuses the archive. `Assets/OnlyWorlds/Pack World Folder...` packs a folder from anywhere
  on disk into the project and imports it; `OWWorldArchive.Pack(folder, archive)` is the same step from code. Pack
  writes only the archive, never into the folder; packs `world.json`, `elements/`, `spatial/` and `media/`, leaving out
  dot-folders, dot-files and Unity `.meta` files, and `records/` whole and byte for byte (folder spec §4.5: a producer's
  files are never dropped; the importer reads none of them); drops `world.json`'s `api` block (it holds a key) unless asked to
  keep it; and is deterministic (ordinal entry order, a fixed timestamp). `OWWorldArchive.Read(archive)` gives a folder
  read's result. Media travels in the archive but is not imported yet.
- `OWFolderReader.ReadEntries(entries, includeLegacySpatial?, source?)` reads a world held in memory, as
  (path inside the world, text) pairs, by the folder read's own rules and code; `Read(folder)` is unchanged.
- `OWFolderLoader.LoadInto(cache, read, key, readOnly?)` fills a cache from a read already done, so it is not paid
  twice. `LoadInto(cache, path)` now calls it and behaves as before.
- Measured on Tuvsh Ul (3,829 elements): an imported archive loads about 5-6x faster than the folder reader from
  cold in a release desktop player, and the asset and a runtime `OWWorldArchive.LoadInto` both work in an Android
  IL2CPP player and a WebGL player (Minimal stripping; higher stripping untested).

### Changed

- `OWJoin.ParseLink` names the link shape with the preview on, `<site>/join/<world_id>#<code>`, and a
  test holds it. Its behaviour is unchanged: it always read the code after the last `#`.
- The schema pin moves from dist.24 to `v0.32.2-dist.33` (canonical `00.32.02`): two descriptions in the generated
  models (an Event's `characters` are those who know of it; a Narrative's dates are when it is told), and
  `ow-presentation.json` gains each type's `public` list (the fields shown on a known-of element). The walk and the
  rulings are unchanged.

### Fixed

- **The calendar reader follows the `atlas_calendar` profile** (schema-dist `profiles/calendar.md`, dist.33) and the
  TypeScript kernel 4.42.2, checked by the profile's conformance fixture, now vendored (`OWCalendarFixtureTests`: 52
  cases, 113 steps). A down era counts its years from the one holding the step before its pivot, so a mid-year pivot
  has no year 0, and `ParseDate` is its inverse again; at equal starts an up era wins over a down one, then the later
  in the list; an `eras` or `intercalary` that is present but not a list (`false`, `0`, `""`) is not a calendar; a
  calendar written as a JSON string is read only when it is strict JSON.

## [0.7.0] - 2026-10-20

Join a world by link, read the one-file export and write it as a folder, and an export's `world.json` says where it
came from and which slice it is (folder format v0.3.9). A minor release because it adds public API (below).

### Added

- **Joining a world by link**: `OWJoin.ParseLink(text)` finds the `ow_j_` code in a pasted link or
  a bare code; `OWJoin.PreviewAsync(code, transport)` (POST `/join/preview`) says who invites, to which world, for
  which reader, without spending the code; `OWJoin.RedeemAsync(code, transport, name?, agentName?)` (POST `/join`)
  spends it. A player's code answers with a read key (`ow_r_`, no PIN) and the Character it reads as; an agent's with
  a seat's write key and secret. Both send no credential, so they sit beside `OWClient`, which cannot be built
  without a key; npm's `previewJoin` and `redeemJoin` are the same wire. Every bad code is keel's one 404
  `not_found` (`OWApiError.IsNotFound`). The wire check records both calls (12 now).
- **The one-file world export** (`format: onlyworlds-world-export`, spec §8c), the file an owner downloads from a
  world's page: `OWEnvelopeReader.Read(path)` / `Parse(json)` give a folder read's result (`OWEnvelopeReadResult`,
  with the file's `Format`, `SchemaVersion` and `ExportedAt`), and `OWEnvelopeLoader.LoadInto(cache, path)` fills a
  read-only cache. The reader keys on `format` alone; `schema_version` (the standard's version the elements follow)
  is kept as information, never checked. An element with no id is skipped and reported, as in a folder. The test
  fixture is keel's own emitter output, published in schema-dist (`example/world-export.json`, serial 25) and vendored
  byte for byte; a test holds its hash to the dist's MANIFEST.
- **The one-file export as a world folder**: `OWFolderExport.ExportFileAsync(path, folder, options?, progress?, ct?)`
  writes the downloaded file as the same folder `ExportAsync` writes from the API (bodies as the file holds them, every
  key of its world carried into `world.json`, each Map's image fetched from its `image_url`), born whole or not at all.
  What a folder cannot keep whole is refused, not skipped: an element with no id, a type that is not the standard's,
  one id under two types.
- **The provenance lines** (folder format v0.3.9, §4.2): an API export's `world.json` now carries `source`
  (`{server, world, at}`: the API's origin, the world's id, when the export began, as `toISOString` writes it) and,
  when `GET /me` says which slice the key reads, `exported_as` (`{kind, role, character}`), after the wire's keys, as
  the Python package's `export_world` writes them (0.7.0). `OWFolderExport.ExportedAs(me, characterName)` is the
  spec owner's table in one place; a refused `/me`, or a guest whose Character cannot be read, writes none. New:
  `OWClient.GetMeAsync` (GET `/me/`, in the wire check: 13 calls), `OWFolderExportOptions.Clock`.

### Changed

- An export declares folder format `0.3.9` (`OWFolderExport.FormatVersion`), as the package's 0.7.0 does.

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
- **The door of every write** (the npm SDK's 4.26.0 has the same). `Agent = true` on
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
