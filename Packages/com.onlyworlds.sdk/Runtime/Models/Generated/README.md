GENERATED CODE -- DO NOT EDIT ANY FILE IN THIS DIRECTORY.

Every .cs file here is emitted by codegen/generate_models.py from the vendored,
hash-verified OnlyWorlds schema distribution under codegen/schema-dist/.

Regenerate on a schema-pin bump:

    python codegen/generate_models.py

Check for drift (CI, and before any push that touches the pin):

    python codegen/check_drift.py

An edit made here is lost on the next regeneration, silently and without a
conflict. If a model needs to be different, the emitter changes; if the SCHEMA
needs to be different, that is a Council motion, and if a semantic CONVENTION
needs to change, that is a row in walk/rulings.yaml -- not a hand edit here.

Hand-written code that extends a generated model belongs in a partial class in
Runtime/Models/, outside this folder.
