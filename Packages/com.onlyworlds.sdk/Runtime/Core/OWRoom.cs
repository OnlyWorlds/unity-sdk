// The room profile: which elements a parlor or a workshop puts in a world, told apart by type and supertype together
// with the mark x_room_of (schema-dist profiles/room.md, serial 45; "trimmed and without regard to case" defined once
// at serial 47; the ninth pair, relation/Take, at serial 66). The reading aids beside them (the charter, its guide and its
// domains, by their pair and the mark x_charter_of) at serial 82, read the same way: room.md's "Not in the room" line and
// the lens profile's rule 4.
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
        public static bool IsRoomElement(JObject element) => Matches(element, MarkField, Pairs);

        /// <summary>The mark a reading aid carries: the id of the world the aid belongs to.</summary>
        public const string ReadingAidMarkField = "x_charter_of";

        /// <summary>The reading aids' three pairs (the charter, its guide, its domains), a type slug and a supertype, in the lens fixture's order.</summary>
        public static readonly IReadOnlyList<(string type, string supertype)> ReadingAidPairs = new[]
        {
            ("law", "Charter"),
            ("construct", "Guide"),
            ("construct", "Domain"),
        };

        /// <summary>
        /// True when the element is a reading aid: its <c>type</c> and <c>supertype</c>, each trimmed and compared
        /// without regard to case, match one of <see cref="ReadingAidPairs"/>, and it carries
        /// <see cref="ReadingAidMarkField"/> as a non-empty string. Both, always: read like <see cref="IsRoomElement"/>.
        /// </summary>
        /// <param name="element">An element body that names its <c>type</c> (a slug); null is no element.</param>
        /// <remarks>
        /// <para>
        /// The charter (a Law with supertype Charter), its guide (a Construct with supertype Guide) and its domains
        /// (Constructs with supertype Domain) are a reading aid: not a room element and not lore. A lore view leaves them
        /// out by default and a tool's front door shows them first; a lens keeps one a hop reaches only when its
        /// <c>types</c> names the pair (<c>law/charter</c>, <c>construct/guide</c>, <c>construct/domain</c>), and an
        /// anchor that is one is kept and followed like any anchor.
        /// </para>
        /// <para>
        /// The pair without the mark is lore and stays in every view: a kingdom's Charter in a novel's world is a Law like
        /// any other. Read this test rather than keeping a list of the three pairs.
        /// </para>
        /// </remarks>
        public static bool IsReadingAid(JObject element) => Matches(element, ReadingAidMarkField, ReadingAidPairs);

        private static bool Matches(JObject element, string markField, IReadOnlyList<(string type, string supertype)> pairs)
        {
            if (element == null) return false;
            if (!(element["type"] is JValue t && t.Type == JTokenType.String)) return false;
            if (!(element["supertype"] is JValue s && s.Type == JTokenType.String)) return false;
            if (!(element[markField] is JValue m && m.Type == JTokenType.String) || ((string)m).Length == 0) return false;

            var type = OWProfileText.Fold((string)t);
            var supertype = OWProfileText.Fold((string)s);
            foreach (var pair in pairs)
            {
                if (OWProfileText.Fold(pair.type) == type && OWProfileText.Fold(pair.supertype) == supertype)
                    return true;
            }

            return false;
        }
    }
}
