// The room profile: which elements a parlor or a workshop puts in a world, told apart by type and supertype together
// with the mark x_room_of (schema-dist profiles/room.md, serial 45; "trimmed and without regard to case" defined once
// at serial 47; the ninth pair, relation/Take, at serial 66). The reading aids beside them (the charter, its guide and its
// domains, by their pair and the mark x_charter_of) at serial 82: room.md's "Not in the room" line and the lens profile's
// rule 4. The two marks are read apart: x_room_of as a non-empty string, x_charter_of present and not null, a string
// only when not blank (isCharterElement, 4.98.0).
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
        public static bool IsRoomElement(JObject element) => HasRoomMark(element) && Matches(element, Pairs);

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
        /// <see cref="ReadingAidMarkField"/>, present and not null. Both, always. The mark is read as the API reads it: a
        /// string mark counts only when something is left after trimming White_Space (so <c>"  "</c> is no mark), and any
        /// other JSON value but null counts (so <c>7</c> is one). The pair is trimmed and folded as
        /// <see cref="IsRoomElement"/> reads its own. The TypeScript SDK's <c>isCharterElement</c> and the Python package's
        /// <c>is_charter_element</c> answer the same.
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
        public static bool IsCharterElement(JObject element) =>
            HasCharterMark(element) && Matches(element, ReadingAidPairs);

        /// <summary>
        /// The room mark as the room test reads it: a JSON string with at least one character (whitespace counts).
        /// </summary>
        private static bool HasRoomMark(JObject element) =>
            element?[MarkField] is JValue m && m.Type == JTokenType.String && ((string)m).Length > 0;

        /// <summary>
        /// The charter mark as the API reads it: present and not null; a string only when something is left after trimming
        /// White_Space. Any other JSON value (a number, a boolean, an object, a list) is a mark.
        /// </summary>
        private static bool HasCharterMark(JObject element)
        {
            var m = element?[ReadingAidMarkField];
            if (m == null || m.Type == JTokenType.Null || m.Type == JTokenType.Undefined) return false;
            return m.Type != JTokenType.String || OWProfileText.TrimWhiteSpace((string)m).Length > 0;
        }

        private static bool Matches(JObject element, IReadOnlyList<(string type, string supertype)> pairs)
        {
            if (element == null) return false;
            if (!(element["type"] is JValue t && t.Type == JTokenType.String)) return false;
            if (!(element["supertype"] is JValue s && s.Type == JTokenType.String)) return false;

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
