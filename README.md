# OnlyWorlds Unity SDK

Read and write [OnlyWorlds](https://onlyworlds.com) worlds from Unity.

This repository is a Unity 6 project that **embeds the package** it publishes. The package lives at
[`Packages/com.onlyworlds.sdk`](Packages/com.onlyworlds.sdk) — that folder is the product; the
surrounding project is its test bed.

## Install into your own project

Package Manager → **+ → Add package from git URL**:

```
https://github.com/OnlyWorlds/unity-sdk.git?path=/Packages/com.onlyworlds.sdk
```

See the [package README](Packages/com.onlyworlds.sdk/README.md) for usage, and the
[CHANGELOG](Packages/com.onlyworlds.sdk/CHANGELOG.md) for what is in each version.

## Working on the SDK itself

Open this repository as a Unity project (6000.0+). The package is embedded, so edits are live —
no reimport dance.

Tests: **Window → General → Test Runner → EditMode**, assembly `OnlyWorlds.Sdk.Tests.Editor`.
85 tests, no network required.

Live-API smoke tests are in `Tests/Integration` and are gated twice, deliberately:

1. The assembly carries an `OW_INTEGRATION_TESTS` define constraint, so it does not compile at all
   by default. It is an **Editor** assembly (`includePlatforms: ["Editor"]`), so add the define
   under **Project Settings → Player → Scripting Define Symbols** for the *Editor* platform — the
   player build target's symbols do not reach it.
2. Every test is also `[Explicit]`, so even once compiled they are skipped by a Run All. Select and
   run them individually.

They need a real key and a network. Both gates are there because a suite that fails for credential
or connectivity reasons stops being believed.

## What OnlyWorlds is

An open standard for portable world data: 22 element types, UUID-linked, tool-neutral. The schema is
governed publicly at [OnlyWorlds/OnlyWorlds](https://github.com/OnlyWorlds/OnlyWorlds); this SDK is
one consumer of it, alongside the [TypeScript SDK](https://github.com/OnlyWorlds/sdk).

## Status

Early. Public and real, but not yet marketed and carrying no compatibility promise. The 22 element
models are currently 3 hand-written proving models, pending a code generator that emits all of them
from the canonical schema.
