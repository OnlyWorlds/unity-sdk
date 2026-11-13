// The record profile: a game's run kept in the world its Characters live in (schema-dist profiles/record.md, serial
// 102, ruled 2026-11-01; its readings tightened at serial 103). The run is an Event carrying the mark x_record ({tool,
// played_at, version?, seed?, outcome?, seats?}), the game's own tally beside it under its own prefix; who took part is
// the Event's `characters` alone; a Narrative that lists the Event in `events` and names a Character as protagonist or
// narrator is that Character's story of it. A run or a story the review profile reads as proposed (OWPayload.IsProposed,
// the reader that shows a world: an x_review_proposed object with a key; an empty mark, {}, "" or [], is settled) is
// not read until a keeper settles it.
//
// Written from the profile and its conformance fixture, profiles/record.fixture.json, which is this file's own test
// (OWRecordTests); keel's reader (elements/record.py) and the packages' (the TypeScript SDK's recordsOf, the Python
// package's records_of) answer the same cases.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary>A record's mark as read: the two required members as written, each optional one null when unset.</summary>
    public sealed class OWRecordMark
    {
        internal OWRecordMark(string tool, string playedAt, string version, string seed, string outcome,
            IReadOnlyDictionary<string, string> seats)
        {
            Tool = tool;
            PlayedAt = playedAt;
            Version = version;
            Seed = seed;
            Outcome = outcome;
            Seats = seats;
        }

        /// <summary>The game, in the tools list's id form (<c>[a-z0-9-]</c>, 1 to 32).</summary>
        public string Tool { get; }

        /// <summary>When the run was played: real time (never world time), read by the message profile's grammar, as
        /// written.</summary>
        public string PlayedAt { get; }

        /// <summary>The engine's version; with <see cref="Seed"/>, the replay. Null when unset.</summary>
        public string Version { get; }

        /// <summary>The run's seed, a string. Null when unset.</summary>
        public string Seed { get; }

        /// <summary>One lowercase word the game chose (<c>won</c>, <c>lost</c>, <c>sunk</c>). Null when unset.</summary>
        public string Outcome { get; }

        /// <summary>Character id to the seat in the game's own words; decoration, never who took part.</summary>
        public IReadOnlyDictionary<string, string> Seats { get; }

        /// <summary>The profile's normalised mark, every member present.</summary>
        public JObject ToJson()
        {
            var seats = new JObject();
            foreach (var kv in Seats) seats[kv.Key] = kv.Value;
            return new JObject
            {
                ["tool"] = Tool,
                ["played_at"] = PlayedAt,
                ["version"] = OWRecord.OrNull(Version),
                ["seed"] = OWRecord.OrNull(Seed),
                ["outcome"] = OWRecord.OrNull(Outcome),
                ["seats"] = seats,
            };
        }
    }

    /// <summary>One Character's story of a record: about them, told by them, or both.</summary>
    public sealed class OWRecordStory
    {
        internal OWRecordStory(string id, string name, string @as)
        {
            Id = id;
            Name = name;
            As = @as;
        }

        /// <summary>The Narrative's id.</summary>
        public string Id { get; }

        /// <summary>Its name as served; empty when it has none.</summary>
        public string Name { get; }

        /// <summary><c>protagonist</c> (a story about them), <c>narrator</c> (their own account) or <c>both</c>.</summary>
        public string As { get; }

        /// <summary>The profile's <c>{id, name, as}</c>.</summary>
        public JObject ToJson() => new JObject { ["id"] = Id, ["name"] = Name, ["as"] = As };
    }

    /// <summary>One record of a Character, as <see cref="OWRecord.Of"/> reads it.</summary>
    public sealed class OWRecord
    {
        /// <summary>The Event's field that carries the record's mark.</summary>
        public const string MarkField = "x_record";

        /// <summary>The mark's six members, in the profile's order; <c>tool</c> and <c>played_at</c> are required.</summary>
        public static readonly IReadOnlyList<string> Members =
            new[] { "tool", "played_at", "version", "seed", "outcome", "seats" };

        private OWRecord(string eventId, string eventName, OWRecordMark mark, string seat,
            IReadOnlyList<OWRecordStory> stories)
        {
            EventId = eventId;
            EventName = eventName;
            Tool = mark.Tool;
            PlayedAt = mark.PlayedAt;
            Version = mark.Version;
            Seed = mark.Seed;
            Outcome = mark.Outcome;
            Seat = seat;
            Stories = stories;
        }

        /// <summary>The run's Event id.</summary>
        public string EventId { get; }

        /// <summary>The Event's name as served; empty when it has none.</summary>
        public string EventName { get; }

        /// <summary>The game.</summary>
        public string Tool { get; }

        /// <summary>When it was played, as written.</summary>
        public string PlayedAt { get; }

        /// <summary>The engine's version, or null.</summary>
        public string Version { get; }

        /// <summary>The seed, or null.</summary>
        public string Seed { get; }

        /// <summary>The outcome word, or null.</summary>
        public string Outcome { get; }

        /// <summary>This Character's seat, when the mark names one for them; else null.</summary>
        public string Seat { get; }

        /// <summary>Their stories of this run, in code point order of their ids.</summary>
        public IReadOnlyList<OWRecordStory> Stories { get; }

        /// <summary>The profile's entry: <c>{event: {id, name}, tool, played_at, version, seed, outcome, seat, stories}</c>.</summary>
        public JObject ToJson() => new JObject
        {
            ["event"] = new JObject { ["id"] = EventId, ["name"] = EventName },
            ["tool"] = Tool,
            ["played_at"] = PlayedAt,
            ["version"] = OWRecord.OrNull(Version),
            ["seed"] = OWRecord.OrNull(Seed),
            ["outcome"] = OWRecord.OrNull(Outcome),
            ["seat"] = OrNull(Seat),
            ["stories"] = new JArray(Stories.Select(s => (object)s.ToJson())),
        };

        /// <summary>
        /// The record's mark on an element, read: the mark when the element is an Event whose <c>x_record</c> has the
        /// profile's shape, else null. A broken writer never shows as a record: a missing or malformed <c>tool</c> or
        /// <c>played_at</c>, or an optional member that is anything but its kind, null or absent, is no mark.
        /// </summary>
        public static OWRecordMark MarkOf(JToken element)
        {
            if (!(element is JObject el) || Str(el["type"]) != "event") return null;
            if (!(el[MarkField] is JObject m)) return null;
            var tool = Str(m["tool"]);
            if (tool == null || !IsTool(tool)) return null;
            var playedAt = Str(m["played_at"]);
            if (playedAt == null || InstantMs(playedAt) == null) return null;
            if (!UnsetOr(m["version"], t => t.Type == JTokenType.String)) return null;
            if (!UnsetOr(m["seed"], t => t.Type == JTokenType.String)) return null;
            if (!UnsetOr(m["outcome"], t => t.Type == JTokenType.String && IsOutcome((string)t))) return null;
            if (!UnsetOr(m["seats"], t => t is JObject o && o.Properties().All(p => p.Value.Type == JTokenType.String)))
                return null;

            var seats = new Dictionary<string, string>(StringComparer.Ordinal);
            if (m["seats"] is JObject so)
                foreach (var p in so.Properties()) seats[p.Name] = (string)p.Value;
            return new OWRecordMark(tool, playedAt, Str(m["version"]), Str(m["seed"]), Str(m["outcome"]), seats);
        }

        /// <summary>
        /// A Character's records in a world: every Event with the record mark whose <c>characters</c> names them, newest
        /// first by <c>played_at</c> read as an instant to the millisecond (ties by the Event's id in code point order),
        /// each with the stories that are theirs. An id no record names, or one in no element, reads an empty list.
        /// <paramref name="elements"/> is the world as this reader is served it.
        /// </summary>
        public static IReadOnlyList<OWRecord> Of(string characterId, IEnumerable<JToken> elements)
        {
            if (characterId == null) throw new ArgumentNullException(nameof(characterId));
            if (elements == null) throw new ArgumentNullException(nameof(elements));
            var all = elements.ToList();
            // This Character's stories, by each Event they name (once per Narrative, however often it names it): one pass
            // over the world, then a lookup per record, as npm 4.124.1 reads (each record rescanned the world before).
            // A story is immutable, so one instance may sit in several records.
            var storiesOf = new Dictionary<string, List<OWRecordStory>>(StringComparer.Ordinal);
            foreach (var t in all)
            {
                if (!(t is JObject n) || Str(n["type"]) != "narrative" || Proposed(n) || Str(n["id"]) == null) continue;
                var p = Str(n["protagonist"]) == characterId;
                var r = Str(n["narrator"]) == characterId;
                if (!p && !r || !(n["events"] is JArray events)) continue;
                var story = new OWRecordStory(Str(n["id"]), Str(n["name"]) ?? string.Empty,
                    p && r ? "both" : p ? "protagonist" : "narrator");
                var named = new HashSet<string>(StringComparer.Ordinal);
                foreach (var e in events)
                {
                    if (e.Type != JTokenType.String || !named.Add((string)e)) continue;
                    if (!storiesOf.TryGetValue((string)e, out var at)) storiesOf[(string)e] = at = new List<OWRecordStory>();
                    at.Add(story);
                }
            }

            var found = new List<(OWRecord record, long ms)>();
            foreach (var token in all)
            {
                var mark = MarkOf(token);
                if (mark == null) continue;
                var ev = (JObject)token;
                if (Proposed(ev) || !ListHas(ev["characters"], characterId)) continue;
                var id = Str(ev["id"]);
                if (id == null) continue; // what cannot be named is never read (the page is silent; the game SDKs agree)

                var stories = storiesOf.TryGetValue(id, out var mine) ? new List<OWRecordStory>(mine) : new List<OWRecordStory>();
                stories.Sort((a, b) => OWCodePointComparer.Instance.Compare(a.Id, b.Id));

                mark.Seats.TryGetValue(characterId, out var seat);
                found.Add((new OWRecord(id, Str(ev["name"]) ?? string.Empty, mark, seat, stories),
                    InstantMs(mark.PlayedAt).Value));
            }

            found.Sort((a, b) =>
            {
                var c = b.ms.CompareTo(a.ms);
                return c != 0 ? c : OWCodePointComparer.Instance.Compare(a.record.EventId, b.record.EventId);
            });
            return found.Select(f => f.record).ToList();
        }

        // -- Reading -----------------------------------------------------------------------

        internal static JToken OrNull(string s) => s == null ? JValue.CreateNull() : new JValue(s);

        private static string Str(JToken t) => t != null && t.Type == JTokenType.String ? (string)t : null;

        private static bool UnsetOr(JToken t, Func<JToken, bool> test) =>
            t == null || t.Type == JTokenType.Null || test(t);

        private static bool ListHas(JToken t, string id) =>
            id != null && t is JArray a && a.Any(x => x.Type == JTokenType.String && (string)x == id);

        /// <summary>
        /// Proposed exactly when the review profile's reader that shows a world says so (record reader rule 1, serial
        /// 103): an <c>x_review_proposed</c> object with a key. An empty mark (<c>{}</c>, <c>""</c>, <c>[]</c>) is
        /// settled, as absent and null are; so is a value that is not an object.
        /// </summary>
        private static bool Proposed(JObject el) => OWPayload.IsProposed(el);

        private static bool IsTool(string s)
        {
            if (s.Length < 1 || s.Length > 32) return false;
            foreach (var c in s)
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-')) return false;
            return true;
        }

        private static bool IsOutcome(string s)
        {
            if (s.Length < 1 || s.Length > 24) return false;
            foreach (var c in s)
                if (c < 'a' || c > 'z') return false;
            return true;
        }

        /// <summary>
        /// <c>played_at</c> as an instant in milliseconds from 0001-01-01 UTC, a fraction truncated past three digits; null
        /// when it is not one. Read by the message profile's grammar (<see cref="OWMessage.TimeKey"/>), the one grammar
        /// for a time in the standard: a date and a time with its seconds, a fraction, <c>Z</c> or an offset or no zone
        /// (read as UTC), <c>T</c> and <c>Z</c> in either case, every part in its range. Checked by its parts, never by a
        /// date parser (one that rolls 02-30 into March or takes 24:00 would make a broken writer's run a record).
        /// </summary>
        internal static long? InstantMs(string s)
        {
            if (s == null) return null;
            var micros = OWMessage.TimeKey(new JValue(s));
            if (!micros.HasValue) return null;
            // Floored, so an instant before 0001-01-01 (the year 0000 is a year) still truncates toward the past.
            var m = micros.Value;
            return m / 1000 - (m % 1000 < 0 ? 1 : 0);
        }
    }
}
