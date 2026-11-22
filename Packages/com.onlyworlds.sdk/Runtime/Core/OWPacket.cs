// The packet profile's readers (schema-dist profiles/packet.md, serial 136; two more fixture cases at serial 138): a work
// item's state (rule 5), open work (rules 5 and 7), who asked (rule 8), and the work items one reader is given read
// together, as profiles/packet.fixture.json reads them. A work item is a Relation with supertype `Work item`, the pair
// trimmed and folded as the room profile's pairs are (OWProfileText.Fold); the state is its subtype, trimmed and folded
// the same way; the room mark is not needed.
//
// Ported from @onlyworlds/sdk src/packet.ts (4.181.0): workStateOf, isOpenWork, askerOf and workOf. The fixture is this
// file's own test (OWPacketTests), as it is the TypeScript SDK's; the tests beside it are the TypeScript SDK's
// (test/packet.test.mjs), case for case.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// A world as <see cref="OWPacket.WorkOf"/> and <see cref="OWPacket.AskerOf"/> read it: the ids of its Characters the
    /// reader holds and the ids of every other element of it the reader holds, each as written.
    /// </summary>
    public sealed class OWWorkWorld
    {
        /// <summary>A world by its Characters' ids and the ids of its other elements; a null list is an empty one, a null id in one is passed over.</summary>
        /// <param name="id">The world's id; not read by the readers, kept for the caller.</param>
        /// <param name="characters">The ids of the world's Characters the reader holds, as written.</param>
        /// <param name="others">The ids of every other element of the world the reader holds, as written.</param>
        public OWWorkWorld(string id, IEnumerable<string> characters, IEnumerable<string> others = null)
        {
            Id = id;
            Characters = characters == null ? Array.Empty<string>() : characters.Where(c => c != null).ToArray();
            Others = others == null ? Array.Empty<string>() : others.Where(o => o != null).ToArray();
        }

        /// <summary>The world's id, or null.</summary>
        public string Id { get; }

        /// <summary>The ids of the world's Characters the reader holds, in the order given.</summary>
        public IReadOnlyList<string> Characters { get; }

        /// <summary>The ids of every other element of the world the reader holds, in the order given.</summary>
        public IReadOnlyList<string> Others { get; }
    }

    /// <summary>One entry of a work item's <c>x_packet_elements</c> (rule 1): the element's type slug, its id as written, and why it is in the packet.</summary>
    public sealed class OWWorkElement
    {
        internal OWWorkElement(string type, string id, string reason)
        {
            Type = type;
            Id = id;
            Reason = reason;
        }

        /// <summary>The element's type slug, as written.</summary>
        public string Type { get; }

        /// <summary>The element's id, as written (never trimmed, never folded).</summary>
        public string Id { get; }

        /// <summary>Why the element is in the packet: one sentence a person reads.</summary>
        public string Reason { get; }

        /// <summary><c>{type, id, reason}</c>, as the packet writes an entry.</summary>
        public JObject ToJson() => new JObject { ["type"] = Type, ["id"] = Id, ["reason"] = Reason };
    }

    /// <summary>The one kind of lint <see cref="OWPacket.WorkOf"/> reports.</summary>
    public static class OWWorkLintKind
    {
        /// <summary>A work item whose <c>x_packet_from</c> is no one id, or names an element the reader holds that is no Character of the world (rule 8).</summary>
        public const string Asker = "asker";
    }

    /// <summary>One lint: its kind and the work item it is on.</summary>
    public sealed class OWWorkLint
    {
        internal OWWorkLint(string kind, string item)
        {
            Kind = kind;
            Item = item;
        }

        /// <summary><see cref="OWWorkLintKind.Asker"/>.</summary>
        public string Kind { get; }

        /// <summary>The work item's id.</summary>
        public string Item { get; }

        /// <summary><c>{kind, item}</c>, as the fixture writes a lint.</summary>
        public JObject ToJson() => new JObject { ["kind"] = Kind, ["item"] = Item };
    }

    /// <summary>
    /// <see cref="OWPacket.WorkOf"/>'s answer. Every map is keyed by an id as written and enumerates in the order its keys
    /// were first met (the work items in the order given; <see cref="WaitingOn"/> in the world's Character order). No list
    /// of work items is in a rule order, so compare them as sets, except <see cref="Elements"/>.
    /// </summary>
    public sealed class OWWork
    {
        internal OWWork(IReadOnlyDictionary<string, string> states, IReadOnlyList<string> shown,
            IReadOnlyDictionary<string, IReadOnlyList<string>> openWork,
            IReadOnlyDictionary<string, IReadOnlyList<OWWorkElement>> elements,
            IReadOnlyDictionary<string, string> askers, IReadOnlyList<OWWorkLint> lints,
            IReadOnlyDictionary<string, IReadOnlyList<string>> waitingOn)
        {
            States = states;
            Shown = shown;
            OpenWork = openWork;
            Elements = elements;
            Askers = askers;
            Lints = lints;
            WaitingOn = waitingOn;
        }

        /// <summary>For each work item, its state (one of <see cref="OWPacket.States"/>), or null for no state (rule 5).</summary>
        public IReadOnlyDictionary<string, string> States { get; }

        /// <summary>Every work item, in the order given: no item is hidden for its state.</summary>
        public IReadOnlyList<string> Shown { get; }

        /// <summary>For each element any work item names in <c>x_packet_elements</c>, the open work that names it (state <c>open</c> or <c>taken</c>); a standing item is counted on none.</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<string>> OpenWork { get; }

        /// <summary>For each work item, its elements in <c>x_packet_elements</c>' order (rule 1).</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<OWWorkElement>> Elements { get; }

        /// <summary>For each work item, the Character of the world that asked for it, or null (rule 8).</summary>
        public IReadOnlyDictionary<string, string> Askers { get; }

        /// <summary>One <see cref="OWWorkLintKind.Asker"/> lint per work item whose key is no one id or names an element the reader holds that is no Character of the world, in the order of the items.</summary>
        public IReadOnlyList<OWWorkLint> Lints { get; }

        /// <summary>For each Character of the world, the open work it asked for: what it is waiting on.</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<string>> WaitingOn { get; }
    }

    /// <summary>
    /// Reads the packet profile's work items: a work item's state, whether it is open work, who asked for it, and the
    /// work items one reader is given read together. Element bodies are <see cref="JToken"/>s as the API or a world
    /// folder gives them; anything that is no work item reads as none.
    /// </summary>
    /// <remarks>
    /// A string is a <see cref="JValue"/> of <see cref="JTokenType.String"/>, as the package's own JSON readers parse
    /// (dates not parsed). A body read with Newtonsoft's default <c>JObject.Parse</c> may hold a date-like string as
    /// <see cref="JTokenType.Date"/>, which these readers take as no string.
    /// </remarks>
    public static class OWPacket
    {
        /// <summary>The pair a work item is: type <c>relation</c>, supertype <c>Work item</c>. One of <see cref="OWRoom.Pairs"/>.</summary>
        public static readonly (string type, string supertype) Pair = ("relation", "Work item");

        /// <summary>The state of work to be done.</summary>
        public const string Open = "open";

        /// <summary>The state of work someone has taken.</summary>
        public const string Taken = "taken";

        /// <summary>The state of work that is done.</summary>
        public const string Done = "done";

        /// <summary>The state of a standing work item: a standing order of work, a kept list, never done and never open work (rule 7).</summary>
        public const string Standing = "standing";

        /// <summary>The four states, as the fold writes them (rule 5).</summary>
        public static readonly IReadOnlyList<string> States = new[] { Open, Taken, Done, Standing };

        /// <summary>The key that names who asked for the work (rule 8).</summary>
        public const string FromField = "x_packet_from";

        /// <summary>The key that lists the packet's elements (rule 1).</summary>
        public const string ElementsField = "x_packet_elements";

        /// <summary>True for a Relation with supertype <c>Work item</c>: each a string, trimmed and folded, the whole word.</summary>
        internal static bool IsWorkItem(JToken item)
        {
            if (!(item is JObject body)) return false;
            if (!(StringOf(body["type"]) is string type) || !(StringOf(body["supertype"]) is string supertype)) return false;
            return OWProfileText.Fold(type) == OWProfileText.Fold(Pair.type)
                   && OWProfileText.Fold(supertype) == OWProfileText.Fold(Pair.supertype);
        }

        /// <summary>
        /// A work item's state (schema-dist profiles/packet.md, rule 5): its <c>subtype</c>, trimmed of White_Space and
        /// compared without regard to case as the room profile defines it, when that is <c>open</c>, <c>taken</c>,
        /// <c>done</c> or <c>standing</c>; else null, no state (a subtype that is missing, null, not a string, empty,
        /// White_Space alone or any other word). <c>" Standing"</c> is <c>standing</c>, <c>"OPEN"</c> is <c>open</c>;
        /// <c>open</c> after a byte order mark is no state, since U+FEFF is no White_Space.
        /// </summary>
        /// <param name="item">An element body; anything that is no work item (a Relation with supertype <c>Work item</c>, the pair trimmed and folded) answers null.</param>
        /// <remarks>The room mark is not needed. A packet before it lands carries no id, and its state reads the same. Pure.</remarks>
        public static string WorkStateOf(JToken item)
        {
            if (!IsWorkItem(item) || !(StringOf(item["subtype"]) is string subtype)) return null;
            var state = OWProfileText.Fold(subtype);
            foreach (var s in States)
            {
                if (s == state) return s;
            }

            return null;
        }

        /// <summary>
        /// True when the work item is open work (packet.md, rules 5 and 7): its state is <c>open</c> or <c>taken</c>. A
        /// <c>done</c> item, a <c>standing</c> one (never done, a kept list) and one with no state are never open work,
        /// and anything that is no work item is none. A reader offers to take a work item, or to set it done, only when
        /// this is true. Pure.
        /// </summary>
        public static bool IsOpenWork(JToken item)
        {
            var state = WorkStateOf(item);
            return state == Open || state == Taken;
        }

        /// <summary>
        /// Who asked for a work item (schema-dist profiles/packet.md, rule 8): the id its <c>x_packet_from</c> writes,
        /// as written (never trimmed, never folded), or null. Absent or null names no one; a value that is not a
        /// string, or a string empty once trimmed of White_Space, is no id and names no one. The asker is never the
        /// maker (<c>created_by</c>, or the tool <c>x_packet_producer</c> names) and never <c>actor</c>.
        /// </summary>
        /// <param name="item">An element body; anything that is no work item answers null.</param>
        /// <param name="world">
        /// With a world, the id counts only when it is, written exactly so, one of the world's Characters; any other id
        /// is null (<see cref="WorkOf"/> says which of them is a lint). Without one, the answer is the key's id
        /// unchecked: whether it names a Character the reader holds is the caller's to read.
        /// </param>
        /// <remarks>Pure.</remarks>
        public static string AskerOf(JToken item, OWWorkWorld world = null)
        {
            if (!IsWorkItem(item)) return null;
            var key = AskerKey((JObject)item, out var bad);
            if (key == null || bad) return null;
            if (world == null) return key;
            return world.Characters.Contains(key) ? key : null;
        }

        /// <summary>
        /// The work items one reader is given, read together (schema-dist profiles/packet.md, rules 1, 5, 7 and 8; the
        /// fixture's parts).
        /// </summary>
        /// <param name="items">
        /// The work items in the order given. Anything that is no work item (a Relation with supertype <c>Work item</c>,
        /// trimmed and folded) or carries no non-empty string <c>id</c> is in no part of the answer, and of two with one
        /// id the first is read.
        /// </param>
        /// <param name="world">The world they sit in: its Characters' ids and the ids of every other element of it the reader holds, each as written.</param>
        /// <param name="sourceWorld">
        /// The world packets about another world name, whose elements the reader holds too; its Characters are no
        /// Characters of <paramref name="world"/>. Null when there is none.
        /// </param>
        /// <remarks>
        /// <list type="bullet">
        /// <item><see cref="OWWork.States"/>: each item's state (<see cref="WorkStateOf"/>). <see cref="OWWork.Shown"/>: every item, whatever its state.</item>
        /// <item><see cref="OWWork.Elements"/>: each item's <c>x_packet_elements</c>, in their order; an entry without the three (strings, the id not empty once trimmed of White_Space) is ignored, never repaired.</item>
        /// <item><see cref="OWWork.OpenWork"/>: for each element id any item names, the ids of the open work (<see cref="IsOpenWork"/>) that names it, each once; a standing item is counted on none.</item>
        /// <item><see cref="OWWork.Askers"/>: each item's asker, the Character of <paramref name="world"/> its <c>x_packet_from</c> names written exactly so, else null.</item>
        /// <item><see cref="OWWork.Lints"/>: an <see cref="OWWorkLintKind.Asker"/> lint for a key that is no one id (not a string, or empty once trimmed of White_Space), or that names, written exactly so, an element the reader holds that is no Character of <paramref name="world"/> (an id in its others, in <paramref name="sourceWorld"/>, or one of the work items given). A key absent or null, and an id that names no element the reader holds in any spelling, are no lint.</item>
        /// <item><see cref="OWWork.WaitingOn"/>: for each Character of <paramref name="world"/>, the open work whose asker it is.</item>
        /// </list>
        /// Only <see cref="OWWork.Elements"/> holds lists in a rule order; the others are in the order the items are
        /// given, and the page rules none. Pure: the bodies are not changed, and the answer shares nothing with them.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="items"/> or <paramref name="world"/> is null.</exception>
        public static OWWork WorkOf(IEnumerable<JToken> items, OWWorkWorld world, OWWorkWorld sourceWorld = null)
        {
            if (items == null) throw new ArgumentNullException(nameof(items), "WorkOf: items is a list of work items");
            if (world == null) throw new ArgumentNullException(nameof(world), "WorkOf: world is a world with its characters and others");

            var characters = new HashSet<string>(world.Characters);
            var held = new HashSet<string>(world.Others);
            if (sourceWorld != null)
            {
                held.UnionWith(sourceWorld.Characters);
                held.UnionWith(sourceWorld.Others);
            }

            var read = new List<(string id, JObject body)>();
            var seen = new HashSet<string>();
            foreach (var item in items)
            {
                if (!IsWorkItem(item) || !(StringOf(item["id"]) is string id) || id.Length == 0 || !seen.Add(id)) continue;
                read.Add((id, (JObject)item));
            }

            held.UnionWith(seen);

            var states = new OrderedMap<string>();
            var shown = new List<string>();
            var openWork = new OrderedMap<List<string>>();
            var elements = new OrderedMap<IReadOnlyList<OWWorkElement>>();
            var askers = new OrderedMap<string>();
            var lints = new List<OWWorkLint>();
            var waitingOn = new OrderedMap<List<string>>();
            foreach (var c in world.Characters)
            {
                if (!waitingOn.ContainsKey(c)) waitingOn.Set(c, new List<string>());
            }

            foreach (var (id, item) in read)
            {
                var open = IsOpenWork(item);
                states.Set(id, WorkStateOf(item));
                shown.Add(id);
                var entries = ElementsOf(item);
                elements.Set(id, entries);
                foreach (var e in entries)
                {
                    if (!openWork.TryGetValue(e.Id, out var list)) openWork.Set(e.Id, list = new List<string>());
                    if (open && !list.Contains(id)) list.Add(id);
                }

                var key = AskerKey(item, out var bad);
                string asker = null;
                if (bad) lints.Add(new OWWorkLint(OWWorkLintKind.Asker, id));
                else if (key != null)
                {
                    if (characters.Contains(key)) asker = key;
                    else if (held.Contains(key)) lints.Add(new OWWorkLint(OWWorkLintKind.Asker, id));
                }

                askers.Set(id, asker);
                if (asker != null && open) waitingOn[asker].Add(id);
            }

            return new OWWork(states, shown, openWork.Freeze(l => (IReadOnlyList<string>)l.ToArray()), elements, askers,
                lints, waitingOn.Freeze(l => (IReadOnlyList<string>)l.ToArray()));
        }

        /// <summary>
        /// The asker key as one id: a string with something left once trimmed of White_Space, as written; null with
        /// <paramref name="bad"/> false for a key absent or null; null with <paramref name="bad"/> true for any other value.
        /// </summary>
        private static string AskerKey(JObject item, out bool bad)
        {
            bad = false;
            var v = item[FromField];
            if (v == null || v.Type == JTokenType.Null || v.Type == JTokenType.Undefined) return null;
            if (!(StringOf(v) is string s) || OWProfileText.TrimWhiteSpace(s).Length == 0)
            {
                bad = true;
                return null;
            }

            return s;
        }

        /// <summary>The entries of <c>x_packet_elements</c> a reader reads: objects holding <c>type</c>, <c>id</c> and <c>reason</c>, each a string, the id not empty once trimmed; any other entry is ignored, never repaired.</summary>
        private static IReadOnlyList<OWWorkElement> ElementsOf(JObject item)
        {
            var read = new List<OWWorkElement>();
            if (!(item[ElementsField] is JArray list)) return read;
            foreach (var entry in list)
            {
                if (!(entry is JObject e)) continue;
                if (!(StringOf(e["type"]) is string type) || !(StringOf(e["id"]) is string id)
                    || !(StringOf(e["reason"]) is string reason)) continue;
                if (OWProfileText.TrimWhiteSpace(id).Length == 0) continue;
                read.Add(new OWWorkElement(type, id, reason));
            }

            return read;
        }

        private static string StringOf(JToken token) =>
            token is JValue value && value.Type == JTokenType.String ? (string)value : null;

        /// <summary>A map that enumerates in the order its keys were first set, so an answer's keys are in the order npm's are.</summary>
        private sealed class OrderedMap<TValue> : IReadOnlyDictionary<string, TValue>
        {
            private readonly Dictionary<string, TValue> _map = new Dictionary<string, TValue>();
            private readonly List<string> _keys = new List<string>();

            public void Set(string key, TValue value)
            {
                if (!_map.ContainsKey(key)) _keys.Add(key);
                _map[key] = value;
            }

            public OrderedMap<TOut> Freeze<TOut>(Func<TValue, TOut> convert)
            {
                var frozen = new OrderedMap<TOut>();
                foreach (var key in _keys) frozen.Set(key, convert(_map[key]));
                return frozen;
            }

            public TValue this[string key] => _map[key];
            public IEnumerable<string> Keys => _keys;
            public IEnumerable<TValue> Values => _keys.Select(k => _map[k]);
            public int Count => _keys.Count;
            public bool ContainsKey(string key) => _map.ContainsKey(key);
            public bool TryGetValue(string key, out TValue value) => _map.TryGetValue(key, out value);

            public IEnumerator<KeyValuePair<string, TValue>> GetEnumerator() =>
                _keys.Select(k => new KeyValuePair<string, TValue>(k, _map[k])).GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
