// The names profile: a world's own words for the 22 types, `x_names` on the World (schema-dist profiles/names.md,
// serial 48). A display lookup only: the API, the folder, the schema and every id keep the standard's names, and a
// folder's `elements/<type>/` directories are never translated.
//
// Ported from @onlyworlds/sdk src/names.ts (names-profile, commit 7447831), typeWord. The conformance fixture,
// profiles/names.fixture.json, is this file's own test (OWNamesTests), as it is the TypeScript SDK's and the Python
// package's: every reader refuses and ignores the same things, or one tool shows Employee where another shows
// Character.

using System;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary>A world's own words for the types (the <c>x_names</c> profile), over the World as a JObject.</summary>
    public static class OWNames
    {
        /// <summary>The World's field that holds the names map.</summary>
        public const string NamesField = "x_names";

        /// <summary>
        /// The world's own word for a type, or null for the standard's name (print your usual label).
        /// </summary>
        /// <param name="world">A <c>GET /world</c> body or a folder's <c>world.json</c>; its <c>x_names</c> is read.
        /// Null reads as no map.</param>
        /// <param name="type">A type slug, lowercase as the schema spells it (<c>character</c>).</param>
        /// <param name="supertype">The element's supertype, when there is one.</param>
        /// <param name="many">True for a plural: a list's heading, a count.</param>
        /// <remarks>
        /// <para>
        /// The order: the supertype's entry, its key matched trimmed and without regard to case (the room profile's one
        /// definition); then the type's entry; then null. An entry with no string <c>one</c> left after trimming is
        /// ignored, and the lookup goes on to the next step. For a plural, a <c>many</c> that is absent, not a string
        /// or blank prints <c>one</c>. Both are trimmed.
        /// </para>
        /// <para>
        /// A value that is not a names map is null everywhere: not an object, <c>version</c> not the number 1
        /// (<c>true</c> included), <c>types</c> or <c>supertypes</c> present and not objects, or a
        /// <c>supertypes</c> value that is not an object. A bad entry or a key that is not one of the 22 slugs is
        /// ignored. The word is printed as written, never re-cased. Never throws.
        /// </para>
        /// </remarks>
        public static string TypeWord(JObject world, string type, string supertype = null, bool many = false)
        {
            if (!(world?[NamesField] is JObject map) || !IsVersionOne(map["version"])) return null;

            var types = map["types"];
            if (types != null && !(types is JObject)) return null;
            var supertypes = map["supertypes"];
            if (supertypes != null)
            {
                if (!(supertypes is JObject bySlug)) return null;
                foreach (var inner in bySlug.Properties())
                {
                    if (!(inner.Value is JObject)) return null;
                }
            }

            if (type == null || Array.IndexOf(OWElementTypes.Slugs, type) < 0) return null;

            var folded = supertype == null ? string.Empty : OWProfileText.Fold(supertype);
            if (folded.Length > 0 && supertypes?[type] is JObject entries)
            {
                foreach (var entry in entries.Properties())
                {
                    if (OWProfileText.Fold(entry.Name) != folded) continue;
                    var w = Word(entry.Value, many);
                    if (w != null) return w;
                }
            }

            return types is JObject byType ? Word(byType[type], many) : null;
        }

        /// <summary>The word a pair prints, or null when the entry is ignored (no string <c>one</c> after trimming).</summary>
        private static string Word(JToken pair, bool many)
        {
            if (!(pair is JObject obj) || !(obj["one"] is JValue oneValue) || oneValue.Type != JTokenType.String) return null;
            var one = OWProfileText.TrimWhiteSpace((string)oneValue);
            if (one.Length == 0) return null;
            if (!many) return one;

            var plural = obj["many"] is JValue manyValue && manyValue.Type == JTokenType.String
                ? OWProfileText.TrimWhiteSpace((string)manyValue)
                : string.Empty;
            return plural.Length == 0 ? one : plural;
        }

        /// <summary>
        /// The JSON number 1, as JavaScript's <c>=== 1</c> reads it (so <c>1.0</c> too). Not a cast: Newtonsoft
        /// converts the boolean <c>true</c> to 1, and the profile refuses it.
        /// </summary>
        private static bool IsVersionOne(JToken version)
        {
            if (!(version is JValue v)) return false;
            switch (v.Type)
            {
                case JTokenType.Integer:
                    return v.Value is long l ? l == 1 : v.Value is int i && i == 1;
                case JTokenType.Float:
                    return v.Value is double d ? d == 1.0 : v.Value is decimal m && m == 1m;
                default:
                    return false;
            }
        }
    }
}
