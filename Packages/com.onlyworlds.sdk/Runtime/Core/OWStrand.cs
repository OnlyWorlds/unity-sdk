// The strand profile: a world in miniature, one story read off four links the standard already has (schema-dist
// profiles/strand.md, serial 98, ruled 2026-10-30). The World's mark x_strand names the story (a Narrative), one of its
// events and one of that event's places; the hero is the story's `protagonist` and where the hero is now is the hero's
// `location`.
//
// Written from the profile and its conformance fixture, profiles/strand.fixture.json, which is this file's own test
// (OWStrandTests); keel's reader (elements/strand.py, strand_of) and the packages' (the TypeScript SDK's strandOf, the
// Python package's strand_of) answer the same cases. Who sees what is the lens reader's own reading (OWLens's view),
// never a second copy.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary>A filled strand slot: the element's id and its name as the reader is served it.</summary>
    public sealed class OWStrandSlot
    {
        internal OWStrandSlot(string id, string name)
        {
            Id = id;
            Name = name;
        }

        /// <summary>The element's id.</summary>
        public string Id { get; }

        /// <summary>Its name as served; empty when it has none.</summary>
        public string Name { get; }

        /// <summary>The profile's <c>{id, name}</c>.</summary>
        public JObject ToJson() => new JObject { ["id"] = Id, ["name"] = Name };
    }

    /// <summary>
    /// A world's strand as one reader sees it (<see cref="Of"/>): five slots and whether then and now are one place. An
    /// empty slot is null, and an empty slot is what there is to make next, never an error.
    /// </summary>
    public sealed class OWStrand
    {
        /// <summary>The World's field that carries the mark: an object of three ids.</summary>
        public const string MarkField = "x_strand";

        /// <summary>The mark's three members, in the profile's order; only <c>narrative</c> is required.</summary>
        public static readonly IReadOnlyList<string> Members = new[] { "narrative", "event", "location" };

        private OWStrand(OWStrandSlot narrative, OWStrandSlot @event, OWStrandSlot location, OWStrandSlot character,
            OWStrandSlot now)
        {
            Narrative = narrative;
            Event = @event;
            Location = location;
            Character = character;
            Now = now;
            Same = now != null && location != null ? now.Id == location.Id : (bool?)null;
        }

        /// <summary>The story: the Narrative the mark names.</summary>
        public OWStrandSlot Narrative { get; }

        /// <summary>The happening: the Event the mark names, when it is in the story's <c>events</c>.</summary>
        public OWStrandSlot Event { get; }

        /// <summary>The place, then: the Location the mark names, when it is in the event's <c>locations</c>.</summary>
        public OWStrandSlot Location { get; }

        /// <summary>The hero: the story's <c>protagonist</c>.</summary>
        public OWStrandSlot Character { get; }

        /// <summary>The place, now: the hero's <c>location</c>.</summary>
        public OWStrandSlot Now { get; }

        /// <summary>
        /// True when <see cref="Now"/> and <see cref="Location"/> are both filled with one id, false when with two, null
        /// otherwise. Ids, never names: two places may share a name.
        /// </summary>
        public bool? Same { get; }

        /// <summary>True when no slot is filled: the world has not started its story, or this reader sees none of it.</summary>
        public bool IsEmpty => Narrative == null;

        /// <summary>The profile's <c>{narrative, event, location, character, now, same}</c>, an empty slot null.</summary>
        public JObject ToJson() => new JObject
        {
            ["narrative"] = Slot(Narrative),
            ["event"] = Slot(Event),
            ["location"] = Slot(Location),
            ["character"] = Slot(Character),
            ["now"] = Slot(Now),
            ["same"] = Same.HasValue ? new JValue(Same.Value) : JValue.CreateNull(),
        };

        /// <summary>
        /// A world's strand as <paramref name="reader"/> sees it: the profile's <c>strandOf(world, reader)</c>. One pure
        /// function over a world snapshot; it never writes, and never changes a body it is given.
        /// </summary>
        /// <param name="world">
        /// The World body (a <c>GET /world</c> answer or a folder's <c>world.json</c>); its <see cref="MarkField"/> is read
        /// as given. Null reads as no mark.
        /// </param>
        /// <param name="elements">
        /// The world's elements as its holder has them: the whole world (a folder, the owner's pull) for every rung but
        /// <see cref="OWLensRung.Served"/>, whose bodies are already a key's served view; a hidden one by its own
        /// <c>hidden</c> or by <paramref name="hiddenIds"/>.
        /// </param>
        /// <param name="reader">Who reads (<see cref="OWLensReader"/>); the owner reading as another is judged by that reader's rung.</param>
        /// <param name="open">True for a world open to anyone: only then does the public reader read anything of it.</param>
        /// <param name="hiddenIds">The world's own hidden list, beside each element's own mark; null for none.</param>
        /// <remarks>
        /// <para>
        /// <b>The slots.</b> The story is the element the mark names when it is a Narrative in the reader's view; with none,
        /// every slot is empty, since a strand hangs off its story. The happening is the element the mark names when it is
        /// an Event in the reader's view and in the story's <c>events</c>; the place, then, the element the mark names when
        /// it is a Location in the reader's view and in that event's <c>locations</c>, empty when the event is. The hero is
        /// the story's <c>protagonist</c> when it is a Character in the reader's view; the place, now, the hero's
        /// <c>location</c> when it is a Location in the reader's view, empty when the hero is.
        /// </para>
        /// <para>
        /// <b>No fallback, no order.</b> A slot holds only its own kind, whether the id comes from the mark or from a
        /// parent's list. No slot is filled from another: not the hero from the event's <c>characters</c>, not the place
        /// then from the hero's <c>location</c>, nor the reverse. A list is read for membership, never its order.
        /// </para>
        /// <para>
        /// <b>As served.</b> Each slot's links are read from the body the reader is served, so a story the reader knows
        /// only as a stub fills its own slot and nothing past it, and a link the reader may not see is not followed.
        /// </para>
        /// <para>
        /// <b>The mark.</b> An object whose <c>narrative</c> is a string; <c>event</c> and <c>location</c> are each a
        /// string id, null or absent, and null or absent reads as unset (serial 99: a strand begins when a story is
        /// chosen, and a place with no happening is an empty place). Any other shape is no mark, and the strand is empty:
        /// a bare string, <c>narrative</c> missing or not a string, <c>event</c> or <c>location</c> an empty string, a
        /// number or an object. An id that names no element in the reader's view is an empty slot: there is no id-format
        /// test here (a malformed id is the doctor's to name). A key the reader cannot resolve, as a folder or an export
        /// may hand on, reads as an empty strand and leaks nothing.
        /// </para>
        /// <para>
        /// <b>Shapes.</b> A multi-link that is not a list lists nothing, and a slot whose element has no name reads an
        /// empty string, never null.
        /// </para>
        /// </remarks>
        public static OWStrand Of(JObject world, IEnumerable<JToken> elements, OWLensReader reader, bool open = false,
            IEnumerable<string> hiddenIds = null)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            var empty = new OWStrand(null, null, null, null, null);
            if (!(world?[MarkField] is JObject mark)) return empty;
            var story = StringOf(mark["narrative"]);
            if (story == null || !MemberOf(mark["event"], out var happening) || !MemberOf(mark["location"], out var then))
                return empty;

            if (reader.Rung == OWLensRung.Public && !open) return empty;

            var view = new OWLens.View(elements, reader, hiddenIds);
            bool Seen(string id, string kind) => id != null && view.Els.ContainsKey(id) && view.Sees(id) && view.TypeOf(id) == kind;
            OWStrandSlot SlotOf(string id) => new OWStrandSlot(id, StringOf(view.Served(id)["name"]) ?? string.Empty);
            bool Listed(string holder, string field, string id) =>
                view.Served(holder)[field] is JArray list && list.Any(entry => StringOf(entry) == id);

            if (!Seen(story, "narrative")) return empty;

            OWStrandSlot @event = null, location = null, character = null, now = null;
            if (Seen(happening, "event") && Listed(story, "events", happening))
            {
                @event = SlotOf(happening);
                if (Seen(then, "location") && Listed(happening, "locations", then)) location = SlotOf(then);
            }

            var hero = StringOf(view.Served(story)["protagonist"]);
            if (Seen(hero, "character"))
            {
                character = SlotOf(hero);
                var where = StringOf(view.Served(hero)["location"]);
                if (Seen(where, "location")) now = SlotOf(where);
            }

            return new OWStrand(SlotOf(story), @event, location, character, now);
        }

        /// <summary>
        /// The event's or the place's member: true with its id for a non-empty string, true with null for null or absent
        /// (unset), false for anything else (an empty string, a number, an object), which makes the whole value no mark.
        /// </summary>
        private static bool MemberOf(JToken token, out string id)
        {
            id = null;
            if (token == null || token.Type == JTokenType.Null) return true;
            id = StringOf(token);
            return !string.IsNullOrEmpty(id);
        }

        private static JToken Slot(OWStrandSlot slot) => slot?.ToJson() ?? (JToken)JValue.CreateNull();

        private static string StringOf(JToken token) =>
            token is JValue value && value.Type == JTokenType.String ? (string)value : null;
    }
}
