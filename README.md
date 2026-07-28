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
65 tests, no network required.

Live-API smoke tests are in `Tests/Integration` behind an `OW_INTEGRATION_TESTS` define and do not
compile by default. Add the define to Player Settings to enable them; they need a real key.

## What OnlyWorlds is

An open standard for portable world data: 22 element types, UUID-linked, tool-neutral. The schema is
governed publicly at [OnlyWorlds/OnlyWorlds](https://github.com/OnlyWorlds/OnlyWorlds); this SDK is
one consumer of it, alongside the [TypeScript SDK](https://github.com/OnlyWorlds/sdk).

## Status

Early. Public and real, but not yet marketed and carrying no compatibility promise. The 22 element
models are currently 3 hand-written proving models, pending a code generator that emits all of them
from the canonical schema.
