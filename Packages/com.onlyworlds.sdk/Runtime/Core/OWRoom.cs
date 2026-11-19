// The room profile: which elements a parlor or a workshop puts in a world, told apart by type and supertype together
// with the mark x_room_of (schema-dist profiles/room.md, serial 45; "trimmed and without regard to case" defined once
// at serial 47; the ninth pair, relation/Take, at serial 66; the tenth, narrative/Report, at serial 112, the report
// profile's; a talk on an Object or a Construct, the lines that name it, at serial 135). The reading aids beside them (the charter, its guide and its domains, by their pair and the mark
// x_charter_of) at serial 82: room.md's "Not in the room" line and the lens profile's rule 4. Both marks are read by one rule, keel's: present and not null, a string only when something is left after
// trimming White_Space (x_charter_of since isCharterElement, 4.98.0; x_room_of since serial 83's ruling).
//
// Ported from @onlyworlds/sdk src/room.ts (4.55.0, ca15043; the trim of 4.57.2), isRoomElement. The conformance
// fixture, profiles/room.fixture.json, is this file's own test (OWRoomTests), as it is the TypeScript SDK's and the
// Python package's. The pairs are written here and held equal to the fixture's by that test, where npm generates them.

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary>Tells a room element (a parlor's message, a workshop's work item) from the world's lore.</summary>
    public static class OWRoom
    {
        /// <summary>The mark a room element carries: the id of the world or the sitting the room belongs to.</summary>
        public const string MarkField = "x_room_of";

        /// <summary>The profile's ten pairs, a type slug and a supertype, in the fixture's order.</summary>
        public static readonly IReadOnlyList<(string type, string supertype)> Pairs = new[]
        {
            ("narrative", "Message"),
            ("narrative", "Board"),
            ("narrative", "Report"),
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
        /// without regard to case, match one of <see cref="Pairs"/>, and it carries <see cref="MarkField"/>, present and
        /// not null. Both, always. The mark is read as the API reads it and as <see cref="IsCharterElement"/> reads its
        /// own: a string mark counts only when something is left after trimming White_Space (so <c>"  "</c> is no mark),
        /// an empty list or an empty object is no mark (serial 126), any other JSON value but null counts (so <c>7</c>
        /// and <c>false</c> are one), and the value is never compared.
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
        public static bool IsRoomElement(JObject element) => HasMark(element, MarkField) && Matches(element, Pairs);

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
        /// <see cref="ReadingAidMarkField"/>, present and not null. Both, always. The mark and the pair are read as
        /// <see cref="IsRoomElement"/> reads its own: a string mark counts only when something is left after trimming
        /// White_Space (so <c>"  "</c> is no mark), and any other JSON value but null counts (so <c>7</c> is one). The
        /// TypeScript SDK's <c>isCharterElement</c> and the Python package's <c>is_charter_element</c> answer the same.
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
            HasMark(element, ReadingAidMarkField) && Matches(element, ReadingAidPairs);

        /// <summary>
        /// A mark (<see cref="MarkField"/> or <see cref="ReadingAidMarkField"/>) as the API reads it: present and not
        /// null; a string only when something is left after trimming White_Space; a list or an object only when it holds
        /// something. Any other JSON value (a number, a boolean) is a mark. The value is never compared.
        /// </summary>
        /// <remarks>
        /// One presence for every mark read by presence (schema-dist serial 126): the review mark's own
        /// (<see cref="OWPayload.IsEmptyMark"/>), so an empty list or object is no mark, since a compact reader leaves
        /// both out and the same element must read the same in either served form.
        /// </remarks>
        internal static bool HasMark(JObject element, string field) => !OWPayload.IsEmptyMark(element?[field]);

        /// <summary>
        /// True when the element is a room element of one pair: <see cref="IsRoomElement"/>'s test (the pair trimmed and
        /// folded, the mark by presence) against that pair alone. The report profile's threads tell a report
        /// (<c>narrative</c> / <c>Report</c>) from a board entry (<c>narrative</c> / <c>Board</c>) by it.
        /// </summary>
        internal static bool IsRoomElementOf(JObject element, (string type, string supertype) pair) =>
            HasMark(element, MarkField) && Matches(element, new[] { pair });

        /// <summary>The pair a line in the room is: type <c>narrative</c>, supertype <c>Message</c>. One of <see cref="Pairs"/>.</summary>
        public static readonly (string type, string supertype) LinePair = ("narrative", "Message");

        /// <summary>
        /// The two fields that put a line in a talk on an element, each with the type its ids are read as (serial 135):
        /// <c>objects</c> names Objects, <c>constructs</c> Constructs. No other field does: a line's <c>characters</c>
        /// are its recipients, never what it is about.
        /// </summary>
        public static readonly IReadOnlyList<(string field, string type)> TalkFields = new[]
        {
            ("objects", "object"),
            ("constructs", "construct"),
        };

        /// <summary>
        /// The talks on an element this element is in as a line (the room profile's "a talk on an Object or a
        /// Construct", serial 135): each <c>(type, id)</c> its <c>objects</c> (<c>object</c>) and <c>constructs</c>
        /// (<c>construct</c>) name, read from the element alone. Empty when it is no line: a line is a room element of
        /// <see cref="LinePair"/> with the mark, so a Message without the mark, a board entry or a report is in no talk.
        /// </summary>
        /// <param name="element">An element body as served; null is no element.</param>
        /// <remarks>
        /// <para>
        /// A field that is not a list names nothing, and in a list an entry that is not a string, or is empty once
        /// trimmed of White_Space, names nothing. Every other entry is an id read as written, a space left on it
        /// included and never folded to lowercase (the address profile's "The same id"), so <c>ABC</c> and <c>abc</c>
        /// are two talks. Each <c>(type, id)</c> once, objects first, each list in its own order; the profile compares
        /// the answer as a set.
        /// </para>
        /// <para>
        /// The talk on an element is every line that names it, whichever sitting it was said in. A line in a talk may
        /// also hang on a board entry or a work item (the report profile's anchors), and is then in both: this answers
        /// only the talks on an element. How a tool orders, groups or labels a talk's lines is the tool's own.
        /// </para>
        /// </remarks>
        public static IReadOnlyList<(string type, string id)> TalksOf(JObject element) =>
            IsRoomElementOf(element, LinePair) ? TalkElementsOf(element) : Array.Empty<(string, string)>();

        /// <summary>
        /// The <c>(type, id)</c> an element's <see cref="TalkFields"/> name, read as <see cref="TalksOf"/> reads a line's,
        /// whatever the element is. The report profile reads a report's links by it.
        /// </summary>
        internal static IReadOnlyList<(string type, string id)> TalkElementsOf(JObject element)
        {
            var talks = new List<(string type, string id)>();
            if (element == null) return talks;
            var seen = new HashSet<(string, string)>();
            foreach (var (field, type) in TalkFields)
            {
                if (!(element[field] is JArray list)) continue;
                foreach (var entry in list)
                {
                    if (!(entry is JValue value && value.Type == JTokenType.String)) continue;
                    var id = (string)value;
                    if (OWProfileText.TrimWhiteSpace(id).Length == 0 || !seen.Add((type, id))) continue;
                    talks.Add((type, id));
                }
            }

            return talks;
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
