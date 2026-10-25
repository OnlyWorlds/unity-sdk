// The room profile: which elements a parlor or a workshop puts in a world, told apart by type and supertype together
// with the mark x_room_of (schema-dist profiles/room.md, serial 45; "trimmed and without regard to case" defined once
// at serial 47; the ninth pair, relation/Take, at serial 66).
//
// Ported from @onlyworlds/sdk src/room.ts (4.55.0, ca15043; the trim of 4.57.2), isRoomElement. The conformance
// fixture, profiles/room.fixture.json, is this file's own test (OWRoomTests), as it is the TypeScript SDK's and the
// Python package's. The pairs are written here and held equal to the fixture's by that test, where npm generates them.

using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary>Tells a room element (a parlor's message, a workshop's work item) from the world's lore.</summary>
    public static class OWRoom
    {
        /// <summary>The mark a room element carries: the id of the world or the sitting the room belongs to.</summary>
        public const string MarkField = "x_room_of";

        /// <summary>The profile's nine pairs, a type slug and a supertype, in the fixture's order.</summary>
        public static readonly IReadOnlyList<(string type, string supertype)> Pairs = new[]
        {
            ("narrative", "Message"),
            ("narrative", "Board"),
            ("relation", "Work item"),
            ("construct", "Spec"),
            ("relation", "Heart"),
            ("relation", "Take"),
            ("relation", "Vote"),
            ("event", "Sitting"),
            ("character", "Member"),
        };

        /// <summary>
        /// True when the element is a room element: its <c>type</c> and <c>supertype</c>, each trimmed and compared
        /// without regard to case, match one of <see cref="Pairs"/>, and it carries <see cref="MarkField"/> as a
        /// non-empty string. Both, always.
        /// </summary>
        /// <param name="element">An element body that names its <c>type</c> (a slug); null is no element.</param>
        /// <remarks>
        /// <para>
        /// A pair without the mark is lore (a novel's letters are Narratives with supertype Message), and a mark
        /// without a pair is not a room element (a malformed mark: report it, never hide it). The pair match is the
        /// whole word, so <c>Messages</c> is lore, and a missing, null or blank supertype is never a room element.
        /// </para>
        /// <para>
        /// What it is for: a lore view (a list, a count, a search, a map, a story's context) leaves room elements out
        /// unless the person asks to see them; the room's own views, and an element opened by its id, show them whole.
        /// A tool that writes the world writes room elements like any other, the mark kept as it is: the rule is the
        /// reader's.
        /// </para>
        /// <para>
        /// Trimmed is Unicode White_Space at both ends, so a value with a leading byte order mark is not a match; case
        /// is folded by Unicode simple case folding from a table, so every runtime answers the same.
        /// </para>
        /// </remarks>
        public static bool IsRoomElement(JObject element)
        {
            if (element == null) return false;
            if (!(element["type"] is JValue t && t.Type == JTokenType.String)) return false;
            if (!(element["supertype"] is JValue s && s.Type == JTokenType.String)) return false;
            if (!(element[MarkField] is JValue m && m.Type == JTokenType.String) || ((string)m).Length == 0) return false;

            var type = OWProfileText.Fold((string)t);
            var supertype = OWProfileText.Fold((string)s);
            foreach (var pair in Pairs)
            {
                if (OWProfileText.Fold(pair.type) == type && OWProfileText.Fold(pair.supertype) == supertype)
                    return true;
            }

            return false;
        }
    }
}
