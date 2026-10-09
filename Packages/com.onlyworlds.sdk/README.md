# OnlyWorlds Unity SDK

Your [OnlyWorlds](https://www.onlyworlds.com) world, its characters, places and laws, as objects in your Unity game.

It reads and writes worlds through the OnlyWorlds API, with typed C# models for all 22 element types and a world
cache you can browse in the Editor.

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
builds you ship.

## What's in it

| Part | Assembly | What it does |
|---|---|---|
| **Client and models** | `OnlyWorlds.Sdk` | The 22 typed element models, `OWClient` for the API, `OWSync` for incremental `/changes`. |
| **Cache** | `OnlyWorlds.Sdk` | `OWWorldCache`, a whole world as a ScriptableObject: offline, inspectable, survives domain reloads. `OWSync.BaselineAsync(client, cache)` fills it from the API; `OWFolderLoader.LoadInto(cache, path)` fills it from a world folder on disk. In the Editor, `OWCacheAsset.LoadOrCreate` saves it as an asset. |
| **World Browser** | `OnlyWorlds.Sdk.Editor` | A three-panel browser for a world, live or offline: **Window → OnlyWorlds → World Browser**. |

The models follow the OnlyWorlds schema; `OWSchemaPin` names the schema version they were generated from.

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
- Questions: [GitHub Discussions](https://github.com/OnlyWorlds/OnlyWorlds/discussions)

## Licence

MIT. See [LICENSE](https://github.com/OnlyWorlds/unity-sdk/blob/main/LICENSE).
