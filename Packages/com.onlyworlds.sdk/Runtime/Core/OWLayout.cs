// Two layout methods for a world nobody laid out, read from its graph as OWGraph.WorldGraph answers it (the TypeScript
// SDK's layoutByKind and layoutAround, 4.167.0, and the Python package's layout_by_kind and layout_around, 0.127.0).
//
// by-kind: each kind of element gets a point on a circle, links act as springs, neighbours push apart, for a fixed
// number of seeded steps; the answer is fitted into a square. around: one element at the centre, its neighbours on rings
// round it, and at two steps their neighbours further out, near the element that reached them.
//
// Written from the shared fixture (Tests/Editor/Fixtures/layout/by-kind.json, byte for byte the two packages' copy),
// which is this file's own test (OWLayoutTests): by-kind's points equal to the last bit, around's within 1e-12.

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary>One element's point, in a frame of -1 to 1 on the picture's longer axis, centred.</summary>
    public sealed class OWLayoutPoint
    {
        internal OWLayoutPoint(string id, double x, double y)
        {
            Id = id;
            X = x;
            Y = y;
        }

        /// <summary>The element's id.</summary>
        public string Id { get; }

        /// <summary>Its x.</summary>
        public double X { get; }

        /// <summary>Its y.</summary>
        public double Y { get; }
    }

    /// <summary>One element's point round the focus: the focus at 0, 0, and how many links away from it the element is.</summary>
    public sealed class OWLayoutAroundPoint
    {
        internal OWLayoutAroundPoint(string id, int depth, double x, double y)
        {
            Id = id;
            Depth = depth;
            X = x;
            Y = y;
        }

        /// <summary>The element's id.</summary>
        public string Id { get; }

        /// <summary>0 for the focus, 1 for its neighbours, 2 for theirs.</summary>
        public int Depth { get; }

        /// <summary>Its x.</summary>
        public double X { get; }

        /// <summary>Its y.</summary>
        public double Y { get; }
    }

    /// <summary>What <see cref="OWLayout.LayoutByKind(JObject)"/> answers: one point per node, in the graph's node order, and the method that made them.</summary>
    public sealed class OWLayoutByKind
    {
        internal OWLayoutByKind(IReadOnlyList<OWLayoutPoint> points) => Points = points;

        /// <summary><c>by-kind</c>.</summary>
        public string Method => "by-kind";

        /// <summary>The method's version, 1, so a saved layout can say what made it.</summary>
        public int Version => 1;

        /// <summary>One point per node, in the order the nodes were handed in.</summary>
        public IReadOnlyList<OWLayoutPoint> Points { get; }
    }

    /// <summary>What <see cref="OWLayout.LayoutAround(JObject, string, int)"/> answers: the focus and every element within the steps, in the graph's node order.</summary>
    public sealed class OWLayoutAround
    {
        internal OWLayoutAround(IReadOnlyList<OWLayoutAroundPoint> points) => Points = points;

        /// <summary><c>around</c>.</summary>
        public string Method => "around";

        /// <summary>The method's version, 1.</summary>
        public int Version => 1;

        /// <summary>The focus and every element within the steps, in the order the nodes were handed in.</summary>
        public IReadOnlyList<OWLayoutAroundPoint> Points { get; }
    }

    /// <summary>
    /// The default layout methods: <see cref="LayoutByKind(JObject)"/>, a whole world's picture, and
    /// <see cref="LayoutAround(JObject, string, int)"/>, one element's. Both are exact: the same graph gives the same
    /// points as the TypeScript SDK and the Python package.
    /// </summary>
    /// <remarks>
    /// Everything but the anchors and around's cosines and sines is addition, subtraction, multiplication, division,
    /// square root and floor, written in the TypeScript SDK's order (each product and sum rounded on its own, never
    /// fused), with JavaScript's <c>Math.min</c> and <c>Math.max</c> (<see cref="MinJs"/>, <see cref="MaxJs"/>), so the
    /// sign of a zero matches too. The anchors are a table of bit patterns (<c>OWLayout.Anchors.cs</c>, generated from the
    /// shared fixture), never computed.
    /// </remarks>
    public static partial class OWLayout
    {
        private const int Steps = 320;
        private const int CellMax = 12;
        private const int RingOne = 60;

        private sealed class Node
        {
            public string Id;
            public string Type;
            public string Name;
        }

        private sealed class Edge
        {
            public string Source;
            public string Target;
            public string Field;
            public double Index;
        }

        private sealed class Ordered
        {
            public List<Node> Handed;
            public Node[] Nodes;
            public Edge[] Edges;
            public Dictionary<string, int> Index;
        }

        /// <summary>Lay out a whole graph by kind: <see cref="LayoutByKind(JObject)"/> on the graph's own words (<see cref="OWWorldGraph.ToJson"/>).</summary>
        /// <exception cref="ArgumentException">The graph cannot be read (a node whose name is neither text nor null, for one).</exception>
        public static OWLayoutByKind LayoutByKind(OWWorldGraph graph) => LayoutByKind(graph?.ToJson());

        /// <summary>
        /// Lay out a whole graph by kind. Each kind present gets an anchor on a circle, in colour group order
        /// (<c>colors.order</c>) and then by type word; every element starts near its kind's anchor, at a seeded jitter.
        /// Then, for 320 steps (160 past 2,000 elements): elements closer than a few rest lengths push each other apart (a
        /// cell holding more than 12 pushes with a rotating sample of 12, scaled up), every link pulls its two ends towards
        /// a rest length, every element is drawn back towards its kind's anchor, and no element moves further in one step
        /// than a cap that shrinks as the steps run out. Last, the points are fitted into a frame of -1 to 1 on the longer
        /// axis, centred, proportions kept.
        /// </summary>
        /// <remarks>
        /// <paramref name="graph"/> is <c>{nodes, edges}</c> in the TypeScript SDK's words: each node <c>{id, type,
        /// name?}</c> (the type a slug), each edge <c>{source, target, field?, index?}</c>. The graph is first put in one
        /// order (nodes by id; links by source, field, index, then target; every word by code point), so the order the
        /// nodes and links are handed in moves nothing. Answers one point per node in the order handed, with the method's
        /// name and version. An empty graph answers no points; one element answers 0, 0. Self-links pull nothing; a link
        /// to an id that is not a node is passed over.
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// The graph cannot be read, named in the message: no nodes or edges list, a node that is not an object or has no
        /// id, a type that is not one of the 22, a name that is neither text nor null, an id twice, an edge without text
        /// <c>source</c> and <c>target</c>, a <c>field</c> that is there and not text, an <c>index</c> that is there and not
        /// a finite number.
        /// </exception>
        public static OWLayoutByKind LayoutByKind(JObject graph)
        {
            var g = ReadGraph("LayoutByKind", graph);
            var nodes = g.Nodes;
            var n = nodes.Length;
            if (n <= 1)
            {
                var zero = new List<OWLayoutPoint>(g.Handed.Count);
                foreach (var x in g.Handed) zero.Add(new OWLayoutPoint(x.Id, 0, 0));
                return new OWLayoutByKind(zero);
            }

            var edges = new List<(int A, int B)>();
            foreach (var e in g.Edges)
            {
                if (g.Index.TryGetValue(e.Source, out var a) && g.Index.TryGetValue(e.Target, out var b) && a != b) edges.Add((a, b));
            }

            var types = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var x in nodes)
                if (seen.Add(x.Type)) types.Add(x.Type);
            types.Sort((a, b) =>
            {
                var d = GroupRank[a] - GroupRank[b];
                return d != 0 ? d : OWCodePointComparer.Instance.Compare(a, b);
            });
            var k = types.Count;
            var anchorX = new Dictionary<string, double>(StringComparer.Ordinal);
            var anchorY = new Dictionary<string, double>(StringComparer.Ordinal);
            for (var i = 0; i < k; i++)
            {
                anchorX[types[i]] = Anchor(k * (k - 1) + 2 * i);
                anchorY[types[i]] = Anchor(k * (k - 1) + 2 * i + 1);
            }

            uint seed = 7;
            var px = new double[n];
            var py = new double[n];
            var tx = new double[n];
            var ty = new double[n];
            for (var i = 0; i < n; i++)
            {
                var ax = anchorX[nodes[i].Type];
                var ay = anchorY[nodes[i].Type];
                px[i] = ax * 0.8 + (Next(ref seed) - 0.5) * 0.5;
                py[i] = ay * 0.8 + (Next(ref seed) - 0.5) * 0.5;
                tx[i] = ax * 0.85;
                ty[i] = ay * 0.85;
            }

            var rest = MaxJs(0.002, 0.9 / Math.Sqrt(n));
            var push = rest * rest * 1.4;
            var cell = rest * 2.2;
            var cell2 = cell * cell;
            var fx = new double[n];
            var fy = new double[n];
            var steps = n > 2000 ? Steps / 2 : Steps;
            var grid = new Dictionary<long, List<int>>();

            for (var it = 0; it < steps; it++)
            {
                var t = 1 - (double)it / steps;
                Array.Clear(fx, 0, n);
                Array.Clear(fy, 0, n);
                grid.Clear();
                for (var i = 0; i < n; i++)
                {
                    var key = CellKey((long)Math.Floor(px[i] / cell), (long)Math.Floor(py[i] / cell));
                    if (grid.TryGetValue(key, out var c)) c.Add(i);
                    else grid[key] = new List<int> { i };
                }

                for (var i = 0; i < n; i++)
                {
                    var cx = (long)Math.Floor(px[i] / cell);
                    var cy = (long)Math.Floor(py[i] / cell);
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        for (var dy = -1; dy <= 1; dy++)
                        {
                            if (!grid.TryGetValue(CellKey(cx + dx, cy + dy), out var cellOf)) continue;
                            if (cellOf.Count > CellMax)
                            {
                                // A crowded cell: a sample of 12 that turns with the element and the step, each push scaled up to the whole.
                                var len = cellOf.Count;
                                var off = (i * 7 + it * 13) % len;
                                var scale = (double)len / CellMax;
                                for (var s = 0; s < CellMax; s++)
                                {
                                    var j = cellOf[(off + s) % len];
                                    if (j == i) continue;
                                    var ux = px[i] - px[j];
                                    var uy = py[i] - py[j];
                                    var d2 = ux * ux + uy * uy + 1e-6;
                                    if (d2 > cell2) continue;
                                    var d = Math.Sqrt(d2);
                                    var f = (push * scale) / d2;
                                    fx[i] += (ux / d) * f;
                                    fy[i] += (uy / d) * f;
                                }

                                continue;
                            }

                            foreach (var j in cellOf)
                            {
                                if (j <= i) continue;
                                var ux = px[i] - px[j];
                                var uy = py[i] - py[j];
                                var d2 = ux * ux + uy * uy + 1e-6;
                                if (d2 > cell2) continue;
                                var d = Math.Sqrt(d2);
                                var f = push / d2;
                                fx[i] += (ux / d) * f;
                                fy[i] += (uy / d) * f;
                                fx[j] -= (ux / d) * f;
                                fy[j] -= (uy / d) * f;
                            }
                        }
                    }
                }

                foreach (var (a, b) in edges)
                {
                    var ux = px[b] - px[a];
                    var uy = py[b] - py[a];
                    var d = Math.Sqrt(ux * ux + uy * uy) + 1e-6;
                    var f = (d - rest) * 0.06;
                    fx[a] += (ux / d) * f;
                    fy[a] += (uy / d) * f;
                    fx[b] -= (ux / d) * f;
                    fy[b] -= (uy / d) * f;
                }

                for (var i = 0; i < n; i++)
                {
                    fx[i] += (tx[i] - px[i]) * 0.06;
                    fy[i] += (ty[i] - py[i]) * 0.06;
                    var m = Math.Sqrt(fx[i] * fx[i] + fy[i] * fy[i]);
                    var cap = 0.03 * t + 0.002;
                    var s = m > cap ? cap / m : 1;
                    px[i] += fx[i] * s;
                    py[i] += fy[i] * s;
                }
            }

            var x0 = double.PositiveInfinity;
            var x1 = double.NegativeInfinity;
            var y0 = double.PositiveInfinity;
            var y1 = double.NegativeInfinity;
            for (var i = 0; i < n; i++)
            {
                x0 = MinJs(x0, px[i]);
                x1 = MaxJs(x1, px[i]);
                y0 = MinJs(y0, py[i]);
                y1 = MaxJs(y1, py[i]);
            }

            var span = MaxJs(x1 - x0, y1 - y0);
            var sc = span > 1e-9 ? 2 / span : 1;
            var mx = (x0 + x1) / 2;
            var my = (y0 + y1) / 2;
            var points = new List<OWLayoutPoint>(n);
            foreach (var x in g.Handed)
            {
                var i = g.Index[x.Id];
                points.Add(new OWLayoutPoint(x.Id, (px[i] - mx) * sc, (py[i] - my) * sc));
            }

            return new OWLayoutByKind(points);
        }

        /// <summary>Lay out one element and what is linked to it: <see cref="LayoutAround(JObject, string, int)"/> on the graph's own words (<see cref="OWWorldGraph.ToJson"/>).</summary>
        /// <exception cref="ArgumentException">A <paramref name="steps"/> that is not 1 or 2, a graph it cannot read, or a focus that is not one of its nodes.</exception>
        public static OWLayoutAround LayoutAround(OWWorldGraph graph, string focus, int steps = 1)
        {
            CheckSteps(steps);
            return LayoutAround(graph?.ToJson(), focus, steps);
        }

        /// <summary>
        /// Lay out one element and what is linked to it. The focus stands at 0, 0. Its neighbours (one link away, either
        /// direction) stand on a ring round it, in colour group order, then by type word, then by name (code point order,
        /// the same in every language and locale), up to 60 on the first ring and further rings beyond it. With
        /// <paramref name="steps"/> 2, each neighbour's own neighbours stand on an outer ring, fanned out near the
        /// neighbour that reached them.
        /// </summary>
        /// <remarks>
        /// The graph is <see cref="LayoutByKind(JObject)"/>'s, put in the same one order first. Answers the focus and every
        /// element within the steps, in the order the nodes were handed in, each with its depth (0, 1 or 2), and the
        /// method's name and version. Nothing iterates, so each point is one cosine and one sine of an angle the method
        /// computes exactly; runtimes agree to within about 1e-12.
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// A <paramref name="steps"/> that is not 1 or 2 (checked first), a graph it cannot read (as
        /// <see cref="LayoutByKind(JObject)"/>), or a focus that is not one of the graph's nodes.
        /// </exception>
        public static OWLayoutAround LayoutAround(JObject graph, string focus, int steps = 1)
        {
            CheckSteps(steps);
            var g = ReadGraph("LayoutAround", graph);
            var nodes = g.Nodes;
            if (focus == null || !g.Index.TryGetValue(focus, out var centre))
                throw new ArgumentException($"LayoutAround: the focus {focus ?? "null"} is not a node of the graph");

            var n = nodes.Length;
            var adj = new List<int>[n];
            for (var i = 0; i < n; i++) adj[i] = new List<int>();
            foreach (var e in g.Edges)
            {
                if (!g.Index.TryGetValue(e.Source, out var a) || !g.Index.TryGetValue(e.Target, out var b)) continue;
                adj[a].Add(b);
                if (a != b) adj[b].Add(a);
            }

            var depth = new int[n];
            for (var i = 0; i < n; i++) depth[i] = -1;
            depth[centre] = 0;
            var via = new int[n];
            // Each step's newly reached elements in the order they were reached: the order the TypeScript SDK's depth
            // map holds them in, which orders each neighbour's fan.
            var reached = new List<int>[steps + 1];
            var frontier = new List<int> { centre };
            for (var s = 1; s <= steps; s++)
            {
                var next = new List<int>();
                foreach (var a in frontier)
                {
                    foreach (var to in adj[a])
                    {
                        if (depth[to] >= 0) continue;
                        depth[to] = s;
                        via[to] = a;
                        next.Add(to);
                    }
                }

                reached[s] = next;
                frontier = next;
            }

            var ring1 = new List<int>(reached[1]);
            ring1.Sort((a, b) =>
            {
                var d = GroupRank[nodes[a].Type] - GroupRank[nodes[b].Type];
                if (d != 0) return d;
                d = OWCodePointComparer.Instance.Compare(nodes[a].Type, nodes[b].Type);
                if (d != 0) return d;
                d = OWCodePointComparer.Instance.Compare(nodes[a].Name ?? string.Empty, nodes[b].Name ?? string.Empty);
                return d != 0 ? d : a - b;
            });

            var placedAt = new bool[n];
            var atX = new double[n];
            var atY = new double[n];
            var angle = new double[n];
            placedAt[centre] = true;
            if (ring1.Count > 0)
            {
                var first = Math.Min(ring1.Count, RingOne);
                var r1 = MaxJs(0.34, first * 0.017);
                var gap = (2 * Math.PI * r1) / first;
                var placed = 0;
                var r = r1;
                while (placed < ring1.Count)
                {
                    var room = (int)MaxJs(1, Math.Floor((2 * Math.PI * r) / gap));
                    var count = Math.Min(room, ring1.Count - placed);
                    for (var k = 0; k < count; k++)
                    {
                        var j = ring1[placed + k];
                        var a = -Math.PI / 2 + (2 * Math.PI * (k + 0.5)) / count;
                        angle[j] = a;
                        atX[j] = Math.Cos(a) * r;
                        atY[j] = Math.Sin(a) * r;
                        placedAt[j] = true;
                    }

                    placed += count;
                    r += gap * 1.2;
                }

                var outer = r - gap * 1.2;
                if (steps == 2)
                {
                    var kids = new Dictionary<int, List<int>>();
                    foreach (var j in reached[2])
                    {
                        var p = via[j];
                        if (kids.TryGetValue(p, out var l)) l.Add(j);
                        else kids[p] = new List<int> { j };
                    }

                    var spread = MinJs(0.5, ((2 * Math.PI) / Math.Max(1, ring1.Count)) * 0.9);
                    foreach (var pair in kids)
                    {
                        var a0 = angle[pair.Key];
                        var ks = pair.Value;
                        for (var k = 0; k < ks.Count; k++)
                        {
                            var j = ks[k];
                            var a = a0 + (ks.Count == 1 ? 0 : ((double)k / (ks.Count - 1) - 0.5) * spread);
                            atX[j] = Math.Cos(a) * outer * 1.8;
                            atY[j] = Math.Sin(a) * outer * 1.8;
                            placedAt[j] = true;
                        }
                    }
                }
            }

            var points = new List<OWLayoutAroundPoint>();
            foreach (var x in g.Handed)
            {
                var j = g.Index[x.Id];
                if (placedAt[j]) points.Add(new OWLayoutAroundPoint(x.Id, depth[j], atX[j], atY[j]));
            }

            return new OWLayoutAround(points);
        }

        private static void CheckSteps(int steps)
        {
            if (steps != 1 && steps != 2) throw new ArgumentException($"LayoutAround: steps is 1 or 2, not {steps}");
        }

        /// <summary>
        /// Check the graph, then put it in the method's one order, whatever order the caller handed: the nodes by id, the
        /// edges by source, then field (none as ''), then index (none as 0), then target, every word in code point order.
        /// </summary>
        private static Ordered ReadGraph(string fn, JObject graph)
        {
            Exception Fail(string msg) => new ArgumentException($"{fn}: {msg}");
            if (graph == null || !(graph["nodes"] is JArray givenNodes) || !(graph["edges"] is JArray givenEdges))
                throw Fail("the graph is { nodes, edges }, as WorldGraph answers it");

            var handed = new List<Node>(givenNodes.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < givenNodes.Count; i++)
            {
                // An array is an object to JavaScript's typeof, one with no id.
                if (givenNodes[i] is JArray) throw Fail($"node {i} has no id");
                if (!(givenNodes[i] is JObject node)) throw Fail($"node {i} is not an object");
                var idToken = node["id"];
                var id = idToken != null && idToken.Type == JTokenType.String ? (string)idToken : null;
                if (string.IsNullOrEmpty(id)) throw Fail($"node {i} has no id");
                var typeToken = node["type"];
                var type = typeToken != null && typeToken.Type == JTokenType.String ? (string)typeToken : null;
                if (type == null || !GroupRank.ContainsKey(type))
                    throw Fail($"node {id}: '{(typeToken == null ? "undefined" : typeToken.Type == JTokenType.String ? type : typeToken.ToString(Newtonsoft.Json.Formatting.None))}' is not one of the 22 element types");
                var nameToken = node["name"];
                if (nameToken != null && nameToken.Type != JTokenType.Null && nameToken.Type != JTokenType.String)
                    throw Fail($"node {id}: name is a string or null");
                if (!seen.Add(id)) throw Fail($"the id {id} is a node twice");
                handed.Add(new Node { Id = id, Type = type, Name = nameToken != null && nameToken.Type == JTokenType.String ? (string)nameToken : null });
            }

            var edges = new Edge[givenEdges.Count];
            for (var k = 0; k < givenEdges.Count; k++)
            {
                var e = givenEdges[k] as JObject;
                var source = e?["source"];
                var target = e?["target"];
                if (e == null || source == null || source.Type != JTokenType.String || target == null || target.Type != JTokenType.String)
                    throw Fail($"edge {k} is not {{ source, target }}");
                var field = e.Property("field");
                if (field != null && field.Value.Type != JTokenType.String) throw Fail($"edge {k}: field is a string");
                var index = e.Property("index");
                double at = 0;
                if (index != null)
                {
                    if (index.Value.Type != JTokenType.Integer && index.Value.Type != JTokenType.Float) throw Fail($"edge {k}: index is a number");
                    at = (double)index.Value;
                    if (double.IsNaN(at) || double.IsInfinity(at)) throw Fail($"edge {k}: index is a number");
                }

                edges[k] = new Edge { Source = (string)source, Target = (string)target, Field = field == null ? null : (string)field.Value, Index = at };
            }

            var nodes = handed.ToArray();
            Array.Sort(nodes, (a, b) => OWCodePointComparer.Instance.Compare(a.Id, b.Id));
            Array.Sort(edges, (a, b) =>
            {
                var d = OWCodePointComparer.Instance.Compare(a.Source, b.Source);
                if (d != 0) return d;
                d = OWCodePointComparer.Instance.Compare(a.Field ?? string.Empty, b.Field ?? string.Empty);
                if (d != 0) return d;
                if (a.Index != b.Index) return a.Index < b.Index ? -1 : 1;
                return OWCodePointComparer.Instance.Compare(a.Target, b.Target);
            });
            var place = new Dictionary<string, int>(nodes.Length, StringComparer.Ordinal);
            for (var i = 0; i < nodes.Length; i++) place[nodes[i].Id] = i;
            return new Ordered { Handed = handed, Nodes = nodes, Edges = edges, Index = place };
        }

        private static double Anchor(int i) => BitConverter.Int64BitsToDouble(unchecked((long)AnchorBits[i]));

        /// <summary>The 32-bit linear congruential generator, exact in float64 in every language.</summary>
        private static double Next(ref uint s)
        {
            s = unchecked(s * 1664525u + 1013904223u);
            return s / 4294967296.0;
        }

        private static long CellKey(long x, long y) => (x + 1048576) * 2097152 + (y + 1048576);

        /// <summary>JavaScript's <c>Math.min</c> of two: NaN if either is, and -0 below +0 (Mono's <c>Math.Min</c> answers the second on a tie).</summary>
        internal static double MinJs(double a, double b)
        {
            if (double.IsNaN(a) || double.IsNaN(b)) return double.NaN;
            if (a < b) return a;
            if (b < a) return b;
            return a == 0 && BitConverter.DoubleToInt64Bits(a) < 0 ? a : b;
        }

        /// <summary>JavaScript's <c>Math.max</c> of two: NaN if either is, and +0 above -0.</summary>
        internal static double MaxJs(double a, double b)
        {
            if (double.IsNaN(a) || double.IsNaN(b)) return double.NaN;
            if (a > b) return a;
            if (b > a) return b;
            return a == 0 && BitConverter.DoubleToInt64Bits(a) >= 0 ? a : b;
        }
    }
}
