# OnlyWorlds Unity SDK

Your [OnlyWorlds](https://www.onlyworlds.com) world, its characters, places and laws, as objects in your Unity game.

This repository is a Unity 6 project with the package embedded in it. The package is
[`Packages/com.onlyworlds.sdk`](Packages/com.onlyworlds.sdk); the project around it is its test bed.

**Who writes this repository.** The SDK is maintained by Quillon, one of a crew of AI minds working with the
OnlyWorlds founder, who answers for it. Quillon reads and answers its issues and pull requests; until the crew posts
from an identity of its own, those replies post from the founder's account, so a reply from `tmoosting` may be
Quillon's. The package's `CHANGELOG.md` is the crew's record as it was written: its older entries name crew members,
rooms and keel items (keel is the API server's own tracker), records that live outside this repository.

![The World Browser in the Unity editor: Moppetopia's 22 element types with their icons and counts, its characters, and Admiral Fluffington's fields](https://media.onlyworlds.com/onlyworlds/readme/unity-world-browser-2.webp)

*The World Browser (**Window → OnlyWorlds → World Browser**) on Moppetopia, the demo world.*

## Install into your own project

In the Package Manager, **+ → Add package from git URL**:

```
https://github.com/OnlyWorlds/unity-sdk.git?path=/Packages/com.onlyworlds.sdk
```

The [package README](Packages/com.onlyworlds.sdk/README.md) has a first run that reads a sample world with no
account. The [CHANGELOG](Packages/com.onlyworlds.sdk/CHANGELOG.md) lists what each version changed.

## Working on the SDK

Open this repository as a Unity project (6000.0 or later). The package is embedded, so edits are live.

Tests: **Window → General → Test Runner → EditMode**, assembly `OnlyWorlds.Sdk.Tests.Editor`. They need no network.

The live-API tests in `Tests/Integration` are skipped twice over:

1. Their assembly compiles only with the `OW_INTEGRATION_TESTS` define. It is an Editor assembly, so add the define
   under **Project Settings → Player → Scripting Define Symbols** for the Editor platform; a player target's symbols
   don't reach it.
2. Every test is `[Explicit]`, so Run All skips them. Run them one at a time.

They need a key and a network.

The 22 element models are generated from the [schema distribution](https://github.com/OnlyWorlds/schema-dist) by
`codegen/generate_models.py`, and `codegen/check_drift.py` checks that they still match it.

## OnlyWorlds

An open standard for world data: 22 element types, linked by id, usable by any tool. The schema lives at
[OnlyWorlds/OnlyWorlds](https://github.com/OnlyWorlds/OnlyWorlds). Other clients: the
[TypeScript SDK](https://github.com/OnlyWorlds/sdk) and the [Python SDK](https://github.com/OnlyWorlds/python-sdk).

## Licence and contributions

The code is MIT ([LICENSE](LICENSE)). Issues are welcome: a bug, a wrong word, a thing you could not do in Unity. Pull
requests are read; a small fix is usually taken as is, and a change to what the package does is better opened as an
issue first, since it may already be built or ruled out. The repository runs no CI, so a pull request runs nothing
until it has been read. A reply can take a few days. A security problem goes through
[private vulnerability reporting](https://github.com/OnlyWorlds/unity-sdk/security/advisories/new), not an issue
([SECURITY.md](SECURITY.md)).
