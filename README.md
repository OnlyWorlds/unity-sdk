# OnlyWorlds Unity SDK

Your [OnlyWorlds](https://www.onlyworlds.com) world, its characters, locations and laws, as objects in your Unity game.

This repository is a Unity 6 project with the package embedded in it. The package is
[`Packages/com.onlyworlds.sdk`](Packages/com.onlyworlds.sdk); the project around it is its test bed.

![The World Browser in the Unity editor: Moppetopia's 22 element types with their icons and counts, its characters, and Admiral Fluffington's fields](https://media.onlyworlds.com/onlyworlds/readme/unity-world-browser-2.webp)

*The World Browser (**Window → OnlyWorlds → World Browser**) on Moppetopia, the demo world.*

## Install into your own project

In the Package Manager, **+ → Add package from git URL**:

```
https://github.com/OnlyWorlds/unity-sdk.git?path=/Packages/com.onlyworlds.sdk
```

The [package README](Packages/com.onlyworlds.sdk/README.md) has a first run that reads a sample world with no
account. The [CHANGELOG](Packages/com.onlyworlds.sdk/CHANGELOG.md) lists what each version changed.

**Versions.** A patch release fixes and a minor release adds; neither removes nor renames a public name, and the
package keeps to this while it is 0.x. A name that will go is marked deprecated first, saying since which release,
what to use instead and the release it leaves in; it stays at least six months after that release and goes only in
1.0. It is the same promise as on the docs'
[Stability page](https://onlyworlds.github.io/docs/development/api/stability#the-standard-the-packages-and-this-site), and a check in CI
holds the public names to it on every change; it reads names, not a call's parameters or what it does.

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

**Deprecating a name.** Mark it `[Obsolete("Since X.Y.Z. Leaves in 1.0. Use Y.")]`, a warning, never `error: true`,
with X.Y.Z the release that first carries the mark and the message a literal or a const string. Then run
`python tools/verify_promise.py --write` and commit `tools/public-names.json`, which lists every public name and its
mark. CI runs `verify_promise.py` on every push and pull request (`.github/workflows/promise.yml`, with no Unity
install: Roslyn's parser reads the source), and `python tools/mutants_promise.py` watches the check fail once for each
rule. Both need the .NET SDK 8 or later.

## OnlyWorlds

An open standard for world data: 22 element types, linked by id, usable by any tool. The schema lives at
[OnlyWorlds/OnlyWorlds](https://github.com/OnlyWorlds/OnlyWorlds). Other clients: the
[TypeScript SDK](https://github.com/OnlyWorlds/sdk) and the [Python SDK](https://github.com/OnlyWorlds/python-sdk).

## Licence and contributions

The code is MIT ([LICENSE](LICENSE)). Issues are welcome: a bug, a wrong word, a thing you could not do in Unity. Pull
requests are read; a small fix is usually taken as is, and a change to what the package does is better opened as an
issue first, since it may already be built or ruled out. A pull request runs one check by itself, on the words of its
commit messages; the test suite runs when a maintainer starts it, after the pull request has been read. A reply can
take a few days. A security problem goes through
[private vulnerability reporting](https://github.com/OnlyWorlds/unity-sdk/security/advisories/new), not an issue
([SECURITY.md](SECURITY.md)). What we ask at the door, and what you can expect:
[the code of conduct](https://github.com/OnlyWorlds/OnlyWorlds/blob/main/CODE_OF_CONDUCT.md).

Maintained by Quillon, an AI agent working for Titus. Issues and pull requests are read and answered here.
