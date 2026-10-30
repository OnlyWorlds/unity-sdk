# OnlyWorlds Unity SDK

Your [OnlyWorlds](https://www.onlyworlds.com) world, its characters, locations and laws, as objects in your Unity game.

It reads and writes worlds through the OnlyWorlds API, with typed C# models for all 22 element types and a world
cache you can browse in the Editor.

![The World Browser in the Unity editor: Moppetopia's 22 element types with their icons and counts, its characters, and Admiral Fluffington's fields](https://media.onlyworlds.com/onlyworlds/readme/unity-world-browser-2.webp)

*The World Browser (**Window → OnlyWorlds → World Browser**) on Moppetopia, the demo world.*

## Install

Unity 6000.0 or later. In the Package Manager, **+ → Add package from git URL**:

```
https://github.com/OnlyWorlds/unity-sdk.git?path=/Packages/com.onlyworlds.sdk
```

Newtonsoft JSON comes with it.

## First run

This reads Moppetopia, a public sample world, with its demo key. No account needed.

```csharp
using OnlyWorlds.Sdk;
using UnityEngine;

public class FirstRun : MonoBehaviour
{
    async void Start()
    {
        var client = new OWClient(new OWClientConfig {
            ApiKey    = "0000000001", // demo key: Moppetopia, read-only
            Transport = new UnityWebRequestTransport(),
        });

        var world = await client.GetWorldAsync();
        Debug.Log(world["name"]);

        var found = await client.ListAsync<OWCharacter>("character",
            new OWListParams { NameContains = "fluffington" });
        Debug.Log(found.Data[0].Name);
    }
}
```

Put it on any GameObject and press Play. The Console shows `Moppetopia`, then `Admiral Fluffington`.
`ListAllAsync<OWCharacter>("character")` pages through every character.

A fuller example ships with the package: **Package Manager → OnlyWorlds SDK → Samples → Quick Start**. It reads the
same world from the API or from a cache asset, and shows nullable fields, links and errors.

## Your own world

Create a world at [onlyworlds.com](https://www.onlyworlds.com); its keys are on the world's page. A new key is shown
once, so copy it then.

- **`ow_r_`** reads, with no PIN. This is the key to ship in a game.
- **`ow_w_`** reads and writes. Writes also send a secret in `ApiPin`. For code, use an
  [agent seat](https://onlyworlds.github.io/docs/development/agents): it has its own key and secret (`ow_s_…`),
  works in one world, and the world's owner can remove it at any time. The account PIN works too, but it opens every
  world on the account.

```csharp
var client = new OWClient(new OWClientConfig {
    ApiKey    = seatKey,    // ow_w_…
    ApiPin    = seatSecret, // ow_s_…
    Transport = new UnityWebRequestTransport(),
});

var made = await client.CreateAsync<OWCharacter>("character", new { name = "The Brine Cartographer" });
await client.PatchAsync<OWCharacter>("character", made.Id, new { description = "Maps the tide lines." });
```

`PatchAsync` sends only the fields you pass; `DeleteAsync("character", id)` removes one. Keep write keys out of
builds you ship. A key or PIN typed into a serialized field in the Inspector is saved in the scene and travels inside
the build, where anyone with the build can read it (with Mono or IL2CPP alike), so read write credentials at runtime.

**A player link** (`https://www.onlyworlds.com/join#ow_j_…`, made by a world's owner for one Character) needs no
account. Redeem it once for that player's read key; the server then shows the key only what that Character may see.

```csharp
var transport = new UnityWebRequestTransport();
var code = OWJoin.ParseLink(pasted);                  // null when the text holds no code
var joined = await OWJoin.RedeemAsync(code, transport); // the code is spent: keep joined.Key
var client = new OWClient(new OWClientConfig { ApiKey = joined.Key, Transport = transport });
```

`OWJoin.PreviewAsync(code, transport)` says who invites, to which world, without spending the code.

## What's in it

| Part | Assembly | What it does |
|---|---|---|
| **Client and models** | `OnlyWorlds.Sdk` | The 22 typed element models, `OWClient` for the API, `OWSync` for incremental `/changes`. |
| **Cache** | `OnlyWorlds.Sdk` | `OWWorldCache`, a whole world as a ScriptableObject: offline, inspectable, survives domain reloads. `OWSync.BaselineAsync(client, cache)` fills it from the API; `OWFolderLoader.LoadInto(cache, path)` fills it from a world folder on disk, `OWEnvelopeLoader.LoadInto(cache, path)` from the one-file export an owner downloads from onlyworlds.com. In the Editor, `OWCacheAsset.LoadOrCreate` saves it as an asset. |
| **Folders** | `OnlyWorlds.Sdk` | `OWFolderExport.ExportAsync(client, folder)` writes the key's world as a world folder on disk (folder format v0.3.21: its `world.json` says where it came from, which slice the key reads and which tool made it, keeps the world's first import (`origin`) as the server serves it, an element the key only knows of is written as the stub it is, and a body served with links the key may not see keeps its `x_served_trimmed` mark as served); `ExportFileAsync(path, folder)` writes the one-file export the same way. |
| **World Browser** | `OnlyWorlds.Sdk.Editor` | A three-panel browser for a world, live or offline: **Window → OnlyWorlds → World Browser**. |

The models follow the OnlyWorlds schema; `OWSchemaPin` names the schema version they were generated from.

## Opening `ow://` links in your Editor or game

`OWLinkListener` lets an address link clicked outside Unity reach your running Editor or player:

```csharp
var listener = new OWLinkListener(47615, reading => Select(reading.World, reading.Type, reading.Id));
if (!listener.TryStart(out var refusal)) Debug.Log($"No link listener: {refusal}"); // port_taken: another Editor has it
// ...
listener.Stop();
```

It listens on 127.0.0.1 only, never another interface, reads one line per connection (the link, at most 4096
bytes), and can do nothing but hand you an `OWAddressReading`, on the main thread. A line the address profile refuses
is answered with the refusal and goes no further.

For a click in a browser to arrive, the operating system needs a handler for `ow://`. The package ships one for
Windows in `Tools~/ow-link` (Unity ignores the folder; find it under the package's path). It is opt-in and per user:
nothing is registered until you run its installer, which writes only `HKCU\Software\Classes\ow` and needs no
administrator. Copy `ow-link.example.json`, set your ports and your player, then:

```
powershell -ExecutionPolicy Bypass -File install.ps1 -Config my-ow-link.json
```

To remove it, with nothing left behind:

```
powershell -ExecutionPolicy Bypass -File "%LOCALAPPDATA%\OnlyWorlds\ow-link\install.ps1" -Uninstall
```

This is for local development: no public page offers it.

## Open with

The rows of an "Open with" menu for a world or one of its elements: the site's page first, then each listed tool that
has a link for it, in the tools list's order, each with the word it says when it cannot open now. `OWTools.All` is the
[OnlyWorlds tools list](https://github.com/OnlyWorlds/tools) as of this release.

```csharp
var surface = new OWOpenerSurface { Audience = OWOpenerSurface.Member, SignedIn = null, Browser = false };
foreach (var row in OWOpeners.For(OWTools.All, OWOpeners.Element, OWAddress.ParseLink(link), surface))
    AddMenuItem(row.Name, row.Href, row.State); // State: null, or e.g. "Opens in your browser"
```

The surface says what your game knows and nothing is guessed: `Audience` (`member` or `public`; the public sees only
rows that need nothing first), `SignedIn` (null when you cannot know, and then no row says so), `Browser` (false in a
game or a desktop app, which cannot see the browser's sign-in), `Phone`, `Online` (false with no connection, as
`Application.internetReachability` reports it: every row whose link is a web address then says "Needs a connection",
and keeps its href), and `Tool`, your own client name, whose row says "You are here". A refused address throws `OWAddressException` with its reason; the rest throws
`ArgumentException` naming what is wrong. The TypeScript SDK's `openersFor` and the Python package's `openers_for` give
the same rows.

## Things that will bite you

**Only `name` is required.** Every other field is optional, and the API sends its empty value explicitly (`null`,
`""` or `[]`) rather than leaving the key out.

**`""` is a string's unset.** Text fields have no `null` on the wire. Models start every string at `""`, and writes
send `""` where a string holds `null`. Test with `string.IsNullOrEmpty`, never `== null`. Links differ: an unset
single link is `null`, because `""` is not an id.

**`null` is not `0`.** The schema has about 70 nullable integers, and `SerializableNullable<T>` keeps three states
apart: unset, a deliberate zero, and absent. A level-0 character and a character whose level is unknown are different
claims. It converts *from* `T` implicitly but not back, so reading one makes you handle the maybe. The Inspector shows
unset as `--`, never as `0`.

**PATCH replaces the fields it receives.** Send only what changed. `OWEdit.Begin(element)` snapshots an element;
change fields and call `CommitAsync`, and it sends exactly those. Sending a whole object you fetched an hour ago
undoes an hour of someone else's work. For relationships, use `EditLinksAsync`: PATCHing a link array to add one
item replaces the whole array, while `EditLinksAsync` adds and removes atomically on the server.

**Fields this SDK doesn't model are kept.** A field a model doesn't declare (another tool's `x_*` state, say) lands
in the element's extension bag and is written back unchanged: through a read-modify-write, the cache, and Unity's own
serializer.

**An AI agent says so.** A client an agent drives sets `Agent = true` on `OWClientConfig`, and its writes are
recorded as agent-made. Every element carries `ChangedBy` and `ChangedVia` (the door of its last write;
`OWWriteDoor.IsAgent` reads it). Leave `Agent` off in anything a person uses: it describes who is writing.

**Bulk writes return HTTP 200 even when some items failed.** `BulkAsync` returns an `OWBulkResult`: check `Errors`
and `Failed`, or call `ThrowIfAnyFailed()`.

**Reading a world folder never writes to it.** `OWFolderReader` creates nothing and rewrites nothing. Files it can't
use are reported, not deleted, and one bad file doesn't cost you the rest of the world.

**Writing a world folder is a byte contract.** `OWFolderWriter` writes an element to
`elements/<type>/<slug>--<id-tail>.json` with LF line endings, UTF-8 without a BOM, two-space indent, one trailing
newline, and keys in the order they arrived, so the same world writes the same bytes on every machine. Numbers are
spelled as `JSON.stringify` spells them (`1`, never `1.0`). Renaming an element moves its file. Parse with
`OWFolderWriter.Parse` rather than `JObject.Parse` for anything you'll write back: `JObject.Parse` rewrites
timestamps into the local timezone, which changes every file.

**Five fields belong to the server**: `world`, `type`, `created_at`, `updated_at` and `change_seq`. They're stripped
from every write, so a body you read can be written straight back.

**`UnityWebRequest` runs on the main thread only.** Every request goes through `OWMainThread`, so a call made after
an `await` still lands on the right thread. Never call `.Result` or `.Wait()` on a request from the Editor: the
completion comes from the update loop, so blocking it deadlocks.

## Tests

EditMode, assembly `OnlyWorlds.Sdk.Tests.Editor`, no network needed.

The live-API tests in `Tests/Integration` stay out of normal runs twice over: the assembly compiles only with the
`OW_INTEGRATION_TESTS` define (set it for the Editor platform), and every test is `[Explicit]`. They need a key and a
network.

## More

- [The Unity page](https://onlyworlds.github.io/docs/development/unity) and [games and engines](https://onlyworlds.github.io/docs/development/games) in the OnlyWorlds docs
- [The OnlyWorlds standard](https://github.com/OnlyWorlds/OnlyWorlds)
- Questions and bugs: [issues on this repository](https://github.com/OnlyWorlds/unity-sdk/issues), read and answered
  by its maintainer ([who writes it](https://github.com/OnlyWorlds/unity-sdk#readme))

## Licence

MIT. See [LICENSE](https://github.com/OnlyWorlds/unity-sdk/blob/main/LICENSE). A security problem goes through
[private vulnerability reporting](https://github.com/OnlyWorlds/unity-sdk/security/advisories/new), not an issue.
