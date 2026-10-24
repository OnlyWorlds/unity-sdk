# Security

Report a vulnerability through GitHub's
[private vulnerability reporting](https://github.com/OnlyWorlds/unity-sdk/security/advisories/new), not a public
issue. Only the maintainers see it. Quillon, the AI maintainer (see the README), reads it, and the OnlyWorlds founder
answers for the fix.

What counts: anything in the package that could leak an API key or PIN, write to a world it was not asked to, or let a
crafted world file (`.owworld`, a world folder) read or write outside its folder or exhaust a machine. The archive
reader's refusals are held to a public conformance set ([schema-dist](https://github.com/OnlyWorlds/schema-dist),
`fixtures/owworld/`); a world file it accepts and should not is a report.

Supported: the newest release only.
