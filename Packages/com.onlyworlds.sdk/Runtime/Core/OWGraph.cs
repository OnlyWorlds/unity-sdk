// The lens profile's graph section: a reading of a world as nodes and edges (schema-dist profiles/lens.md, serial 81,
// ruled 2026-10-27; the reading aids out of an unlensed graph and a stub lens read as nothing at serial 82). The elements a reader sees are the nodes, the links between them the edges. It adds nothing to the
// world and stores nothing.
//
// Written from the profile and its conformance fixture's graph kind (profiles/lens.fixture.json), which is this file's
// own test (OWGraphTests). The packages' reader is worldGraph(world, reader, options) (the Python package's
// world_graph): here OWGraph.Read. Who sees what is the lens reader's own reading (OWLens's view), never a second copy;
// with a lens the nodes are exactly what the lens kept. A node's family and icon are the presentation sidecar's
// (OWPresentation).

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary>Why a node that is out by default is in: the kinds a flag brings back and marks.</summary>
    public static class OWGraphMark
    {
        /// <summary>A room element (<see cref="OWRoom"/>), brought back by <see cref="OWGraphOptions.Rooms"/>.</summary>
        public const string Room = "room";

        /// <summary>A hidden element (<see cref="OWPayload.IsHidden"/>), brought back by <see cref="OWGraphOptions.Hidden"/>.</summary>
        public const string Hidden = "hidden";

        /// <summary>An element written as proposed, brought back by <see cref="OWGraphOptions.Proposed"/>.</summary>
        public const string Proposed = "proposed";

        /// <summary>Every mark, in the order a node carries them.</summary>
        public static readonly string[] All = { Room, Hidden, Proposed };
    }

    /// <summary>
    /// Why an element is not a node, or a link was left out: the lens's closed list (<see cref="OWLensCut"/>), then the
    /// graph's own, in one order. <see cref="OutsideLens"/> is only ever a link's reason, and only with a lens.
    /// </summary>
    public static class OWGraphCut
    {
        /// <summary>A room element, out by default.</summary>
        public const string Room = OWGraphMark.Room;

        /// <summary>A hidden element, out by default. The owner's count only.</summary>
        public const string Hidden = OWGraphMark.Hidden;

        /// <summary>An element written as proposed, out by default. The owner's count only.</summary>
        public const string Proposed = OWGraphMark.Proposed;

        /// <summary>Map space (a Map, a Pin, a Marker or a Zone), out by default for size, not for standing.</summary>
        public const string MapSpace = "map_space";

        /// <summary>
        /// A reading aid (<see cref="OWRoom.IsCharterElement"/>: the charter, its guide, its domains, by the pair and the mark
        /// <c>x_charter_of</c>), out of an unlensed graph. No flag brings it back; a lens that names its pair keeps it.
        /// </summary>
        public const string ReadingAid = "reading_aid";

        /// <summary>
        /// A link from a kept node to an element the reader sees that the lens neither kept nor cut: past its hops,
        /// outside its types, the lens element itself, a message its mail part took. Every reader's count: the target is
        /// in its view, so the count leaks nothing. Never in a lens's cut.
        /// </summary>
        public const string OutsideLens = "outside_lens";

        /// <summary>Every reason, in the one order the counts are given in: the lens's, then the graph's.</summary>
        public static readonly string[] All =
            OWLensCut.All.Concat(new[] { Room, Hidden, Proposed, MapSpace, ReadingAid, OutsideLens }).ToArray();

        /// <summary>
        /// The counts only the owner sees (the owner reading as itself, or as another reader): the lens's two, and the two
        /// kinds only the owner may bring back. Any other reader's answer never carries them, not even as zero.
        /// </summary>
        public static readonly string[] OwnerOnly = OWLensCut.OwnerOnly.Concat(new[] { Hidden, Proposed }).ToArray();
    }

    /// <summary><see cref="OWGraph.Read"/>'s options: a lens, its reading's moment, and the four flags.</summary>
    public sealed class OWGraphOptions
    {
        /// <summary>A lens body: the nodes are then exactly what it kept, and no flag adds to them. Null: no lens.</summary>
        public JObject Lens { get; set; }

        /// <summary>The id of a lens in the world, which the reader is served whole; never beside <see cref="Lens"/>.</summary>
        public string LensId { get; set; }

        /// <summary>The lens's reading date, as <see cref="OWLensOptions.AsOf"/>; read only with a lens.</summary>
        public int? AsOf { get; set; }

        /// <summary>The world's now, as <see cref="OWLensOptions.Now"/>; read only with a lens.</summary>
        public int? Now { get; set; }

        /// <summary>The lens's time for its mail rule, as <see cref="OWLensOptions.Clock"/>; read only with a lens.</summary>
        public DateTimeOffset? Clock { get; set; }

        /// <summary>The world's members list, as <see cref="OWLensOptions.Members"/>; read only with a lens.</summary>
        public IEnumerable<JToken> Members { get; set; }

        /// <summary>
        /// The ids the world's hidden mark keeps, beside each element's own <c>hidden</c>, as
        /// <see cref="OWLensOptions.HiddenIds"/>: who sees them, and which nodes carry the <c>hidden</c> kind. The TypeScript
        /// SDK and the Python package read this list as <c>hidden</c> on the world snapshot (<c>world.hidden</c>, as the
        /// lens profile's fixture worlds carry it). Null for none.
        /// </summary>
        public IEnumerable<string> HiddenIds { get; set; }

        /// <summary>Bring back room elements, marked <c>room</c>. Refused for the public reader.</summary>
        public bool Rooms { get; set; }

        /// <summary>Bring back hidden elements, marked <c>hidden</c>. The owner's alone: refused from any other rung.</summary>
        public bool Hidden { get; set; }

        /// <summary>Bring back elements written as proposed, marked <c>proposed</c>. The owner's alone.</summary>
        public bool Proposed { get; set; }

        /// <summary>Bring back map space: Maps, Pins, Markers and Zones, with no mark. Refused for the public reader.</summary>
        public bool MapSpace { get; set; }
    }

    /// <summary>
    /// A flag the reader may not ask, refused before anything is read: <see cref="Flag"/> is the profile's word for it
    /// (<c>hidden</c>, <c>proposed</c>, <c>rooms</c>, <c>map_space</c>).
    /// </summary>
    public sealed class OWGraphRefusedException : ArgumentException
    {
        internal OWGraphRefusedException(string flag, string message)
            : base(message, "options")
        {
            Flag = flag;
        }

        /// <summary>The flag refused, as the profile spells it.</summary>
        public string Flag { get; }
    }

    /// <summary>One element of the graph.</summary>
    public sealed class OWGraphNode
    {
        internal OWGraphNode(string id, string type, JToken name, string family, string icon, IReadOnlyList<string> marks,
            bool knownOf)
        {
            Id = id;
            Type = type;
            _name = name?.DeepClone() ?? JValue.CreateNull();
            Name = _name.Type == JTokenType.String ? (string)_name : null;
            Family = family;
            Icon = icon;
            Marks = marks;
            KnownOf = knownOf;
        }

        /// <summary>The element's id.</summary>
        public string Id { get; }

        /// <summary>The element's type as stored, never folded (the profile's ruling G4); every source stores its slug (<c>character</c>).</summary>
        public string Type { get; }

        /// <summary>The element's name when it is a string, or null; <see cref="ToJson"/> writes the stored value whatever it is.</summary>
        public string Name { get; }

        /// <summary>The stored name, null when absent: nothing invented (the profile's ruling G3).</summary>
        private readonly JToken _name;

        /// <summary>The type's family, from the presentation sidecar (<see cref="OWPresentation.FamilyOf"/>).</summary>
        public string Family { get; }

        /// <summary>The type's icon name, from the presentation sidecar (<see cref="OWPresentation.IconOf"/>).</summary>
        public string Icon { get; }

        /// <summary>The out-by-default kinds the element belongs to, in <see cref="OWGraphMark.All"/>'s order; empty when none.</summary>
        public IReadOnlyList<string> Marks { get; }

        /// <summary>True on a guest's known-of element, a stub, which has no edges out.</summary>
        public bool KnownOf { get; }

        /// <summary>The node in the profile's words: <c>id</c>, <c>type</c>, <c>name</c>, <c>family</c>, <c>icon</c>, and <c>marks</c> and <c>known_of</c> when they apply.</summary>
        public JObject ToJson()
        {
            var o = new JObject
            {
                ["id"] = Id,
                ["type"] = Type,
                ["name"] = _name.DeepClone(),
                ["family"] = OWGraph.Text(Family),
                ["icon"] = OWGraph.Text(Icon),
            };
            if (Marks.Count > 0) o["marks"] = new JArray(Marks);
            if (KnownOf) o["known_of"] = true;
            return o;
        }
    }

    /// <summary>One link: one source, one field, one target, at its place in the field's list (0 for a single link).</summary>
    public sealed class OWGraphEdge
    {
        internal OWGraphEdge(string source, string field, string target, int index)
        {
            Source = source;
            Field = field;
            Target = target;
            Index = index;
        }

        /// <summary>The element whose field holds the link.</summary>
        public string Source { get; }

        /// <summary>The link field (a schema link field, or a declared extension link).</summary>
        public string Field { get; }

        /// <summary>The element linked to.</summary>
        public string Target { get; }

        /// <summary>The target's place in the field's list as the reader is served it: the owner's as stored, every other reader's as keel serves it (a contributor's keeps a link to a proposed element; the profile's ruling G5).</summary>
        public int Index { get; }

        /// <summary>The edge in the profile's words: <c>source</c>, <c>field</c>, <c>target</c>, <c>index</c>.</summary>
        public JObject ToJson() => new JObject
        {
            ["source"] = Source,
            ["field"] = Field,
            ["target"] = Target,
            ["index"] = Index,
        };
    }

    /// <summary><see cref="OWGraph.Read"/>'s answer.</summary>
    public sealed class OWWorldGraph
    {
        internal OWWorldGraph(IReadOnlyList<OWGraphNode> nodes, IReadOnlyList<OWGraphEdge> edges,
            IReadOnlyDictionary<string, int> edgesCut, IReadOnlyDictionary<string, int> cut, string refusedKey)
        {
            Nodes = nodes;
            Edges = edges;
            EdgesCut = edgesCut;
            Cut = cut;
            RefusedKey = refusedKey;
        }

        /// <summary>The nodes, by id in code point order.</summary>
        public IReadOnlyList<OWGraphNode> Nodes { get; }

        /// <summary>The edges, by source id, then field name (both in code point order), then index.</summary>
        public IReadOnlyList<OWGraphEdge> Edges { get; }

        /// <summary>
        /// The links left out because their target is not a node, counted by the target's reason, in
        /// <see cref="OWGraphCut.All"/>'s order; a reason with no count is absent. A reader that is not the owner never
        /// carries <see cref="OWGraphCut.OwnerOnly"/>; every reader carries <c>outside_lens</c>. A link to an id that is
        /// not in the world is a dangling link, left out and not counted.
        /// </summary>
        public IReadOnlyDictionary<string, int> EdgesCut { get; }

        /// <summary>
        /// With a lens, the lens's own cut (<see cref="OWLensResult.Cut"/>), unchanged. Without one, the elements out by
        /// default that the reader sees, counted by reason (<c>room</c>, <c>hidden</c> and <c>proposed</c> for the owner
        /// only, <c>map_space</c>, <c>reading_aid</c>).
        /// </summary>
        public IReadOnlyDictionary<string, int> Cut { get; }

        /// <summary>
        /// The profile's <c>unreadable</c>: with a lens whose <c>x_lens</c> cannot be read, the key refused
        /// (<see cref="OWLensResult.RefusedKey"/>), and the graph is empty. Null otherwise, and always without a lens.
        /// </summary>
        public string RefusedKey { get; }

        /// <summary>True unless a lens was named whose <c>x_lens</c> cannot be read.</summary>
        public bool IsReadable => RefusedKey == null;

        /// <summary>
        /// The graph in the profile's words, as the fixture writes its answers: <c>nodes</c>, <c>edges</c>,
        /// <c>edges_cut</c>, <c>cut</c> (each in <see cref="OWGraphCut.All"/>'s order) and <c>unreadable</c>. The same
        /// graph gives the same bytes.
        /// </summary>
        public JObject ToJson() => new JObject
        {
            ["nodes"] = new JArray(Nodes.Select(n => n.ToJson())),
            ["edges"] = new JArray(Edges.Select(e => e.ToJson())),
            ["edges_cut"] = Counts(EdgesCut),
            ["cut"] = Counts(Cut),
            ["unreadable"] = OWGraph.Text(RefusedKey),
        };

        private static JObject Counts(IReadOnlyDictionary<string, int> counts)
        {
            var o = new JObject();
            foreach (var why in OWGraphCut.All)
            {
                if (counts.TryGetValue(why, out var n) && n > 0) o[why] = n;
            }

            return o;
        }
    }

    /// <summary>
    /// Reads a world as a graph for one reader: the profile's <c>worldGraph(world, reader, options)</c>
    /// (<see cref="Read"/>). Element bodies are <see cref="JObject"/>s as the API or a world folder gives them, the world
    /// any sequence of them, read as <see cref="OWLens.Resolve(IEnumerable{JToken}, JObject, OWLensReader, OWLensOptions)"/>
    /// reads it, with the same <see cref="OWLensReader"/>.
    /// </summary>
    /// <remarks>
    /// A graph adds nothing to the world: no node or edge is stored, and a drawing never changes or adds a link. Positions
    /// are not part of it: a tool that bakes a layout keys it by id, never by name; colour and size stay the tool's.
    /// </remarks>
    public static class OWGraph
    {
        /// <summary>
        /// The extension fields a published profile declares a link, each a list of ids, read as stored for every reader:
        /// the lens profile's <c>x_lens_leads_to</c> and the spine profile's <c>x_spine_depends_on</c>. Another <c>x_</c>
        /// field is never an edge.
        /// </summary>
        public static readonly IReadOnlyList<string> DeclaredLinks = new[] { "x_lens_leads_to", "x_spine_depends_on" };

        /// <summary>The map space types, out by default for size: Map, Pin, Marker and Zone.</summary>
        public static readonly IReadOnlyList<string> MapSpaceTypes = new[] { "map", "pin", "marker", "zone" };

        /// <summary>
        /// The flags, as the profile names them, in the order a refusal names the first unmet one: <c>hidden</c>,
        /// <c>proposed</c>, <c>rooms</c>, <c>map_space</c>.
        /// </summary>
        public static readonly IReadOnlyList<string> Flags = new[] { "hidden", "proposed", "rooms", "map_space" };

        /// <summary>
        /// Reads a world as a graph for one reader: the elements the reader sees as nodes, the links between them as
        /// edges, and what was left out, counted by reason. Pure: it never writes, and never changes a body it is given.
        /// </summary>
        /// <param name="world">The world's elements as its holder has them, as <see cref="OWLens.Resolve(IEnumerable{JToken}, JObject, OWLensReader, OWLensOptions)"/> takes them.</param>
        /// <param name="reader">Who reads (<see cref="OWLensReader"/>); the owner reading as another is judged by that reader's rung.</param>
        /// <param name="options">The lens, its moment and the flags; null for none.</param>
        /// <remarks>
        /// <para>
        /// Without a lens the nodes are every element the reader sees less what is out by default: room elements, hidden
        /// ones, proposed ones and map space, each brought back by its flag and then marked (map space carries no mark),
        /// and the reading aids (<see cref="OWRoom.IsCharterElement"/>), which no flag brings back.
        /// With a lens (<see cref="OWGraphOptions.Lens"/> or <see cref="OWGraphOptions.LensId"/>) the nodes are exactly the
        /// lens's kept, its cut rides along unchanged, and a flag adds nothing: a lens means one thing everywhere. A lens
        /// whose <c>x_lens</c> cannot be read gives an empty graph naming the key; a lens id the reader is not served whole
        /// gives an empty graph that is readable, as does a lens body served only as a known-of stub.
        /// </para>
        /// <para>
        /// Each schema link field, and each declared extension link (<see cref="DeclaredLinks"/>), gives one edge per
        /// target that is a node, both ends as stored (a mirror pair is two edges, never merged; a Relation is a node with
        /// one edge per party). A known-of node has no edges out. A link to an element that is not a node is counted in
        /// <see cref="OWWorldGraph.EdgesCut"/> by its target's reason: the lens's cut reason (which wins over
        /// <c>not_visible</c>), else with a lens <c>outside_lens</c> for one the reader sees, else without one the first of
        /// the kinds not brought back, and <c>not_visible</c> for one the reader does not see (the owner's count).
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">No reader.</exception>
        /// <exception cref="ArgumentException">Both <see cref="OWGraphOptions.Lens"/> and <see cref="OWGraphOptions.LensId"/>.</exception>
        /// <exception cref="OWGraphRefusedException">
        /// <c>hidden</c> or <c>proposed</c> from a reader that is not the owner, or any flag from the public reader, refused
        /// by name before anything is read; when several are, the first in <see cref="Flags"/>' order.
        /// </exception>
        public static OWWorldGraph Read(IEnumerable<JToken> world, OWLensReader reader, OWGraphOptions options = null)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            var o = options ?? new OWGraphOptions();
            if (o.Lens != null && o.LensId != null)
                throw new ArgumentException("OWGraph.Read: a lens is a body (Lens) or an id (LensId), not both.", nameof(options));
            Refuse(reader, o);

            var view = new OWLens.View(world, reader, o.HiddenIds);
            var flagged = new Dictionary<string, bool>
            {
                [OWGraphCut.Room] = o.Rooms,
                [OWGraphCut.Hidden] = o.Hidden,
                [OWGraphCut.Proposed] = o.Proposed,
                [OWGraphCut.MapSpace] = o.MapSpace,
                [OWGraphCut.ReadingAid] = false,
            };
            var seenOwner = view.Rung == OWLensRung.Owner;

            List<string> KindsOf(string id)
            {
                var kinds = new List<string>();
                if (view.IsRoom(id)) kinds.Add(OWGraphCut.Room);
                if (view.Hidden.Contains(id)) kinds.Add(OWGraphCut.Hidden);
                if (view.Proposed.Contains(id)) kinds.Add(OWGraphCut.Proposed);
                if (MapSpaceTypes.Contains(view.TypeOf(id))) kinds.Add(OWGraphCut.MapSpace);
                if (view.IsReadingAid(id)) kinds.Add(OWGraphCut.ReadingAid);
                return kinds;
            }

            // What the reader could see as a node: its view, and for the owner what is written as proposed too.
            bool Candidate(string id) =>
                view.Sees(id) || (seenOwner && view.Proposed.Contains(id));

            var nodeIds = new HashSet<string>();
            IReadOnlyDictionary<string, int> cut;
            Func<string, string> reasonOf;
            string refusedKey = null;
            var lens = o.Lens ?? (o.LensId != null && view.Whole.Contains(o.LensId) ? view.Els[o.LensId] : null);
            if (o.Lens != null || o.LensId != null)
            {
                var reading = new OWLens.Reading(view, new OWLensOptions
                {
                    AsOf = o.AsOf, Now = o.Now, Clock = o.Clock, Members = o.Members,
                });
                var result = OWLens.Finish(reading, lens, out var cutById);
                nodeIds.UnionWith(result.Kept);
                cut = result.Cut;
                refusedKey = result.RefusedKey;
                reasonOf = id =>
                {
                    if (cutById.TryGetValue(id, out var why)) return why;
                    return view.Sees(id) ? OWGraphCut.OutsideLens : OWLensCut.NotVisible;
                };
            }
            else
            {
                string OutBy(string id) => KindsOf(id).FirstOrDefault(k => !flagged[k]);
                var counts = new Dictionary<string, int>();
                foreach (var id in view.Order)
                {
                    if (!Candidate(id)) continue;
                    var why = OutBy(id);
                    if (why == null) nodeIds.Add(id);
                    else counts[why] = counts.TryGetValue(why, out var n) ? n + 1 : 1;
                }

                cut = Tally(counts, view.OwnerCounts);
                reasonOf = id => Candidate(id) ? OutBy(id) : OWLensCut.NotVisible;
            }

            var order = OWCodePointComparer.Instance;
            var ids = nodeIds.OrderBy(i => i, order).ToList();
            var nodes = ids.Select(id =>
            {
                var type = view.TypeOf(id);
                var marks = KindsOf(id).Where(k => OWGraphMark.All.Contains(k)).ToList();
                return new OWGraphNode(id, StringOf(view.Els[id]["type"]), view.Els[id]["name"],
                    OWPresentation.FamilyOf(type), OWPresentation.IconOf(type),
                    marks, view.KnownOf.Contains(id));
            }).ToList();

            var edges = new List<OWGraphEdge>();
            var cutEdges = new Dictionary<string, int>();

            void Visit(string source, string field, JToken entry, int index)
            {
                var target = StringOf(entry);
                // A link to an id that is not in the world is a dangling link: left out, never counted.
                if (target == null || !view.Els.ContainsKey(target)) return;

                if (nodeIds.Contains(target))
                {
                    edges.Add(new OWGraphEdge(source, field, target, index));
                    return;
                }

                var why = reasonOf(target);
                if (why != null) cutEdges[why] = cutEdges.TryGetValue(why, out var n) ? n + 1 : 1;
            }

            foreach (var source in ids)
            {
                if (view.KnownOf.Contains(source)) continue;
                var body = view.Els[source];
                // A schema link is served whole to the owner; any other reader is served the list as keel serves it, a link
                // to a proposed element kept below the public rung, and the index is the place in that list (the profile's
                // ruling G5). A Pin's generic pair is placement, never an edge (G2).
                foreach (var (field, many) in OWLens.LinkFieldsOf(view.TypeOf(source)))
                {
                    var linkTarget = OWElementTypes.LinkTarget(view.TypeOf(source), field);
                    if (linkTarget == null) continue;
                    var value = body[field];
                    var entries = many
                        ? (value is JArray array ? array.ToList() : new List<JToken>())
                        : new List<JToken> { value };
                    var served = 0;
                    for (var i = 0; i < entries.Count; i++)
                    {
                        var t = StringOf(entries[i]);
                        var inServed = seenOwner || view.ServedAs(t, linkTarget);
                        Visit(source, field, entries[i], seenOwner ? i : served);
                        if (inServed) served++;
                    }
                }

                // A declared extension link is served as stored to every reader.
                foreach (var field in DeclaredLinks)
                {
                    if (!(body[field] is JArray list)) continue;
                    for (var i = 0; i < list.Count; i++) Visit(source, field, list[i], i);
                }
            }

            var sorted = edges.OrderBy(e => e.Source, order).ThenBy(e => e.Field, order).ThenBy(e => e.Index).ToList();
            return new OWWorldGraph(nodes, sorted, Tally(cutEdges, view.OwnerCounts), cut, refusedKey);
        }

        /// <summary>
        /// The flags the reader may not ask, judged by the rung its view is built from: <c>hidden</c> and <c>proposed</c>
        /// are the owner's alone, and the public reader takes none. The first unmet one in <see cref="Flags"/>' order is
        /// named.
        /// </summary>
        private static void Refuse(OWLensReader reader, OWGraphOptions o)
        {
            var rung = reader.Rung;
            if (o.Hidden && rung != OWLensRung.Owner)
                throw new OWGraphRefusedException("hidden", $"OWGraph.Read: hidden is the owner's alone; a {Word(rung)} reader may not ask it.");
            if (o.Proposed && rung != OWLensRung.Owner)
                throw new OWGraphRefusedException("proposed", $"OWGraph.Read: proposed is the owner's alone; a {Word(rung)} reader may not ask it.");

            if (rung != OWLensRung.Public) return;
            if (o.Rooms) throw new OWGraphRefusedException("rooms", "OWGraph.Read: a public reader takes no flag; it asked rooms.");
            if (o.MapSpace) throw new OWGraphRefusedException("map_space", "OWGraph.Read: a public reader takes no flag; it asked map_space.");
        }

        /// <summary>
        /// Each node's degree in a graph <see cref="Read"/> answered: the number of its edges, in or out, each edge counted
        /// once for every node it touches (a mirror pair is two edges, so each end counts 2; a link to itself counts once).
        /// Every node is a key, 0 included, enumerated in the graph's node order.
        /// </summary>
        /// <remarks>
        /// Edges join nodes only, so the count is the reader's: what the reading left out (hidden, room, proposed, map
        /// space, past a lens) is never counted. This is "most linked" for a finder: rank by it, highest first. Mirrors the
        /// TypeScript SDK's <c>degreeOf</c> (4.95.0) and the Python package's <c>degree_of</c> (0.78.0).
        /// </remarks>
        /// <exception cref="ArgumentNullException">No graph.</exception>
        public static IReadOnlyDictionary<string, int> DegreeOf(OWWorldGraph graph)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph), "OWGraph.DegreeOf: graph is what OWGraph.Read answers.");
            var degree = new NodeCounts(graph.Nodes.Select(n => n.Id));
            foreach (var e in graph.Edges)
            {
                degree.Add(e.Source);
                if (e.Target != e.Source) degree.Add(e.Target);
            }

            return degree;
        }

        /// <summary>
        /// Each node's out-degree in a graph <see cref="Read"/> answered: the number of distinct nodes its own link fields
        /// point at. A target listed in two fields counts once, a link to itself does not count, and a mirror pair (A lists
        /// B, B lists A) counts 1 at each end, since each counts its own list. Every node is a key, 0 included, enumerated
        /// in the graph's node order.
        /// </summary>
        /// <remarks>
        /// Like <see cref="DegreeOf"/>, the count is the reader's: what the reading left out is never counted. This is how
        /// far an element reaches out, apart from how often it is named. Mirrors the TypeScript SDK's <c>outDegreeOf</c>
        /// (4.98.0) and the Python package's <c>out_degree_of</c> (0.80.0).
        /// </remarks>
        /// <exception cref="ArgumentNullException">No graph.</exception>
        public static IReadOnlyDictionary<string, int> OutDegreeOf(OWWorldGraph graph)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph), "OWGraph.OutDegreeOf: graph is what OWGraph.Read answers.");
            var degree = new NodeCounts(graph.Nodes.Select(n => n.Id));
            var seen = new HashSet<(string, string)>();
            foreach (var e in graph.Edges)
            {
                if (e.Target == e.Source || !seen.Add((e.Source, e.Target))) continue;
                degree.Add(e.Source);
            }

            return degree;
        }

        /// <summary>A count per node id, enumerated in the order the ids were given; an id not given is never added.</summary>
        private sealed class NodeCounts : IReadOnlyDictionary<string, int>
        {
            private readonly List<string> _order = new List<string>();
            private readonly Dictionary<string, int> _counts = new Dictionary<string, int>();

            public NodeCounts(IEnumerable<string> ids)
            {
                foreach (var id in ids)
                {
                    if (id == null || _counts.ContainsKey(id)) continue;
                    _order.Add(id);
                    _counts[id] = 0;
                }
            }

            public void Add(string id)
            {
                if (id != null && _counts.TryGetValue(id, out var n)) _counts[id] = n + 1;
            }

            public int this[string key] => _counts[key];
            public IEnumerable<string> Keys => _order;
            public IEnumerable<int> Values => _order.Select(k => _counts[k]);
            public int Count => _order.Count;
            public bool ContainsKey(string key) => _counts.ContainsKey(key);
            public bool TryGetValue(string key, out int value) => _counts.TryGetValue(key, out value);

            public IEnumerator<KeyValuePair<string, int>> GetEnumerator() =>
                _order.Select(k => new KeyValuePair<string, int>(k, _counts[k])).GetEnumerator();

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private static string Word(OWLensRung rung) => rung.ToString().ToLowerInvariant();

        /// <summary>Counts in <see cref="OWGraphCut.All"/>'s order, a reason with no count left out, the owner's reasons kept for the owner.</summary>
        private static IReadOnlyDictionary<string, int> Tally(IReadOnlyDictionary<string, int> counts, bool owner)
        {
            var o = new Dictionary<string, int>();
            foreach (var why in OWGraphCut.All)
            {
                if (!owner && Array.IndexOf(OWGraphCut.OwnerOnly, why) >= 0) continue;
                if (counts.TryGetValue(why, out var n) && n > 0) o[why] = n;
            }

            return o;
        }

        /// <summary>A string as JSON: a JSON null for null, never a null string.</summary>
        internal static JToken Text(string value) => value == null ? JValue.CreateNull() : new JValue(value);

        /// <summary>A token's string, or null when it is not a JSON string.</summary>
        private static string StringOf(JToken token) =>
            token is JValue value && value.Type == JTokenType.String ? (string)value : null;
    }
}
