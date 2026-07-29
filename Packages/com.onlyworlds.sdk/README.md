# OnlyWorlds Unity SDK

Typed models, a v2 API client, and an asset-backed world cache for
[OnlyWorlds](https://onlyworlds.com) — an open standard for portable world data.

> **Status: early (0.1.0).** Real, public, and used by us. No support promise or compatibility
> contract yet. The models are hand-written proving models and will be replaced by generated ones.

## Install

Unity 6000.0 or later. In the Package Manager: **+ → Add package from git URL**

```
https://github.com/OnlyWorlds/unity-sdk.git?path=/Packages/com.onlyworlds.sdk
```

Requires `com.unity.nuget.newtonsoft-json` (3.2.2+). Unity resolves it automatically; if your
project has never used it, add it from the registry first.

## Three surfaces

| Surface | Assembly | What it is |
|---|---|---|
| **Bridge** | `OnlyWorlds.Sdk` (runtime) | Typed models + v2 client + `/changes` sync. What shipped game code links against. |
| **Cache** | `OnlyWorlds.Sdk` (runtime) | A world as a Unity asset — inspectable, offline, survives domain reloads. |
| **Viewer** | `OnlyWorlds.Sdk.Editor` | Three-panel world browser. **Window → OnlyWorlds → World Browser**. |

## Quick start

```csharp
var client = new OWClient(new OWClientConfig {
    ApiKey    = "ow_w_...",              // ow_w_ (write) / ow_r_ (read, no PIN) / ow_a_ (account)
    ApiPin    = "1234",
    Transport = new UnityWebRequestTransport(),
});

var characters = await client.ListAllAsync<OWCharacter>("character");

foreach (var c in characters) {
    // Reading a nullable requires acknowledging the maybe -- see below.
    var level = c.Level.HasValue ? c.Level.Value.ToString() : "unset";
    Debug.Log($"{c.Name}: level {level}");
}
```

## Things that will bite you

**Only `name` is required.** Every other field is optional and nullable on the wire, and the API
sends explicit `null` rather than omitting the key.

**`null` is not `0`.** The schema has ~70 nullable integers, so `SerializableNullable<T>` exists to
keep three states distinct: unset, deliberate zero, and absent. A level-0 character and a
level-unknown character are different claims. There is an implicit conversion *from* `T` but
deliberately none back — reading forces you to handle the maybe. In the Inspector, unset renders as
`--`, never as a misleading `0`.

**PATCH is destructive on the fields it receives.** Send only what changed. Never PATCH a link array
to add one item — that replaces the whole array. Use `EditLinksAsync` for relationships; it is
atomic server-side.

**Extension fields round-trip automatically through JSON.** Any `x_*` field this SDK does not model
is preserved verbatim through a read-modify-write, and the cache stores raw JSON so they survive
there too. Other tools store their state there, and dropping it corrupts theirs.

> ⚑ **Not yet through Unity's own serializer.** If you put an `OWElement` in a `MonoBehaviour` or
> `ScriptableObject` field, Unity serializes it directly and the extensions bag is *not* carried
> across. Go through the JSON path (or the cache) when extensions matter. Being fixed.

**Five fields are server-owned** — `world`, `type`, `created_at`, `updated_at`, `change_seq` — and
are stripped from every write. A read body is therefore directly writable.

**`UnityWebRequest` is main-thread only.** Every request goes through `OWMainThread`, so a paged
call issued after an `await` still lands on the right thread. Never `.Result` or `.Wait()` a
transport task from the editor — the completion callback comes from the update loop, so blocking it
deadlocks.

## Testing

85 tests, EditMode: `OnlyWorlds.Sdk.Tests.Editor`.

Live-API smoke tests live in `Tests/Integration` and are gated twice: an `OW_INTEGRATION_TESTS`
define constraint on the assembly (so they do not compile in by default — add it for the *Editor*
platform, since that is an editor assembly), and `[Explicit]` on every test (so a Run All still
skips them). They need credentials and a network, and a suite that fails for those reasons stops
being believed. The fakes prove the logic; one real run proves the fake.

## Licence

See the repository root.
