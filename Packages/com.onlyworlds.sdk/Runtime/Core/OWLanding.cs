// The landing profile: a world's first picture at a door (schema-dist profiles/landing.md, serial 103, ruled
// 2026-11-01). A real world shows its most-linked Characters, each with what it links; a small world shows its gaps,
// each an invitation to make the world bigger. It adds nothing to the world and stores nothing: it is a reading of the
// graph the lens profile defines (OWGraph.Read), so it sees exactly what that graph holds and never more.
//
// Written from the profile and its conformance fixture, profiles/landing.fixture.json, which is this file's own test
// (OWLandingTests). The packages' reader is landingOf(graph, options) (the Python package's landing_of): here
// OWLanding.Of. Degree is the graph's own count (OWGraph.DegreeOf).

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary>Which part a door draws first: the gaps under the size, the graph at it and above.</summary>
    public static class OWLandingMode
    {
        /// <summary>A graph with fewer nodes than the size: the door draws its gaps first.</summary>
        public const string Gaps = "gaps";

        /// <summary>A graph of the size or more: the door draws its clusters first.</summary>
        public const string Graph = "graph";
    }

    /// <summary>The kinds of gap, in the one order the gaps are given in.</summary>
    public static class OWLandingGapKind
    {
        /// <summary>A node with no edge: the links report's orphan.</summary>
        public const string Orphan = "orphan";

        /// <summary>A node with one edge: the links report's near-orphan.</summary>
        public const string NearOrphan = "near_orphan";

        /// <summary>A Character whose <c>location</c> names no Location node.</summary>
        public const string NoPlace = "no_place";

        /// <summary>A Location that no Character's <c>location</c> names.</summary>
        public const string NobodyHere = "nobody_here";

        /// <summary>Every kind, in the order the gaps are sorted in.</summary>
        public static readonly IReadOnlyList<string> All = Array.AsReadOnly(new[] { Orphan, NearOrphan, NoPlace, NobodyHere });
    }

    /// <summary>
    /// <see cref="OWLanding.Of"/>'s options, each a whole number of 1 or more; null is its default. The profile's
    /// <c>heads</c>, <c>hops</c> and <c>size</c>.
    /// </summary>
    public sealed class OWLandingOptions
    {
        /// <summary>The default of <see cref="Heads"/>.</summary>
        public const int DefaultHeads = 5;

        /// <summary>The default of <see cref="Hops"/>.</summary>
        public const int DefaultHops = 1;

        /// <summary>The default of <see cref="Size"/>.</summary>
        public const int DefaultSize = 24;

        /// <summary>The keys of the options as JSON, as the profile and its fixture write them, in the order they are read.</summary>
        public static readonly IReadOnlyList<string> JsonKeys = Array.AsReadOnly(new[] { "heads", "hops", "size" });

        /// <summary>How many clusters at most. Null: 5.</summary>
        public int? Heads { get; set; }

        /// <summary>How far a head reaches, in steps along an edge either way. Null: 1.</summary>
        public int? Hops { get; set; }

        /// <summary>The node count at which the mode turns from gaps to graph. Null: 24.</summary>
        public int? Size { get; set; }

        /// <summary>
        /// The options written as JSON, in the profile's words (<c>heads</c>, <c>hops</c>, <c>size</c>). A key given as
        /// null takes its default; a whole number written with a zero fraction (<c>2.0</c>) is that number, since a JSON
        /// reader with one number type cannot tell the two apart.
        /// </summary>
        /// <exception cref="ArgumentNullException">No options.</exception>
        /// <exception cref="OWLandingRefusedException">
        /// An unknown key, refused by name before any value is read; then a value that is not a whole number of 1 or
        /// more (a fraction, a string, a boolean, 0 or less, an infinity), named. A whole number past 2,147,483,647 reads
        /// as 2,147,483,647.
        /// </exception>
        public static OWLandingOptions FromJson(JObject options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options), "OWLandingOptions.FromJson: the options are an object, such as { heads: 5 }");
            foreach (var p in options.Properties())
            {
                if (!JsonKeys.Contains(p.Name))
                    throw new OWLandingRefusedException(p.Name,
                        $"OWLanding.Of: unknown option '{p.Name}'. The keys are {string.Join(", ", JsonKeys)}.");
            }

            return new OWLandingOptions
            {
                Heads = Whole(options, "heads"),
                Hops = Whole(options, "hops"),
                Size = Whole(options, "size"),
            };
        }

        /// <summary>A key's whole number, or null when absent or null; refused by name when it is anything else.</summary>
        private static int? Whole(JObject options, string key)
        {
            var t = options[key];
            if (t == null || t.Type == JTokenType.Null) return null;
            // A whole number too large for an int reads as int.MaxValue: any count past the graph's nodes means all of
            // them (the landing profile, ruled for serial 104).
            if (t.Type == JTokenType.Integer)
            {
                var raw = ((JValue)t).Value;
                if (raw is long v && v >= 1) return v > int.MaxValue ? int.MaxValue : (int)v;
                if (raw is System.Numerics.BigInteger b && b.Sign > 0) return int.MaxValue;
            }
            else if (t.Type == JTokenType.Float)
            {
                var v = (double)t;
                if (!double.IsInfinity(v) && v >= 1 && Math.Floor(v) == v) return v > int.MaxValue ? int.MaxValue : (int)v;
            }

            throw OWLanding.NotWhole(key, t.ToString(Newtonsoft.Json.Formatting.None));
        }
    }

    /// <summary>
    /// An option <see cref="OWLanding.Of"/> does not take, refused before anything is read: <see cref="Option"/> is the
    /// profile's word for it (<c>heads</c>, <c>hops</c>, <c>size</c>, or the unknown key as given).
    /// </summary>
    public sealed class OWLandingRefusedException : ArgumentException
    {
        internal OWLandingRefusedException(string option, string message)
            : base(message, "options")
        {
            Option = option;
        }

        /// <summary>The option refused, as the profile spells it, or the unknown key as given.</summary>
        public string Option { get; }
    }

    /// <summary>One node of a cluster: its steps from the head (0 for the head itself) and its degree.</summary>
    public sealed class OWLandingMember
    {
        internal OWLandingMember(string id, int hops, int degree)
        {
            Id = id;
            Hops = hops;
            Degree = degree;
        }

        /// <summary>The node's id.</summary>
        public string Id { get; }

        /// <summary>Steps from the head, 0 for the head itself.</summary>
        public int Hops { get; }

        /// <summary>The node's degree in the graph (<see cref="OWGraph.DegreeOf"/>).</summary>
        public int Degree { get; }

        /// <summary>The profile's <c>{id, hops, degree}</c>.</summary>
        public JObject ToJson() => new JObject { ["id"] = Id, ["hops"] = Hops, ["degree"] = Degree };
    }

    /// <summary>One head and the nodes that joined it.</summary>
    public sealed class OWLandingCluster
    {
        internal OWLandingCluster(string head, IReadOnlyList<OWLandingMember> members)
        {
            Head = head;
            Members = members;
        }

        /// <summary>The head: a Character, ranked by degree.</summary>
        public string Head { get; }

        /// <summary>The head first at 0, then by hops, then by degree (highest first), then by id in code point order.</summary>
        public IReadOnlyList<OWLandingMember> Members { get; }

        /// <summary>The profile's <c>{head, members}</c>.</summary>
        public JObject ToJson() => new JObject
        {
            ["head"] = Head,
            ["members"] = new JArray(Members.Select(m => (object)m.ToJson())),
        };
    }

    /// <summary>A node no head reaches, with its degree: the world around the clusters, which a drawing mutes.</summary>
    public sealed class OWLandingNode
    {
        internal OWLandingNode(string id, int degree)
        {
            Id = id;
            Degree = degree;
        }

        /// <summary>The node's id.</summary>
        public string Id { get; }

        /// <summary>The node's degree in the graph (<see cref="OWGraph.DegreeOf"/>).</summary>
        public int Degree { get; }

        /// <summary>The profile's <c>{id, degree}</c>.</summary>
        public JObject ToJson() => new JObject { ["id"] = Id, ["degree"] = Degree };
    }

    /// <summary>One gap: its kind (<see cref="OWLandingGapKind"/>) and the node that carries it.</summary>
    public sealed class OWLandingGap
    {
        internal OWLandingGap(string kind, string id)
        {
            Kind = kind;
            Id = id;
        }

        /// <summary>One of <see cref="OWLandingGapKind.All"/>.</summary>
        public string Kind { get; }

        /// <summary>The node's id.</summary>
        public string Id { get; }

        /// <summary>The profile's <c>{kind, id}</c>.</summary>
        public JObject ToJson() => new JObject { ["kind"] = Kind, ["id"] = Id };
    }

    /// <summary>
    /// A world's first picture at a door, as <see cref="Of"/> reads it from a graph: the mode, the size it was read at,
    /// the clusters, the nodes no head reaches, and the gaps. Structure only, never coordinates: a layout is the drawing
    /// tool's, keyed by id.
    /// </summary>
    public sealed class OWLanding
    {
        private OWLanding(string mode, int size, IReadOnlyList<OWLandingCluster> clusters,
            IReadOnlyList<OWLandingNode> unclustered, IReadOnlyList<OWLandingGap> gaps)
        {
            Mode = mode;
            Size = size;
            Clusters = clusters;
            Unclustered = unclustered;
            Gaps = gaps;
        }

        /// <summary><see cref="OWLandingMode.Gaps"/> when the graph has fewer nodes than <see cref="Size"/>, else <see cref="OWLandingMode.Graph"/>.</summary>
        public string Mode { get; }

        /// <summary>The size the mode was read at.</summary>
        public int Size { get; }

        /// <summary>One per head, in the heads' order (most linked first, ties by id in code point order).</summary>
        public IReadOnlyList<OWLandingCluster> Clusters { get; }

        /// <summary>Every node no head reaches, by degree (highest first), then by id in code point order.</summary>
        public IReadOnlyList<OWLandingNode> Unclustered { get; }

        /// <summary>In <see cref="OWLandingGapKind.All"/>'s order, then by id in code point order; a node can carry several.</summary>
        public IReadOnlyList<OWLandingGap> Gaps { get; }

        /// <summary>The answer in the profile's words: <c>mode</c>, <c>size</c>, <c>clusters</c>, <c>unclustered</c>, <c>gaps</c>, all five always, in that order.</summary>
        public JObject ToJson() => new JObject
        {
            ["mode"] = Mode,
            ["size"] = Size,
            ["clusters"] = new JArray(Clusters.Select(c => (object)c.ToJson())),
            ["unclustered"] = new JArray(Unclustered.Select(u => (object)u.ToJson())),
            ["gaps"] = new JArray(Gaps.Select(g => (object)g.ToJson())),
        };

        /// <summary>
        /// Reads a world's first picture from the graph <see cref="OWGraph.Read"/> answered for the visitor, so it holds
        /// exactly what that reader may see: the room, hidden and proposed elements, map space and the reading aids are
        /// already out, and a reader that wants them back asks the graph. Pure: it never changes the graph.
        /// </summary>
        /// <param name="graph">What <see cref="OWGraph.Read"/> answered for the visitor.</param>
        /// <param name="options">The options (<see cref="OWLandingOptions"/>, or <see cref="OWLandingOptions.FromJson"/>); null for the defaults.</param>
        /// <remarks>
        /// <para>
        /// Degree is <see cref="OWGraph.DegreeOf"/>'s: every edge, in or out, once at each node it touches, so a mirror pair
        /// counts 2 at each end and a link to itself once. The mode is <c>gaps</c> when the graph has fewer nodes than the
        /// size, <c>graph</c> otherwise; both parts are always read.
        /// </para>
        /// <para>
        /// Heads: the Characters with at least one edge, most linked first, ties by id in code point order, at most
        /// <c>heads</c>; a known-of node is never a head. A node within <c>hops</c> steps of a head (a step follows an edge
        /// either way, and a mirror pair is one step) joins the head it is fewest steps from, a tie going to the
        /// higher-ranked head. Every node no head reaches is unclustered.
        /// </para>
        /// <para>
        /// Gaps, for every node that is not known-of: <c>orphan</c> (no edge) and <c>near_orphan</c> (one edge), the links
        /// report's own; <c>no_place</c>, a Character whose <c>location</c> names no Location node (a place hidden from the
        /// visitor is no node, so it reads as none); <c>nobody_here</c>, a Location no Character's <c>location</c> names
        /// (another type's <c>location</c> does not count). Everything is counted in integers and ids compare by code
        /// point, never by UTF-16 unit, so the same graph and options give the same answer, byte for byte.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">No graph.</exception>
        /// <exception cref="OWLandingRefusedException">An option less than 1, named, before anything is read.</exception>
        public static OWLanding Of(OWWorldGraph graph, OWLandingOptions options = null)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph), "OWLanding.Of: graph is what OWGraph.Read answers.");
            var k = Option(options?.Heads, "heads", OWLandingOptions.DefaultHeads);
            var reach = Option(options?.Hops, "hops", OWLandingOptions.DefaultHops);
            var size = Option(options?.Size, "size", OWLandingOptions.DefaultSize);

            var degree = OWGraph.DegreeOf(graph);
            int Deg(string id) => id != null && degree.TryGetValue(id, out var d) ? d : 0;

            var near = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var type = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var n in graph.Nodes)
            {
                if (n.Id == null || near.ContainsKey(n.Id)) continue;
                near[n.Id] = new HashSet<string>(StringComparer.Ordinal);
                type[n.Id] = n.Type;
            }

            var placed = new HashSet<string>(StringComparer.Ordinal);
            var inhabited = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in graph.Edges)
            {
                if (e.Source == null || e.Target == null) continue;
                if (e.Source != e.Target && near.TryGetValue(e.Source, out var fromSource) && near.TryGetValue(e.Target, out var fromTarget))
                {
                    fromSource.Add(e.Target);
                    fromTarget.Add(e.Source);
                }

                if (e.Field == "location" && type.TryGetValue(e.Source, out var st) && st == "character"
                    && type.TryGetValue(e.Target, out var tt) && tt == "location")
                {
                    placed.Add(e.Source);
                    inhabited.Add(e.Target);
                }
            }

            var heads = graph.Nodes
                .Where(n => n.Id != null && n.Type == "character" && !n.KnownOf && Deg(n.Id) > 0)
                .Select(n => n.Id)
                .ToList();
            heads.Sort((a, b) =>
            {
                var c = Deg(b).CompareTo(Deg(a));
                return c != 0 ? c : OWCodePointComparer.Instance.Compare(a, b);
            });
            if (heads.Count > k) heads.RemoveRange(k, heads.Count - k);

            // Each node's least (hops, rank) over every head's own walk, so no queue order ever decides a tie.
            var best = new Dictionary<string, (int hops, int rank)>(StringComparer.Ordinal);
            for (var rank = 0; rank < heads.Count; rank++)
            {
                var head = heads[rank];
                var seen = new Dictionary<string, int>(StringComparer.Ordinal) { [head] = 0 };
                var frontier = new List<string> { head };
                for (var step = 0; step < reach && frontier.Count > 0; step++)
                {
                    var next = new List<string>();
                    foreach (var id in frontier)
                    {
                        if (!near.TryGetValue(id, out var neighbours)) continue;
                        foreach (var nb in neighbours)
                        {
                            if (seen.ContainsKey(nb)) continue;
                            seen[nb] = step + 1;
                            next.Add(nb);
                        }
                    }

                    frontier = next;
                }

                foreach (var kv in seen)
                {
                    if (!best.TryGetValue(kv.Key, out var b) || kv.Value < b.hops || (kv.Value == b.hops && rank < b.rank))
                        best[kv.Key] = (kv.Value, rank);
                }
            }

            var members = heads.Select(_ => new List<OWLandingMember>()).ToList();
            var unclustered = new List<OWLandingNode>();
            foreach (var n in graph.Nodes)
            {
                if (n.Id != null && best.TryGetValue(n.Id, out var b)) members[b.rank].Add(new OWLandingMember(n.Id, b.hops, Deg(n.Id)));
                else unclustered.Add(new OWLandingNode(n.Id, Deg(n.Id)));
            }

            foreach (var list in members)
            {
                list.Sort((a, b) =>
                {
                    var c = a.Hops.CompareTo(b.Hops);
                    if (c != 0) return c;
                    c = b.Degree.CompareTo(a.Degree);
                    return c != 0 ? c : OWCodePointComparer.Instance.Compare(a.Id, b.Id);
                });
            }

            unclustered.Sort((a, b) =>
            {
                var c = b.Degree.CompareTo(a.Degree);
                return c != 0 ? c : OWCodePointComparer.Instance.Compare(a.Id, b.Id);
            });

            var gaps = new List<OWLandingGap>();
            foreach (var n in graph.Nodes)
            {
                if (n.KnownOf || n.Id == null) continue;
                var d = Deg(n.Id);
                if (d == 0) gaps.Add(new OWLandingGap(OWLandingGapKind.Orphan, n.Id));
                if (d == 1) gaps.Add(new OWLandingGap(OWLandingGapKind.NearOrphan, n.Id));
                if (n.Type == "character" && !placed.Contains(n.Id)) gaps.Add(new OWLandingGap(OWLandingGapKind.NoPlace, n.Id));
                if (n.Type == "location" && !inhabited.Contains(n.Id)) gaps.Add(new OWLandingGap(OWLandingGapKind.NobodyHere, n.Id));
            }

            gaps.Sort((a, b) =>
            {
                var c = KindRank(a.Kind).CompareTo(KindRank(b.Kind));
                return c != 0 ? c : OWCodePointComparer.Instance.Compare(a.Id, b.Id);
            });

            var clusters = heads.Select((h, i) => new OWLandingCluster(h, members[i])).ToList();
            return new OWLanding(graph.Nodes.Count < size ? OWLandingMode.Gaps : OWLandingMode.Graph, size, clusters,
                unclustered, gaps);
        }

        private static int KindRank(string kind)
        {
            for (var i = 0; i < OWLandingGapKind.All.Count; i++)
                if (OWLandingGapKind.All[i] == kind) return i;
            return OWLandingGapKind.All.Count;
        }

        private static int Option(int? value, string key, int fallback)
        {
            if (value == null) return fallback;
            if (value.Value < 1) throw NotWhole(key, value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return value.Value;
        }

        internal static OWLandingRefusedException NotWhole(string key, string got) =>
            new OWLandingRefusedException(key, $"OWLanding.Of: {key} is a whole number, 1 or more; got {got}.");
    }
}
