// The by-links-3d layout: a whole world in three dimensions by its links, with the world's Maps and Pins as a plate its
// pinned elements stand on, and points an earlier answer gave held still. Written from the default layouts page
// (schema-dist profiles/layout.md, `by-links-3d`, serial 137), step by step in the page's order and numbering, and
// checked against its fixture (profiles/layout-by-links-3d.fixture.json, vendored as
// Tests/Editor/Fixtures/layout/layout-by-links-3d.fixture.json): OWLayoutByLinks3dTests. The TypeScript SDK's
// layoutByLinks3d and the Python package's layout_links_3d are the two other readers of the same page.
//
// Every number is a double and every expression is worked as the page writes it, left to right, nothing regrouped or
// fused. Steps 1 to 8 use + - * /, sqrt, floor, min and max, and the generator's whole numbers; step 9 a cosine and a
// sine, and step 11 an arcsine, a cosine and a sine, which is why the points they place are held to 6e-8 and every
// other point to the last bit. The directions table is generated data (OWLayout.Directions.cs), never computed.

using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary>One element placed by <c>by-links-3d</c>: its point and how it got there.</summary>
    public sealed class OWLayout3dPoint
    {
        internal OWLayout3dPoint(string id, double x, double y, double z, string how, string group, string part, bool held)
        {
            Id = id;
            X = x;
            Y = y;
            Z = z;
            How = how;
            Group = group;
            Part = part;
            Held = held;
        }

        /// <summary>The element's id.</summary>
        public string Id { get; }

        /// <summary>Its x, in units where a link at rest is <see cref="OWLayout.ByLinks3dRest"/> long.</summary>
        public double X { get; }

        /// <summary>Its y.</summary>
        public double Y { get; }

        /// <summary>Its z.</summary>
        public double Z { get; }

        /// <summary>
        /// How it was placed: <c>centre</c> (a group's centre), <c>with</c> (in a centre's group), <c>arm</c> (hanging off
        /// the body, more than one link out or with links of its own), <c>link</c> (its only link is to the body) or
        /// <c>map</c> (at its Pin on the world's map).
        /// </summary>
        public string How { get; }

        /// <summary>The id of the centre it stands with (a centre's own id, a pinned centre's too), or null.</summary>
        public string Group { get; }

        /// <summary>Null where <see cref="Group"/> is an id; else <c>map</c> (pinned) or <c>one-link</c> (<c>arm</c> or <c>link</c>).</summary>
        public string Part { get; }

        /// <summary>True when it stands at its held point (a held element that is not pinned).</summary>
        public bool Held { get; }
    }

    /// <summary>What <see cref="OWLayout.LayoutByLinks3d(JObject, IEnumerable{JToken}, JToken, JToken)"/> answers.</summary>
    public sealed class OWLayoutByLinks3d
    {
        internal OWLayoutByLinks3d(IReadOnlyList<OWLayout3dPoint> points, IReadOnlyList<string> centres, IReadOnlyList<string> unplaced)
        {
            Points = points;
            Centres = centres;
            Unplaced = unplaced;
        }

        /// <summary><c>by-links-3d</c>.</summary>
        public string Method => "by-links-3d";

        /// <summary>
        /// The version of the method's answers, the TypeScript SDK's <c>BY_LINKS_3D_VERSION</c>: raised whenever an answer
        /// to a case of the method's fixture moves, so a tool that keeps a picture to hand back as held can tell it from
        /// one the method no longer gives.
        /// </summary>
        public int Version => 2;

        /// <summary>One row per placed element, in the order the nodes were handed in.</summary>
        public IReadOnlyList<OWLayout3dPoint> Points { get; }

        /// <summary>The main shape's centres in rank order, then each other piece's, the pieces by size (most nodes first; among equals, the one holding the lowest id).</summary>
        public IReadOnlyList<string> Centres { get; }

        /// <summary>The elements linked to no other and pinned on no map, which the method does not place, by id in code point order.</summary>
        public IReadOnlyList<string> Unplaced { get; }
    }

    /// <summary>A <c>by-links-3d</c> call refused, with the page's code: <c>held-not-object</c>, <c>held-not-point</c>, <c>held-unit</c> or <c>held-box</c>.</summary>
    public sealed class OWLayoutRefusedException : ArgumentException
    {
        internal OWLayoutRefusedException(string code, string message) : base(message) => Code = code;

        /// <summary>The refusal's code, as the layout page names it.</summary>
        public string Code { get; }
    }

    public static partial class OWLayout
    {
        /// <summary>The <c>by-links-3d</c> rest length: a link's length at rest, and the unit of the answer's points.</summary>
        public const double ByLinks3dRest = 60.0;

        private const double Rest = 60.0;
        private const int Steps3d = 300;
        private const int MaxCentres = 12;
        private const int MinGroup = 3;
        private const double WaysCap = 4503599627370496.0;
        private const double Spread = 2;
        private const double Tile = 0.4;
        private const double Lobe = 1.1;
        private const double Gap = 1.25;
        private const double Clear = 1.8;
        private const int PackSteps = 600;
        private const double Cross = 0.25;
        private const double PlateWeight = 0.02;
        private const double Hold = 0.12;
        private const double Cap0 = 0.1;
        private const double Cutoff = 3;
        private const double Line = Rest / 8;
        private const double Snap = Rest / 1000000000;
        private const double HeldBox = 768;
        private const int FrameAgree = 3;
        private const double Fit = 0.000001;
        private static readonly double Gold = Math.PI * (3 - Math.Sqrt(5));
        private static readonly double RingGap = Clear * Rest;
        private static readonly string[] Opposing = { "rivals", "adversaries", "rival", "antagonist" };

        private const int HowCentre = 0, HowWith = 1, HowArm = 2, HowLink = 3, HowOff = 4, HowMap = 5;
        private static readonly string[] HowWords = { "centre", "with", "arm", "link", "off", "map" };

        /// <summary>Lay out a whole graph in three dimensions by its links: <see cref="LayoutByLinks3d(JObject, IEnumerable{JToken}, JToken, JToken)"/> on the graph's own words (<see cref="OWWorldGraph.ToJson"/>).</summary>
        /// <exception cref="OWLayoutRefusedException">A refusal the page names, with its code.</exception>
        /// <exception cref="ArgumentException">A graph that is not <c>{nodes, edges}</c>, or two nodes with one id.</exception>
        public static OWLayoutByLinks3d LayoutByLinks3d(OWWorldGraph graph, IEnumerable<JToken> elements = null, JToken held = null, JToken heldUnit = null)
            => LayoutByLinks3d(graph?.ToJson(), elements, held, heldUnit);

        /// <summary>
        /// Lay out a whole graph in three dimensions by its links (the default layouts page, <c>by-links-3d</c>). The
        /// largest part of the graph that no single element's removal parts (its body) is cut into groups round its most
        /// linked elements, the groups are packed apart in directions read from a table, and 300 seeded force steps
        /// settle each group round its point; what hangs off the body fans out from it. When the world has a Map that
        /// holds three or more of the graph's elements by a Pin, the one that holds the most (a tie to the greater id) is
        /// a flat plate with each of its pinned elements at its Pin, and a Map over a place on it a small plate above it.
        /// Every other piece of linked elements is laid out the same way on its own: one with a pinned or held element
        /// stands where those points put it, the rest on a ring beside the main shape. An element linked to no other and
        /// pinned on no map is not placed, and is listed in <see cref="OWLayoutByLinks3d.Unplaced"/>.
        /// </summary>
        /// <remarks>
        /// <paramref name="graph"/> is <c>{nodes, edges}</c> as <see cref="OWGraph"/>'s world graph answers it, of any
        /// types, in any order; leave Map, Pin and Marker out of it. <paramref name="elements"/> is the world's elements as
        /// served (only Maps and Pins are read, through <see cref="OWMapSpace.Of"/> and <see cref="OWMapSpace.LayoutOf"/>,
        /// and a layout map is never the plate), or null for none. <paramref name="held"/>: points an earlier answer gave,
        /// an object of element id to <c>[x, y, z]</c>, so those elements stand where they stood and the rest is laid out
        /// round them; <paramref name="heldUnit"/> the step they were rounded to (whole numbers: 1), from 0 to an eighth of
        /// a rest. Null for either is absent. The same input gives the same points in every runtime: to the last bit for
        /// every element placed <c>centre</c>, <c>with</c> or <c>map</c> off the ring, and within 6e-8 for the rest.
        /// </remarks>
        /// <exception cref="OWLayoutRefusedException">
        /// Before anything runs: <paramref name="held"/> not an object (<c>held-not-object</c>); a held value that is not
        /// exactly three finite numbers (<c>held-not-point</c>); a <paramref name="heldUnit"/> that is not a finite number
        /// from 0 to an eighth of a rest (<c>held-unit</c>). As each piece is laid out, the main one first: the held points
        /// it holds wider apart than 768 rests on an axis (<c>held-box</c>).
        /// </exception>
        /// <exception cref="ArgumentException">A graph that is not <c>{nodes, edges}</c>, a node without a text id, or two nodes with one id.</exception>
        public static OWLayoutByLinks3d LayoutByLinks3d(JObject graph, IEnumerable<JToken> elements = null, JToken held = null, JToken heldUnit = null)
        {
            if (graph == null || !(graph["nodes"] is JArray givenNodes) || !(graph["edges"] is JArray givenEdges))
                throw new ArgumentException("LayoutByLinks3d: the graph is { nodes, edges }, as WorldGraph answers it");
            var heldIn = ReadHeld(held);
            var unit = ReadUnit(heldUnit);

            var handed = new List<string>(givenNodes.Count);
            for (var i = 0; i < givenNodes.Count; i++)
            {
                var id = (givenNodes[i] as JObject)?["id"];
                if (id == null || id.Type != JTokenType.String) throw new ArgumentException($"LayoutByLinks3d: node {i} has no id");
                handed.Add((string)id);
            }

            var edges = new List<Link3>(givenEdges.Count);
            foreach (var t in givenEdges)
            {
                if (!(t is JObject e)) continue;
                var s = e["source"];
                var g = e["target"];
                if (s == null || s.Type != JTokenType.String || g == null || g.Type != JTokenType.String) continue;
                var f = e["field"];
                edges.Add(new Link3 { Source = (string)s, Target = (string)g, Field = f != null && f.Type == JTokenType.String ? (string)f : null });
            }

            return Pieces(handed, edges, SpaceOf(elements), heldIn, unit);
        }

        // ---- What is read besides the graph ------------------------------------------------------------------------

        private sealed class Link3
        {
            public string Source;
            public string Target;
            public string Field;
        }

        private sealed class SpaceMap
        {
            public string Id;
            public long? Width;
            public long? Height;
            public string Location;
        }

        private sealed class SpacePin
        {
            public string Pin;
            public string Map;
            public string ElementId;
            public double X;
            public double Y;
        }

        private sealed class Space
        {
            public List<SpaceMap> Maps;
            public List<SpacePin> Pins;
        }

        /// <summary>The world's Maps and Pins as the map-space reading answers them, a layout map left out with its Pins (its rule 11); null for no elements.</summary>
        private static Space SpaceOf(IEnumerable<JToken> elements)
        {
            if (elements == null) return null;
            var list = new List<JToken>(elements);
            var space = OWMapSpace.Of(list);
            var rawMaps = new Dictionary<string, JObject>(StringComparer.Ordinal);
            var rawPins = new Dictionary<string, JObject>(StringComparer.Ordinal);
            var mapOrder = new List<JObject>();
            var pinOrder = new List<JObject>();
            foreach (var t in list)
            {
                if (!(t is JObject e)) continue;
                var type = e["type"];
                var id = e["id"];
                if (type == null || type.Type != JTokenType.String || id == null || id.Type != JTokenType.String || (string)id == string.Empty) continue;
                if ((string)type == "map") rawMaps[(string)id] = e;
                else if ((string)type == "pin") rawPins[(string)id] = e;
            }

            mapOrder.AddRange(rawMaps.Values);
            pinOrder.AddRange(rawPins.Values);
            var answer = new Space { Maps = new List<SpaceMap>(), Pins = new List<SpacePin>() };
            foreach (var m in space.Maps)
            {
                if (OWMapSpace.LayoutOf(rawMaps[m.Id], mapOrder, pinOrder).IsLayoutMap) continue;
                answer.Maps.Add(new SpaceMap { Id = m.Id, Width = m.Width, Height = m.Height, Location = m.Location });
            }

            foreach (var p in space.Pins)
                answer.Pins.Add(new SpacePin { Pin = p.Pin, Map = p.Map, ElementId = p.ElementId, X = p.Point.X, Y = p.Point.Y });
            return answer;
        }

        // ---- Held points: the refusals before anything runs --------------------------------------------------------

        private static List<KeyValuePair<string, double[]>> ReadHeld(JToken held)
        {
            if (held == null || held.Type == JTokenType.Null) return null;
            if (!(held is JObject map))
                throw new OWLayoutRefusedException("held-not-object", "LayoutByLinks3d: held is not a mapping of ids to points");
            var entries = new List<KeyValuePair<string, double[]>>();
            foreach (var p in map.Properties())
            {
                if (!(p.Value is JArray a) || a.Count != 3)
                    throw new OWLayoutRefusedException("held-not-point", $"LayoutByLinks3d: held[{p.Name}] is not three finite numbers");
                var q = new double[3];
                for (var i = 0; i < 3; i++)
                {
                    if (a[i].Type != JTokenType.Integer && a[i].Type != JTokenType.Float)
                        throw new OWLayoutRefusedException("held-not-point", $"LayoutByLinks3d: held[{p.Name}] is not three finite numbers");
                    q[i] = (double)a[i];
                    if (double.IsNaN(q[i]) || double.IsInfinity(q[i]))
                        throw new OWLayoutRefusedException("held-not-point", $"LayoutByLinks3d: held[{p.Name}] is not three finite numbers");
                }

                entries.Add(new KeyValuePair<string, double[]>(p.Name, q));
            }

            return entries;
        }

        private static double ReadUnit(JToken unit)
        {
            if (unit == null || unit.Type == JTokenType.Null) return 0;
            if (unit.Type == JTokenType.Integer || unit.Type == JTokenType.Float)
            {
                var u = (double)unit;
                if (!double.IsNaN(u) && !double.IsInfinity(u) && u >= 0 && u <= Line) return u;
            }

            throw new OWLayoutRefusedException("held-unit", $"LayoutByLinks3d: heldUnit {unit.ToString(Newtonsoft.Json.Formatting.None)} is not a finite number from 0 to {Line}");
        }

        /// <summary>The box test of the held points one run holds (Held points, Refused).</summary>
        private static void TestBox(List<KeyValuePair<string, double[]>> held)
        {
            if (held == null || held.Count == 0) return;
            for (var a = 0; a < 3; a++)
            {
                double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
                foreach (var p in held)
                {
                    if (p.Value[a] < lo) lo = p.Value[a];
                    if (p.Value[a] > hi) hi = p.Value[a];
                }

                if (hi - lo > HeldBox * Rest)
                    throw new OWLayoutRefusedException("held-box", $"LayoutByLinks3d: the held points' box is {(hi - lo) / Rest} rests on {"xyz"[a]}, wider than {HeldBox}");
            }
        }

        // ---- Steps 10 to 12: the pieces, the ring and the answer ---------------------------------------------------

        private sealed class RunAnswer
        {
            public List<string> Ids;
            public double[] Points;
            public int[] How;
            public string[] Group;
            public string[] Part;
            public bool[] Held;
            public List<string> Centres;
            public Dictionary<string, int> Row;
        }

        private static OWLayoutByLinks3d Pieces(List<string> handed, List<Link3> edges, Space space, List<KeyValuePair<string, double[]>> held, double unit)
        {
            var n = handed.Count;
            if (n == 0) return new OWLayoutByLinks3d(new OWLayout3dPoint[0], new string[0], new string[0]);
            var ids = new List<string>(handed);
            ids.Sort(OWCodePointComparer.Instance);
            for (var i = 1; i < n; i++)
                if (ids[i] == ids[i - 1]) throw new ArgumentException($"LayoutByLinks3d: two nodes share the id {ids[i]}");
            var at = new Dictionary<string, int>(n, StringComparer.Ordinal);
            for (var i = 0; i < n; i++) at[ids[i]] = i;

            // The pieces: a path of pairs joins two nodes of one piece; the main piece the largest, a tie to the lowest node.
            var parent = new int[n];
            for (var i = 0; i < n; i++) parent[i] = i;
            int Find(int a)
            {
                while (parent[a] != a)
                {
                    parent[a] = parent[parent[a]];
                    a = parent[a];
                }

                return a;
            }

            foreach (var e in edges)
            {
                if (!at.TryGetValue(e.Source, out var a) || !at.TryGetValue(e.Target, out var b) || a == b) continue;
                int ra = Find(a), rb = Find(b);
                if (ra != rb)
                {
                    if (ra < rb) parent[rb] = ra;
                    else parent[ra] = rb;
                }
            }

            var pieceOf = new int[n];
            var rootPiece = new int[n];
            for (var i = 0; i < n; i++) rootPiece[i] = -1;
            var pieces = new List<List<int>>();
            for (var v = 0; v < n; v++)
            {
                var r = Find(v);
                if (rootPiece[r] == -1)
                {
                    rootPiece[r] = pieces.Count;
                    pieces.Add(new List<int>());
                }

                pieceOf[v] = rootPiece[r];
                pieces[pieceOf[v]].Add(v);
            }

            int mainP = -1, mainSize = 0;
            for (var p = 0; p < pieces.Count; p++)
            {
                if (pieces[p].Count <= mainSize) continue;
                mainSize = pieces[p].Count;
                mainP = p;
            }

            // The main run holds the held points of the main piece's nodes (when it holds a pair) and of every pinned node.
            var heldAll = new Dictionary<string, double[]>(StringComparer.Ordinal);
            if (held != null)
                foreach (var p in held)
                    heldAll[p.Key] = p.Value;
            var onMap = new HashSet<string>(StringComparer.Ordinal);
            if (space != null)
            {
                var pa = PlateOf(space, at, null);
                if (pa != null) onMap.UnionWith(pa.Order);
            }

            List<KeyValuePair<string, double[]>> mainHeld = null;
            if (held != null)
            {
                mainHeld = new List<KeyValuePair<string, double[]>>();
                foreach (var p in held)
                    if (at.TryGetValue(p.Key, out var v) && ((mainSize >= 2 && pieceOf[v] == mainP) || onMap.Contains(p.Key)))
                        mainHeld.Add(p);
            }

            var main = Run(handed, edges, space, mainHeld, unit);

            var pinned = new bool[n];
            var keep = new bool[n];
            for (var v = 0; v < n; v++)
            {
                if (main.How[main.Row[ids[v]]] == HowMap) pinned[v] = true;
                if ((mainSize >= 2 && pieceOf[v] == mainP) || pinned[v]) keep[v] = true;
            }

            // Step 11's order: every other piece that holds a pair, most nodes first, a tie to the lower least node.
            var others = new List<int>();
            for (var p = 0; p < pieces.Count; p++)
                if (p != mainP && pieces[p].Count >= 2) others.Add(p);
            others.Sort((p, q) => pieces[q].Count != pieces[p].Count ? pieces[q].Count - pieces[p].Count : pieces[p][0] - pieces[q][0]);

            // A piece's run: its nodes and its edges in the order handed in, no Maps, its pinned nodes held at their main-run points.
            var runs = new Dictionary<int, RunAnswer>();
            var moved = new Dictionary<int, bool>();
            foreach (var p in others)
            {
                var inP = new HashSet<string>(StringComparer.Ordinal);
                foreach (var v in pieces[p]) inP.Add(ids[v]);
                var pnodes = handed.FindAll(inP.Contains);
                var pedges = edges.FindAll(e => inP.Contains(e.Source) && inP.Contains(e.Target));
                var sub = new List<KeyValuePair<string, double[]>>();
                foreach (var v in pieces[p])
                {
                    if (pinned[v])
                    {
                        var m = main.Row[ids[v]];
                        sub.Add(new KeyValuePair<string, double[]>(ids[v], new[] { main.Points[3 * m], main.Points[3 * m + 1], main.Points[3 * m + 2] }));
                    }
                    else if (heldAll.TryGetValue(ids[v], out var q)) sub.Add(new KeyValuePair<string, double[]>(ids[v], q));
                }

                runs[p] = Run(pnodes, pedges, null, sub.Count > 0 ? sub : null, unit);
                moved[p] = sub.Count == 0;
            }

            // Step 11. The main shape: M over the nodes taken from the main run, in the one order; Rmain over those and
            // every node of a piece that is not moved.
            int cnt = 0;
            double sx = 0, sy = 0, sz = 0;
            for (var v = 0; v < n; v++)
            {
                if (!keep[v]) continue;
                var m = main.Row[ids[v]];
                sx += main.Points[3 * m];
                sy += main.Points[3 * m + 1];
                sz += main.Points[3 * m + 2];
                cnt++;
            }

            var M = cnt > 0 ? new[] { sx / cnt, sy / cnt, sz / cnt } : new double[] { 0, 0, 0 };
            double rMain = 0;
            void Reach3(double x, double y, double z)
            {
                double dx = x - M[0], dy = y - M[1], dz = z - M[2];
                var d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (d > rMain) rMain = d;
            }

            for (var v = 0; v < n; v++)
            {
                if (!keep[v]) continue;
                var m = main.Row[ids[v]];
                Reach3(main.Points[3 * m], main.Points[3 * m + 1], main.Points[3 * m + 2]);
            }

            foreach (var p in others)
            {
                if (moved[p]) continue;
                var u = runs[p];
                foreach (var v in pieces[p])
                {
                    if (pinned[v]) continue;
                    var j = u.Row[ids[v]];
                    Reach3(u.Points[3 * j], u.Points[3 * j + 1], u.Points[3 * j + 2]);
                }
            }

            var ringList = others.FindAll(p => moved[p]);
            var c = new Dictionary<int, double[]>();
            var rp = new Dictionary<int, double>();
            foreach (var p in ringList)
            {
                var u = runs[p];
                double qx = 0, qy = 0, qz = 0;
                foreach (var v in pieces[p])
                {
                    var i = u.Row[ids[v]];
                    qx += u.Points[3 * i];
                    qy += u.Points[3 * i + 1];
                    qz += u.Points[3 * i + 2];
                }

                var k = pieces[p].Count;
                var cp = new[] { qx / k, qy / k, qz / k };
                double r = 0;
                foreach (var v in pieces[p])
                {
                    var i = u.Row[ids[v]];
                    double dx = u.Points[3 * i] - cp[0], dy = u.Points[3 * i + 1] - cp[1], dz = u.Points[3 * i + 2] - cp[2];
                    var d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (d > r) r = d;
                }

                c[p] = cp;
                rp[p] = r;
            }

            double rmax = 0;
            foreach (var p in ringList)
                if (rp[p] > rmax) rmax = rp[p];
            var shift = new Dictionary<int, double[]>();
            var fitEdge = 2 * Math.PI - Fit;
            var ringR = rMain + RingGap + rmax;
            var on = new List<KeyValuePair<int, double>>();
            double sum = 0;
            void Close()
            {
                var share = (2 * Math.PI - sum) / on.Count;
                double a = 0;
                foreach (var q in on)
                {
                    var phi = a + q.Value / 2;
                    a = a + (q.Value + share);
                    var cp = c[q.Key];
                    shift[q.Key] = new[] { M[0] + ringR * Math.Cos(phi) - cp[0], M[1] + ringR * Math.Sin(phi) - cp[1], M[2] - cp[2] };
                }
            }

            foreach (var p in ringList)
            {
                var th = 2 * Math.Asin((rp[p] + RingGap / 2) / ringR);
                if (on.Count > 0 && !(sum + th <= fitEdge))
                {
                    Close();
                    ringR = ringR + 2 * rmax + RingGap;
                    on.Clear();
                    sum = 0;
                    th = 2 * Math.Asin((rp[p] + RingGap / 2) / ringR);
                }

                on.Add(new KeyValuePair<int, double>(p, th));
                sum = sum + th;
            }

            if (on.Count > 0) Close();

            // Step 12. The answer, in the order the nodes were handed in.
            var rows = new List<OWLayout3dPoint>();
            var unplaced = new List<int>();
            var placed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in handed)
            {
                var v = at[id];
                if (keep[v])
                {
                    var m = main.Row[id];
                    var group = main.Group[m];
                    var part = main.Part[m];
                    if (pinned[v] && pieceOf[v] != mainP && runs.TryGetValue(pieceOf[v], out var pr) && pr.Centres.Contains(id))
                    {
                        group = id;
                        part = null;
                    }

                    rows.Add(new OWLayout3dPoint(id, main.Points[3 * m], main.Points[3 * m + 1], main.Points[3 * m + 2], HowWords[main.How[m]], group, part, main.Held[m]));
                    placed.Add(id);
                    continue;
                }

                var p = pieceOf[v];
                if (pieces[p].Count < 2)
                {
                    unplaced.Add(v);
                    continue;
                }

                var u = runs[p];
                var j = u.Row[id];
                double x = u.Points[3 * j], y = u.Points[3 * j + 1], z = u.Points[3 * j + 2];
                if (shift.TryGetValue(p, out var T))
                {
                    x = x + T[0];
                    y = y + T[1];
                    z = z + T[2];
                }

                rows.Add(new OWLayout3dPoint(id, x, y, z, HowWords[u.How[j]], u.Group[j], u.Part[j], u.Held[j]));
                placed.Add(id);
            }

            unplaced.Sort();
            var centres = main.Centres.FindAll(placed.Contains);
            foreach (var p in others) centres.AddRange(runs[p].Centres);
            return new OWLayoutByLinks3d(rows, centres, unplaced.ConvertAll(v => ids[v]));
        }

        // ---- Steps 1 to 9 and the held points' rules: one run ------------------------------------------------------

        private static RunAnswer Run(List<string> handed, List<Link3> edgesIn, Space space, List<KeyValuePair<string, double[]>> heldIn, double unit)
        {
            TestBox(heldIn);
            var n = handed.Count;

            // 1. The one order, and the pairs.
            var ids = new List<string>(handed);
            ids.Sort(OWCodePointComparer.Instance);
            var at = new Dictionary<string, int>(n, StringComparer.Ordinal);
            for (var i = 0; i < n; i++) at[ids[i]] = i;
            var links = new List<int[]>();
            var opp = new List<int[]>();
            var seen = new HashSet<long>();
            var pairs = new List<int[]>();
            foreach (var e in edgesIn)
            {
                if (!at.TryGetValue(e.Source, out var a) || !at.TryGetValue(e.Target, out var b)) continue;
                if (e.Field != null && Array.IndexOf(Opposing, e.Field) >= 0) opp.Add(new[] { a, b });
                if (a == b) continue;
                int lo = a < b ? a : b, hi = a < b ? b : a;
                if (seen.Add((long)lo * n + hi)) pairs.Add(new[] { lo, hi });
            }

            pairs.Sort((p, q) => p[0] != q[0] ? p[0] - q[0] : p[1] - q[1]);
            var adj = new List<int>[n];
            for (var i = 0; i < n; i++) adj[i] = new List<int>();
            foreach (var p in pairs)
            {
                adj[p[0]].Add(p[1]);
                adj[p[1]].Add(p[0]);
            }

            foreach (var l in adj) l.Sort();
            var degree = new int[n];
            for (var i = 0; i < n; i++) degree[i] = adj[i].Count;

            // 2. The body. 3. The centres and their groups.
            Body(n, adj, out var body, out var hangCut, out var hangDepth, out var off);
            var taken = Centres(n, adj, body, out var group);
            var k = taken.Length;

            // The held points that are nodes (a held id that is no node is passed over).
            bool[] hflag = null;
            double[] hat = null;
            if (heldIn != null)
                foreach (var p in heldIn)
                {
                    if (!at.TryGetValue(p.Key, out var v)) continue;
                    if (hflag == null)
                    {
                        hflag = new bool[n];
                        hat = new double[3 * n];
                    }

                    hflag[v] = true;
                    hat[3 * v] = p.Value[0];
                    hat[3 * v + 1] = p.Value[1];
                    hat[3 * v + 2] = p.Value[2];
                }

            // 4. The plate; with held points, H1's frame, and the plate again at the frame's scale.
            PlateAnswer plate = null;
            double[] frameP = null;
            if (space != null)
            {
                plate = PlateOf(space, at, null);
                if (plate != null && hflag != null && FrameOf(plate, at, hflag, hat, unit, out var s, out frameP))
                    plate = PlateOf(space, at, s);
            }

            // H2: each group's case; H3's fixed parts and H4's snap for step 5; H5's kept nodes.
            bool[] keep = null, free = null, gflag = null;
            double[] gat = null;
            var extra = new List<int>();
            if (hflag != null)
            {
                var pinnedV = new bool[n];
                if (plate != null)
                    foreach (var id in plate.Order)
                        pinnedV[at[id]] = true;
                keep = new bool[n];
                for (var v = 0; v < n; v++) keep[v] = hflag[v] && !pinnedV[v];
                gflag = new bool[k];
                gat = new double[3 * k];
                free = new bool[k];
                var gs = new double[3 * k];
                var gc = new int[k];
                for (var v = 0; v < n; v++)
                {
                    if (!body[v] || !keep[v]) continue;
                    var g = group[v];
                    gs[3 * g] += hat[3 * v];
                    gs[3 * g + 1] += hat[3 * v + 1];
                    gs[3 * g + 2] += hat[3 * v + 2];
                    gc[g]++;
                }

                for (var g = 0; g < k; g++)
                {
                    var t = taken[g];
                    if (pinnedV[t]) continue;
                    if (keep[t])
                    {
                        gflag[g] = true;
                        gat[3 * g] = hat[3 * t];
                        gat[3 * g + 1] = hat[3 * t + 1];
                        gat[3 * g + 2] = hat[3 * t + 2];
                    }
                    else if (gc[g] > 0)
                    {
                        gflag[g] = true;
                        free[g] = true;
                        gat[3 * g] = gs[3 * g] / gc[g];
                        gat[3 * g + 1] = gs[3 * g + 1] / gc[g];
                        gat[3 * g + 2] = gs[3 * g + 2] / gc[g];
                    }
                    else free[g] = true;
                }

                for (var v = 0; v < n; v++)
                    if (keep[v] && !body[v]) extra.Add(v);
            }

            // 5. Where each group's point stands.
            var pts = GroupPoints(n, body, taken, group, pairs, opp, plate, at, gflag, gat, frameP, hflag, hat, 3 * unit + Snap);

            // 6 to 8. The feed, the start, the steps.
            var relaxed = ForceSteps(ids, body, pairs, taken, group, pts, keep, hat, free, extra);

            // 9. What is placed after the steps.
            var how = Hang(n, body, hangCut, hangDepth, off, taken, group, pts, degree, relaxed, keep, hat, out var pos);

            // The run's answer, in the order handed in.
            var answer = new RunAnswer
            {
                Ids = handed,
                Points = new double[3 * n],
                How = new int[n],
                Group = new string[n],
                Part = new string[n],
                Held = new bool[n],
                Centres = new List<string>(),
                Row = new Dictionary<string, int>(n, StringComparer.Ordinal),
            };
            for (var i = 0; i < n; i++)
            {
                var v = at[handed[i]];
                answer.Row[handed[i]] = i;
                answer.Points[3 * i] = pos[3 * v];
                answer.Points[3 * i + 1] = pos[3 * v + 1];
                answer.Points[3 * i + 2] = pos[3 * v + 2];
                answer.Held[i] = keep != null && keep[v];
                var w = how[v];
                answer.How[i] = w;
                var own = body[v] && taken[group[v]] == v;
                if (w == HowCentre || w == HowWith || (w == HowMap && own)) answer.Group[i] = ids[taken[group[v]]];
                else if (w == HowMap) answer.Part[i] = "map";
                else if (w == HowArm || w == HowLink) answer.Part[i] = "one-link";
            }

            foreach (var t in taken) answer.Centres.Add(ids[t]);
            return answer;
        }

        // ---- Step 2: the body ----------------------------------------------------------------------------------------

        private static void Body(int n, List<int>[] adj, out bool[] body, out int[] hangCut, out int[] hangDepth, out bool[] off)
        {
            // The pieces; the main piece the largest, a tie to the one that holds the lowest node.
            var piece = new int[n];
            for (var i = 0; i < n; i++) piece[i] = -1;
            var queue = new int[n];
            int main = -1, mainSize = 0, pieces = 0;
            for (var s = 0; s < n; s++)
            {
                if (piece[s] != -1) continue;
                int head = 0, tail = 0;
                queue[tail++] = s;
                piece[s] = pieces;
                while (head < tail)
                {
                    var v = queue[head++];
                    foreach (var w in adj[v])
                    {
                        if (piece[w] != -1) continue;
                        piece[w] = pieces;
                        queue[tail++] = w;
                    }
                }

                if (tail > mainSize)
                {
                    mainSize = tail;
                    main = pieces;
                }

                pieces++;
            }

            // The blocks (Hopcroft and Tarjan, without recursion); the body is the block with the most members, a tie
            // to the one whose members in rising order are the higher at the first place they differ.
            body = new bool[n];
            if (mainSize == 1)
            {
                for (var i = 0; i < n; i++)
                    if (piece[i] == main) body[i] = true;
            }
            else if (mainSize > 1)
            {
                var disc = new int[n];
                var low = new int[n];
                var parent = new int[n];
                var next = new int[n];
                var stamp = new int[n];
                for (var i = 0; i < n; i++)
                {
                    disc[i] = -1;
                    parent[i] = -1;
                    stamp[i] = -1;
                }

                var stackV = new int[n];
                var edgeA = new List<int>();
                var edgeB = new List<int>();
                var best = new List<int>();
                int blocks = 0, clock = 0, top = 0;
                var root = 0;
                while (piece[root] != main) root++;
                disc[root] = low[root] = clock++;
                stackV[top++] = root;
                while (top > 0)
                {
                    var v = stackV[top - 1];
                    if (next[v] < adj[v].Count)
                    {
                        var w = adj[v][next[v]++];
                        if (disc[w] == -1)
                        {
                            parent[w] = v;
                            disc[w] = low[w] = clock++;
                            edgeA.Add(v);
                            edgeB.Add(w);
                            stackV[top++] = w;
                        }
                        else if (w != parent[v] && disc[w] < disc[v])
                        {
                            edgeA.Add(v);
                            edgeB.Add(w);
                            if (disc[w] < low[v]) low[v] = disc[w];
                        }
                    }
                    else
                    {
                        top--;
                        var u = parent[v];
                        if (u == -1) continue;
                        if (low[v] < low[u]) low[u] = low[v];
                        if (low[v] < disc[u]) continue;
                        var members = new List<int>();
                        for (;;)
                        {
                            var last = edgeA.Count - 1;
                            int a = edgeA[last], b = edgeB[last];
                            edgeA.RemoveAt(last);
                            edgeB.RemoveAt(last);
                            if (stamp[a] != blocks)
                            {
                                stamp[a] = blocks;
                                members.Add(a);
                            }

                            if (stamp[b] != blocks)
                            {
                                stamp[b] = blocks;
                                members.Add(b);
                            }

                            if (a == u && b == v) break;
                        }

                        if (members.Count >= best.Count)
                        {
                            members.Sort();
                            var wins = members.Count > best.Count;
                            if (!wins)
                                for (var i = 0; i < members.Count; i++)
                                    if (members[i] != best[i])
                                    {
                                        wins = members[i] > best[i];
                                        break;
                                    }

                            if (wins) best = members;
                        }

                        blocks++;
                    }
                }

                foreach (var v in best) body[v] = true;
            }

            // What hangs: outward from the whole body at once, each node's cut and its depth from it.
            hangCut = new int[n];
            hangDepth = new int[n];
            for (var i = 0; i < n; i++) hangCut[i] = -1;
            int qh = 0, qt = 0;
            for (var i = 0; i < n; i++)
                if (body[i]) queue[qt++] = i;
            while (qh < qt)
            {
                var v = queue[qh++];
                foreach (var w in adj[v])
                {
                    if (body[w] || hangCut[w] != -1) continue;
                    hangCut[w] = body[v] ? v : hangCut[v];
                    hangDepth[w] = hangDepth[v] + 1;
                    queue[qt++] = w;
                }
            }

            off = new bool[n];
            for (var i = 0; i < n; i++) off[i] = piece[i] != main;
        }

        // ---- Step 3: the centres and their groups ----------------------------------------------------------------

        private static int[] Centres(int n, List<int>[] adj, bool[] body, out int[] group)
        {
            var members = new List<int>();
            for (var i = 0; i < n; i++)
                if (body[i]) members.Add(i);
            var taken = Pick(n, adj, members, body, out group, out _);
            for (var round = 0; round < 2; round++)
            {
                if (taken.Count == 0) break;
                var sizes = new int[taken.Count];
                foreach (var v in members) sizes[group[v]]++;
                var big = 0;
                for (var r = 1; r < taken.Count; r++)
                    if (sizes[r] > sizes[big]) big = r;
                if (2 * sizes[big] <= members.Count) break;
                var inside = new List<int>();
                var inSet = new bool[n];
                foreach (var v in members)
                    if (group[v] == big && v != taken[big])
                    {
                        inside.Add(v);
                        inSet[v] = true;
                    }

                var sub = Pick(n, adj, inside, inSet, out var subGroup, out var subDist);
                if (sub.Count < 2) break;
                var bas = taken.Count;
                taken.AddRange(sub);
                foreach (var v in inside)
                    if (subDist[v] != -1) group[v] = bas + subGroup[v];
            }

            return taken.ToArray();
        }

        private static List<int> Pick(int n, List<int>[] adj, List<int> members, bool[] inSet, out int[] group, out int[] dist)
        {
            var size = members.Count;
            var count = new int[n];
            foreach (var v in members)
            {
                var c = 0;
                foreach (var w in adj[v])
                    if (inSet[w]) c++;
                count[v] = c;
            }

            var ranked = members.ToArray();
            Array.Sort(ranked, (a, b) => count[b] != count[a] ? count[b] - count[a] : a - b);
            var f = 0;
            while (f * f < size) f++;
            var least = Math.Max(3, f);
            var mark = new int[n];
            var hit = new int[MaxCentres];
            var taken = new List<int>();
            foreach (var v in ranked)
            {
                if (count[v] < least || taken.Count == MaxCentres) break;
                Array.Clear(hit, 0, hit.Length);
                foreach (var w in adj[v])
                {
                    if (!inSet[w]) continue;
                    for (var r = 0; r < taken.Count; r++) hit[r] += (mark[w] >> r) & 1;
                }

                var ok = true;
                for (var r = 0; r < taken.Count; r++)
                    if (2 * hit[r] >= count[v])
                    {
                        ok = false;
                        break;
                    }

                if (!ok) continue;
                var rank = taken.Count;
                taken.Add(v);
                mark[v] |= 1 << rank;
                foreach (var w in adj[v])
                    if (inSet[w]) mark[w] |= 1 << rank;
            }

            if (taken.Count == 0 && ranked.Length > 0) taken.Add(ranked[0]);
            for (;;)
            {
                Reach(n, adj, members, inSet, taken, out group, out dist);
                var sizes = new int[taken.Count];
                foreach (var v in members)
                    if (group[v] != -1) sizes[group[v]]++;
                var last = -1;
                for (var r = 0; r < taken.Count; r++)
                    if (sizes[r] < MinGroup) last = r;
                if (last == -1 || taken.Count == 1) return taken;
                taken.RemoveAt(last);
            }
        }

        private static void Reach(int n, List<int>[] adj, List<int> members, bool[] inSet, List<int> taken, out int[] group, out int[] dist)
        {
            var k = taken.Count;
            dist = new int[n];
            group = new int[n];
            for (var i = 0; i < n; i++)
            {
                dist[i] = -1;
                group[i] = -1;
            }

            var ways = new double[n * k];
            var front = new List<int>();
            for (var r = 0; r < k; r++)
            {
                dist[taken[r]] = 0;
                ways[taken[r] * k + r] = 1;
                front.Add(taken[r]);
            }

            var d = 0;
            while (front.Count > 0)
            {
                d++;
                var next = new List<int>();
                foreach (var v in front)
                    foreach (var w in adj[v])
                    {
                        if (!inSet[w]) continue;
                        if (dist[w] == -1)
                        {
                            dist[w] = d;
                            next.Add(w);
                        }

                        if (dist[w] != d) continue;
                        for (var r = 0; r < k; r++) ways[w * k + r] = Math.Min(WaysCap, ways[w * k + r] + ways[v * k + r]);
                    }

                front = next;
            }

            for (var r = 0; r < k; r++) group[taken[r]] = r;
            foreach (var v in members)
            {
                if (dist[v] <= 0) continue;
                var best = 0;
                for (var r = 1; r < k; r++)
                    if (ways[v * k + r] > ways[v * k + best]) best = r;
                group[v] = best;
            }
        }

        // ---- Step 4: the plate, and H1's frame -------------------------------------------------------------------

        private sealed class PlateAnswer
        {
            /// <summary>The pinned elements in the order they were placed: the first map's, then each small plate's.</summary>
            public List<string> Order = new List<string>();

            public Dictionary<string, double[]> At = new Dictionary<string, double[]>(StringComparer.Ordinal);

            /// <summary>The first map's elements and each one's map place, (x - W / 2, y - H / 2), in the order of <see cref="Order"/>.</summary>
            public List<KeyValuePair<string, double[]>> First = new List<KeyValuePair<string, double[]>>();
        }

        private static bool Sized(long? v) => v.HasValue && v.Value > 0;

        private static PlateAnswer PlateOf(Space space, Dictionary<string, int> nodes, double? scaleHeld)
        {
            var maps = new Dictionary<string, SpaceMap>(StringComparer.Ordinal);
            foreach (var m in space.Maps) maps[m.Id] = m;
            var pins = new List<SpacePin>(space.Pins);
            var sorted = new SpacePin[pins.Count];
            pins.CopyTo(sorted);
            // A stable sort by pin id in code point order.
            var order = new int[sorted.Length];
            for (var i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) =>
            {
                var d = OWCodePointComparer.Instance.Compare(sorted[a].Pin, sorted[b].Pin);
                return d != 0 ? d : a - b;
            });

            var mapOrder = new List<string>();
            var on = new Dictionary<string, List<KeyValuePair<string, double[]>>>(StringComparer.Ordinal);
            foreach (var i in order)
            {
                var pin = sorted[i];
                if (pin.Map == null || !maps.ContainsKey(pin.Map) || pin.ElementId == null || !nodes.ContainsKey(pin.ElementId)) continue;
                if (!on.TryGetValue(pin.Map, out var row))
                {
                    on[pin.Map] = row = new List<KeyValuePair<string, double[]>>();
                    mapOrder.Add(pin.Map);
                }

                if (!row.Exists(x => x.Key == pin.ElementId)) row.Add(new KeyValuePair<string, double[]>(pin.ElementId, new[] { pin.X, pin.Y }));
            }

            if (mapOrder.Count == 0) return null;
            string first = null;
            var best = -1;
            foreach (var c in mapOrder)
            {
                var size = on[c].Count;
                if (size > best || (size == best && OWCodePointComparer.Instance.Compare(c, first) > 0))
                {
                    first = c;
                    best = size;
                }
            }

            var map = maps[first];
            var mine = on[first];
            if (mine.Count < 3 || !Sized(map.Width) || !Sized(map.Height)) return null;
            double W = map.Width.Value, H = map.Height.Value;
            double many = mine.Count;
            var scale = scaleHeld ?? Spread * Rest / Math.Sqrt(W * H / many);
            var answer = new PlateAnswer();
            foreach (var e in mine)
            {
                double mx = e.Value[0] - W / 2, my = e.Value[1] - H / 2;
                answer.First.Add(new KeyValuePair<string, double[]>(e.Key, new[] { mx, my }));
                answer.Order.Add(e.Key);
                answer.At[e.Key] = new[] { mx * scale, my * scale, 0.0 };
            }

            var todo = mapOrder.FindAll(x => x != first);
            todo.Sort(OWCodePointComparer.Instance);
            var takenAny = true;
            while (takenAny)
            {
                takenAny = false;
                foreach (var c in new List<string>(todo))
                {
                    var m = maps[c];
                    var row = on[c];
                    var fresh = row.FindAll(x => !answer.At.ContainsKey(x.Key));
                    if (m.Location == null || !answer.At.TryGetValue(m.Location, out var l) || !Sized(m.Width) || !Sized(m.Height) || row.Count < 3 || fresh.Count == 0)
                        continue;
                    double cw = m.Width.Value, ch = m.Height.Value;
                    double count = row.Count;
                    var sc = Tile * Rest / Math.Sqrt(cw * ch / count);
                    var rise = row.Count >= 20 ? Rest * 4 : Rest * 2;
                    foreach (var e in fresh)
                    {
                        answer.Order.Add(e.Key);
                        answer.At[e.Key] = new[] { l[0] + (e.Value[0] - cw / 2) * sc, l[1] + (e.Value[1] - ch / 2) * sc, l[2] + rise };
                    }

                    todo.Remove(c);
                    takenAny = true;
                }
            }

            return answer;
        }

        /// <summary>H1: the frame, where the plate stood when the held points were answered; false when there is none.</summary>
        private static bool FrameOf(PlateAnswer plate, Dictionary<string, int> at, bool[] hflag, double[] hat, double unit, out double s, out double[] F)
        {
            s = 0;
            F = null;
            var cv = new List<int>();
            var cx = new List<double>();
            var cy = new List<double>();
            foreach (var e in plate.First)
            {
                var v = at[e.Key];
                if (!hflag[v]) continue;
                cv.Add(v);
                cx.Add(e.Value[0]);
                cy.Add(e.Value[1]);
            }

            var order = new int[cv.Count];
            for (var i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (p, q) => cv[p] - cv[q]);
            var m = order.Length;
            var tolZ = unit + Snap;
            var band = 3 * unit + Snap;
            for (var ia = 0; ia < m; ia++)
            {
                var a = order[ia];
                var va = cv[a];
                double ax = hat[3 * va], ay = hat[3 * va + 1], az = hat[3 * va + 2];
                var partners = new List<int>();
                var bdist = new List<double>();
                for (var ib = 0; ib < m; ib++)
                {
                    var c = order[ib];
                    if (c == a) continue;
                    var dz = hat[3 * cv[c] + 2] - az;
                    if (!(dz <= tolZ && -dz <= tolZ)) continue;
                    if (cx[c] == cx[a] && cy[c] == cy[a]) continue;
                    double mx = cx[c] - cx[a], my = cy[c] - cy[a];
                    partners.Add(c);
                    bdist.Add(Math.Sqrt(mx * mx + my * my));
                }

                var byFar = new int[partners.Count];
                for (var i = 0; i < byFar.Length; i++) byFar[i] = i;
                Array.Sort(byFar, (p, q) => bdist[q] != bdist[p] ? (bdist[q] > bdist[p] ? 1 : -1) : p - q);
                foreach (var ib in byFar)
                {
                    var b = partners[ib];
                    var vb = cv[b];
                    double hx = hat[3 * vb] - ax, hy = hat[3 * vb + 1] - ay;
                    if (hx == 0 && hy == 0) continue;
                    var sc = Math.Sqrt(hx * hx + hy * hy) / bdist[ib];
                    var P = new[] { ax - sc * cx[a], ay - sc * cy[a], az };
                    var agree = 0;
                    for (var ic = 0; ic < m; ic++)
                    {
                        var c = order[ic];
                        var vc = cv[c];
                        double ex = hat[3 * vc] - (P[0] + sc * cx[c]), ey = hat[3 * vc + 1] - (P[1] + sc * cy[c]), ez = hat[3 * vc + 2] - P[2];
                        if (ex <= band && -ex <= band && ey <= band && -ey <= band && ez <= band && -ez <= band) agree++;
                    }

                    if (agree < FrameAgree) continue;
                    s = sc;
                    F = P;
                    return true;
                }
            }

            return false;
        }

        // ---- Step 5: where each group's point stands -------------------------------------------------------------

        private sealed class GroupPointsAnswer
        {
            public double[] Cpt;
            public double[] Lobe;
            public bool[] Pinned;
            public double[] PlateAt;
        }

        private static double Dir(int K, int s, int axis) =>
            BitConverter.Int64BitsToDouble(unchecked((long)DirectionBits[3 * K * (K - 1) / 2 + 3 * s + axis]));

        /// <summary>The page's cube root: 0 for v not over 0; else from sqrt(v), or 1 when that is under 1, forty Newton steps.</summary>
        private static double Root(double v)
        {
            if (!(v > 0)) return 0;
            var x = Math.Sqrt(v);
            if (x < 1) x = 1;
            for (var i = 0; i < 40; i++) x = (2 * x + v / (x * x)) / 3;
            return x;
        }

        private static void Counts(int n, int K, int[] part, List<int[]> pairs, List<int[]> opps, out int[] size, out int[] link, out int[] opp)
        {
            size = new int[K];
            link = new int[K * K];
            opp = new int[K * K];
            for (var v = 0; v < n; v++)
                if (part[v] != -1) size[part[v]]++;
            foreach (var pr in pairs)
            {
                int p = part[pr[0]], q = part[pr[1]];
                if (p == -1 || q == -1 || p == q) continue;
                if (p < q) link[p * K + q]++;
                else link[q * K + p]++;
            }

            foreach (var e in opps)
            {
                int p = part[e[0]], q = part[e[1]];
                if (p == -1 || q == -1 || p == q) continue;
                if (p < q) opp[p * K + q]++;
                else opp[q * K + p]++;
            }
        }

        private static int[] BySize(int K, int[] size)
        {
            var order = new int[K];
            for (var i = 0; i < K; i++) order[i] = i;
            Array.Sort(order, (a, b) => size[b] != size[a] ? size[b] - size[a] : a - b);
            return order;
        }

        /// <summary>Each part's direction: the two ends, then the rest by size, each the free direction of least cost.</summary>
        private static double[] Directions(int K, int[] size, int[] link, int[] opp)
        {
            var dirs = new double[3 * K];
            if (K == 1) return dirs;
            if (K > DirectionsMax) throw new InvalidOperationException($"LayoutByLinks3d: the directions table has no row of {K} directions");
            var anyOpp = false;
            for (var i = 0; i < K; i++)
                for (var j = i + 1; j < K; j++)
                    if (opp[i * K + j] > 0) anyOpp = true;
            int b0 = -1, b1 = -1;
            if (anyOpp)
            {
                int bo = -1, bs = -1;
                for (var i = 0; i < K; i++)
                    for (var j = i + 1; j < K; j++)
                    {
                        int o = opp[i * K + j], s = size[i] + size[j];
                        if (o > bo || (o == bo && s > bs))
                        {
                            bo = o;
                            bs = s;
                            b0 = i;
                            b1 = j;
                        }
                    }
            }
            else
            {
                var bySize = BySize(K, size);
                var m = Math.Min(K, 4);
                var big = new int[m];
                Array.Copy(bySize, big, m);
                Array.Sort(big);
                for (var x = 0; x < m; x++)
                    for (var y = x + 1; y < m; y++)
                    {
                        int i = big[x], j = big[y];
                        if (b0 == -1)
                        {
                            b0 = i;
                            b1 = j;
                            continue;
                        }

                        var left = (long)link[i * K + j] * size[b0] * size[b1];
                        var right = (long)link[b0 * K + b1] * size[i] * size[j];
                        if (left < right)
                        {
                            b0 = i;
                            b1 = j;
                        }
                    }
            }

            var place = new int[K];
            for (var i = 0; i < K; i++) place[i] = -1;
            var used = new bool[K];
            var placed = new List<int> { b0, b1 };
            place[b0] = 0;
            used[0] = true;
            place[b1] = K - 1;
            used[K - 1] = true;
            foreach (var g in BySize(K, size))
            {
                if (place[g] != -1) continue;
                int bestS = -1;
                double bestC = 0;
                for (var s = 0; s < K; s++)
                {
                    if (used[s]) continue;
                    double c = 0;
                    foreach (var h in placed)
                    {
                        var t = place[h];
                        int lo = g < h ? g : h, hi = g < h ? h : g;
                        var w = link[lo * K + hi] - 4 * opp[lo * K + hi];
                        double dx = Dir(K, s, 0) - Dir(K, t, 0), dy = Dir(K, s, 1) - Dir(K, t, 1), dz = Dir(K, s, 2) - Dir(K, t, 2);
                        c += w * Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    }

                    if (bestS == -1 || c < bestC)
                    {
                        bestS = s;
                        bestC = c;
                    }
                }

                place[g] = bestS;
                used[bestS] = true;
                placed.Add(g);
            }

            for (var g = 0; g < K; g++)
                for (var a = 0; a < 3; a++)
                    dirs[3 * g + a] = Dir(K, place[g], a);
            return dirs;
        }

        /// <summary>The packing (step 5), and H3's with fixed parts: <paramref name="fixedAt"/> null when no part is fixed.</summary>
        private static double[] Pack(int K, double[] radius, int[] link, int[] opp, double[] dirs, bool[] fixedFlag, double[] fixedAt)
        {
            var P = new double[3 * K];
            var nf = 0;
            if (fixedFlag != null)
                for (var i = 0; i < K; i++)
                    if (fixedFlag[i]) nf++;
            if (nf == 0 && K == 1) return P;
            double sum = 0;
            for (var i = 0; i < K; i++) sum += radius[i];
            if (nf == 0)
            {
                for (var c = 0; c < 3 * K; c++) P[c] = dirs[c] * sum;
            }
            else
            {
                double ox = 0, oy = 0, oz = 0;
                for (var i = 0; i < K; i++)
                {
                    if (!fixedFlag[i]) continue;
                    P[3 * i] = fixedAt[3 * i];
                    P[3 * i + 1] = fixedAt[3 * i + 1];
                    P[3 * i + 2] = fixedAt[3 * i + 2];
                    ox += fixedAt[3 * i];
                    oy += fixedAt[3 * i + 1];
                    oz += fixedAt[3 * i + 2];
                }

                if (nf == K) return P;
                ox /= nf;
                oy /= nf;
                oz /= nf;
                for (var i = 0; i < K; i++)
                {
                    if (fixedFlag[i]) continue;
                    P[3 * i] = ox + dirs[3 * i] * sum;
                    P[3 * i + 1] = oy + dirs[3 * i + 1] * sum;
                    P[3 * i + 2] = oz + dirs[3 * i + 2] * sum;
                }
            }

            double total = 0;
            for (var i = 0; i < K; i++)
                for (var j = i + 1; j < K; j++)
                    total += link[i * K + j];
            if (total == 0) total = 1;
            var D = new double[3 * K];
            for (var step = 0; step < PackSteps; step++)
            {
                Array.Clear(D, 0, D.Length);
                for (var i = 0; i < K; i++)
                    for (var j = i + 1; j < K; j++)
                    {
                        double dx = P[3 * i] - P[3 * j], dy = P[3 * i + 1] - P[3 * j + 1], dz = P[3 * i + 2] - P[3 * j + 2];
                        var dist = Math.Max(Math.Sqrt(dx * dx + dy * dy + dz * dz), 0.000001);
                        var f = dist - Gap * (radius[i] + radius[j]);
                        if (f > 0) f = f * ((0.02 + 0.5 * link[i * K + j] / total) * (opp[i * K + j] > 0 ? 0.25 : 1));
                        double ux = dx / dist * f * 0.5, uy = dy / dist * f * 0.5, uz = dz / dist * f * 0.5;
                        D[3 * i] = D[3 * i] - ux;
                        D[3 * i + 1] = D[3 * i + 1] - uy;
                        D[3 * i + 2] = D[3 * i + 2] - uz;
                        D[3 * j] = D[3 * j] + ux;
                        D[3 * j + 1] = D[3 * j + 1] + uy;
                        D[3 * j + 2] = D[3 * j + 2] + uz;
                    }

                for (var i = 0; i < K; i++)
                {
                    if (nf > 0 && fixedFlag[i]) continue;
                    for (var c = 3 * i; c < 3 * i + 3; c++) P[c] = P[c] + D[c] * 0.5;
                }
            }

            if (nf > 0) return P;
            double mx = 0, my = 0, mz = 0;
            for (var i = 0; i < K; i++)
            {
                mx += P[3 * i];
                my += P[3 * i + 1];
                mz += P[3 * i + 2];
            }

            mx /= K;
            my /= K;
            mz /= K;
            for (var i = 0; i < K; i++)
            {
                P[3 * i] -= mx;
                P[3 * i + 1] -= my;
                P[3 * i + 2] -= mz;
            }

            return P;
        }

        private static GroupPointsAnswer GroupPoints(int n, bool[] body, int[] taken, int[] group, List<int[]> pairs, List<int[]> opps,
            PlateAnswer plate, Dictionary<string, int> at, bool[] gflag, double[] gat, double[] frameP, bool[] hflag, double[] hat, double near)
        {
            var k = taken.Length;
            var part = new int[n];
            for (var v = 0; v < n; v++) part[v] = body[v] ? group[v] : -1;
            Counts(n, k, part, pairs, opps, out var size, out var link, out var opp);
            var lobe = new double[k];
            for (var g = 0; g < k; g++) lobe[g] = Rest * Lobe * Root(size[g]);
            var answer = new GroupPointsAnswer { Lobe = lobe, Pinned = new bool[n] };

            if (plate == null)
            {
                var dirs = Directions(k, size, link, opp);
                answer.Cpt = Pack(k, lobe, link, opp, dirs, gflag, gat);
                return answer;
            }

            var pinned = answer.Pinned;
            var q = new double[3 * n];
            var outside = 0;
            double half = 0;
            foreach (var id in plate.Order)
            {
                var v = at[id];
                var xyz = plate.At[id];
                pinned[v] = true;
                q[3 * v] = xyz[0];
                q[3 * v + 1] = xyz[1];
                q[3 * v + 2] = xyz[2];
                if (!body[v]) outside++;
                half = Math.Max(half, Math.Sqrt(xyz[0] * xyz[0] + xyz[1] * xyz[1]));
            }

            var over = new bool[k];
            var unit = new int[k];
            var pu = 0;
            for (var g = 0; g < k; g++)
            {
                unit[g] = -1;
                if (pinned[taken[g]]) over[g] = true;
                else unit[g] = pu++;
            }

            var K = pu + 1;
            var upart = new int[n];
            for (var v = 0; v < n; v++) upart[v] = !body[v] ? -1 : pinned[v] || over[group[v]] ? pu : unit[group[v]];
            Counts(n, K, upart, pairs, opps, out var usize, out var ulink, out var uopp);
            usize[pu] += outside;
            var udirs = Directions(K, usize, ulink, uopp);
            var radius = new double[K];
            for (var p = 0; p < pu; p++) radius[p] = Rest * Lobe * Root(usize[p]);
            radius[pu] = half * Clear;
            bool[] fflag = null;
            double[] fat = null;
            if (gflag != null)
            {
                fflag = new bool[K];
                fat = new double[3 * K];
                for (var g = 0; g < k; g++)
                {
                    if (over[g] || !gflag[g]) continue;
                    var p = unit[g];
                    fflag[p] = true;
                    fat[3 * p] = gat[3 * g];
                    fat[3 * p + 1] = gat[3 * g + 1];
                    fat[3 * p + 2] = gat[3 * g + 2];
                }

                if (frameP != null)
                {
                    fflag[pu] = true;
                    fat[3 * pu] = frameP[0];
                    fat[3 * pu + 1] = frameP[1];
                    fat[3 * pu + 2] = frameP[2];
                }
            }

            var upt = Pack(K, radius, ulink, uopp, udirs, fflag, fat);
            var plateAt = new double[3 * n];
            for (var v = 0; v < n; v++)
            {
                if (!pinned[v]) continue;
                for (var c = 0; c < 3; c++) plateAt[3 * v + c] = upt[3 * pu + c] + q[3 * v + c];
            }

            // H4: the snap, before any group reads its centre's standing place.
            if (hflag != null)
                for (var v = 0; v < n; v++)
                {
                    if (!pinned[v] || !hflag[v]) continue;
                    var close = true;
                    for (var c = 0; c < 3; c++)
                    {
                        var d = plateAt[3 * v + c] - hat[3 * v + c];
                        if (!(d <= near && -d <= near)) close = false;
                    }

                    if (close)
                        for (var c = 0; c < 3; c++)
                            plateAt[3 * v + c] = hat[3 * v + c];
                }

            var cpt = new double[3 * k];
            for (var g = 0; g < k; g++)
            {
                if (over[g])
                {
                    var c = taken[g];
                    cpt[3 * g] = plateAt[3 * c] + 0;
                    cpt[3 * g + 1] = plateAt[3 * c + 1] + 0;
                    cpt[3 * g + 2] = plateAt[3 * c + 2] + (lobe[g] + Rest * 6);
                }
                else
                {
                    var p = unit[g];
                    cpt[3 * g] = upt[3 * p];
                    cpt[3 * g + 1] = upt[3 * p + 1];
                    cpt[3 * g + 2] = upt[3 * p + 2];
                }
            }

            answer.Cpt = cpt;
            answer.PlateAt = plateAt;
            return answer;
        }

        // ---- Steps 6 to 8: the feed, the start, the steps --------------------------------------------------------

        /// <summary>FNV-1a, 32 bits, over the id's UTF-8 bytes.</summary>
        private static uint Fnv1a(string id)
        {
            var h = 2166136261u;
            foreach (var b in Encoding.UTF8.GetBytes(id)) h = unchecked((h ^ b) * 16777619u);
            return h;
        }

        /// <summary>Steps 6, 7 and 8: each body node's point after the force steps, row v at 3v (rows outside the body unread).</summary>
        private static double[] ForceSteps(List<string> ids, bool[] body, List<int[]> pairs, int[] taken, int[] group, GroupPointsAnswer pts,
            bool[] keep, double[] hat, bool[] free, List<int> extra)
        {
            var n = ids.Count;
            var blist = new List<int>();
            var bi = new int[n];
            for (var v = 0; v < n; v++)
            {
                bi[v] = -1;
                if (!body[v]) continue;
                bi[v] = blist.Count;
                blist.Add(v);
            }

            var nb = blist.Count;
            var centre = new bool[n];
            foreach (var t in taken) centre[t] = true;
            var pinned = pts.PlateAt != null ? pts.Pinned : null;

            // 6. The movers, their anchors, fixed or not, their links and weights.
            var la = new List<int>();
            var lb = new List<int>();
            var lw = new List<double>();
            foreach (var p in pairs)
            {
                int i = bi[p[0]], j = bi[p[1]];
                if (i < 0 || j < 0) continue;
                la.Add(i);
                lb.Add(j);
                int u = blist[i], w = blist[j];
                var x = group[u] == group[w] ? 1.0 : Cross;
                if (pinned != null && (pinned[u] || pinned[w])) x = PlateWeight;
                lw.Add(x);
            }

            var m = nb + extra.Count;
            var anchor = new double[3 * m];
            var pull = new double[m];
            var isFixed = new bool[m];
            var mgroup = new int[m];
            var mid = new string[m];
            for (var i = 0; i < nb; i++)
            {
                var v = blist[i];
                if (pinned != null && pinned[v])
                    for (var c = 0; c < 3; c++) anchor[3 * i + c] = pts.PlateAt[3 * v + c];
                else if (keep != null && keep[v])
                    for (var c = 0; c < 3; c++) anchor[3 * i + c] = hat[3 * v + c];
                else
                    for (var c = 0; c < 3; c++) anchor[3 * i + c] = pts.Cpt[3 * group[v] + c];
                pull[i] = Hold;
                isFixed[i] = (centre[v] && !(free != null && free[group[v]])) || (pinned != null && pinned[v]) || (keep != null && keep[v]);
                mgroup[i] = group[v];
                mid[i] = ids[v];
            }

            for (var e = 0; e < extra.Count; e++)
            {
                var i = nb + e;
                var v = extra[e];
                for (var c = 0; c < 3; c++) anchor[3 * i + c] = hat[3 * v + c];
                pull[i] = 0;
                isFixed[i] = true;
                mgroup[i] = -1;
                mid[i] = ids[v];
            }

            // 7. The start: a fixed mover at its anchor; any other in its group's ball, by its own seeded generator.
            var p0 = new double[3 * m];
            var freeCount = new Dictionary<int, int>();
            for (var i = 0; i < m; i++)
            {
                if (isFixed[i]) continue;
                freeCount.TryGetValue(mgroup[i], out var fc);
                freeCount[mgroup[i]] = fc + 1;
            }

            for (var i = 0; i < m; i++)
            {
                if (isFixed[i])
                {
                    for (var c = 0; c < 3; c++) p0[3 * i + c] = anchor[3 * i + c];
                    continue;
                }

                var ball = Rest * Lobe * Root(freeCount[mgroup[i]]);
                var g = Fnv1a(mid[i]);
                Next(ref g);
                Next(ref g);
                double x = 0, y = 0, z = 0;
                for (var tries = 0; tries < 64; tries++)
                {
                    double r1 = 2 * Next(ref g) - 1, r2 = 2 * Next(ref g) - 1, r3 = 2 * Next(ref g) - 1;
                    if (r1 * r1 + r2 * r2 + r3 * r3 <= 1)
                    {
                        x = r1;
                        y = r2;
                        z = r3;
                        break;
                    }
                }

                p0[3 * i] = anchor[3 * i] + (0 + x * ball);
                p0[3 * i + 1] = anchor[3 * i + 1] + (0 + y * ball);
                p0[3 * i + 2] = anchor[3 * i + 2] + (0 + z * ball);
            }

            // 8. The steps.
            var pos = Relax(m, p0, anchor, pull, isFixed, la, lb, lw);
            var relaxed = new double[3 * n];
            for (var i = 0; i < nb; i++)
                for (var c = 0; c < 3; c++)
                    relaxed[3 * blist[i] + c] = pos[3 * i + c];
            return relaxed;
        }

        private static double[] Relax(int m, double[] p0, double[] anchor, double[] pull, bool[] isFixed, List<int> la, List<int> lb, List<double> lw)
        {
            const double R = Rest;
            const double cut = Cutoff * R;
            const double cut2 = cut * cut;
            var px = new double[m];
            var py = new double[m];
            var pz = new double[m];
            for (var i = 0; i < m; i++)
            {
                px[i] = p0[3 * i];
                py[i] = p0[3 * i + 1];
                pz[i] = p0[3 * i + 2];
            }

            var scale = new double[m];
            for (var e = 0; e < la.Count; e++)
            {
                scale[la[e]] += lw[e];
                scale[lb[e]] += lw[e];
            }

            for (var i = 0; i < m; i++) scale[i] = 1 / Math.Max(1, scale[i] / 4);
            var free = 0;
            for (var i = 0; i < m; i++)
                if (!isFixed[i]) free++;
            var run = free == 0 ? Math.Min(Steps3d, 1) : Steps3d;

            var dx = new double[m];
            var dy = new double[m];
            var dz = new double[m];
            var lx = new double[m];
            var ly = new double[m];
            var lz = new double[m];
            var key = new long[m];
            var order = new int[m];
            var cells = new Dictionary<long, int>();
            var cellStart = new List<int>();
            var cellKey = new List<long>();
            var cellFree = new List<bool>();
            for (var step = 0; step < run; step++)
            {
                // The cells: each mover's cell, the cells in rising order, each listing its movers in rising order.
                double x0 = double.PositiveInfinity, y0 = double.PositiveInfinity, z0 = double.PositiveInfinity;
                double x1 = double.NegativeInfinity, y1 = double.NegativeInfinity, z1 = double.NegativeInfinity;
                for (var i = 0; i < m; i++)
                {
                    if (px[i] < x0) x0 = px[i];
                    if (px[i] > x1) x1 = px[i];
                    if (py[i] < y0) y0 = py[i];
                    if (py[i] > y1) y1 = py[i];
                    if (pz[i] < z0) z0 = pz[i];
                    if (pz[i] > z1) z1 = pz[i];
                }

                var nx = (long)Math.Floor((x1 - x0) / cut) + 1;
                var ny = (long)Math.Floor((y1 - y0) / cut) + 1;
                var nz = (long)Math.Floor((z1 - z0) / cut) + 1;
                for (var i = 0; i < m; i++)
                {
                    var cx = (long)Math.Floor((px[i] - x0) / cut);
                    var cy = (long)Math.Floor((py[i] - y0) / cut);
                    var cz = (long)Math.Floor((pz[i] - z0) / cut);
                    key[i] = (cx * ny + cy) * nz + cz;
                    order[i] = i;
                }

                Array.Sort(order, (a, b) => key[a] != key[b] ? (key[a] < key[b] ? -1 : 1) : a - b);
                cells.Clear();
                cellStart.Clear();
                cellKey.Clear();
                cellFree.Clear();
                for (var u = 0; u < m; u++)
                {
                    var i = order[u];
                    if (cellKey.Count == 0 || cellKey[cellKey.Count - 1] != key[i])
                    {
                        cells[key[i]] = cellKey.Count;
                        cellKey.Add(key[i]);
                        cellStart.Add(u);
                        cellFree.Add(false);
                    }

                    if (!isFixed[i]) cellFree[cellFree.Count - 1] = true;
                }

                cellStart.Add(m);

                // The push.
                Array.Clear(dx, 0, m);
                Array.Clear(dy, 0, m);
                Array.Clear(dz, 0, m);
                for (var ci = 0; ci < cellKey.Count; ci++)
                {
                    var ck = cellKey[ci];
                    var cx = ck / (ny * nz);
                    var cy = ck / nz % ny;
                    var cz = ck % nz;
                    int s0 = cellStart[ci], s1 = cellStart[ci + 1];
                    for (var ox = 0; ox <= 1; ox++)
                        for (var oy = ox == 1 ? -1 : 0; oy <= 1; oy++)
                            for (var oz = ox == 1 || oy == 1 ? -1 : 0; oz <= 1; oz++)
                            {
                                long qx = cx + ox, qy = cy + oy, qz = cz + oz;
                                if (qx >= nx || qy < 0 || qy >= ny || qz < 0 || qz >= nz) continue;
                                if (!cells.TryGetValue((qx * ny + qy) * nz + qz, out var qi)) continue;
                                if (!cellFree[ci] && !cellFree[qi]) continue;
                                var same = qi == ci;
                                int t0 = cellStart[qi], t1 = cellStart[qi + 1];
                                for (var u = s0; u < s1; u++)
                                {
                                    var i = order[u];
                                    for (var w = same ? u + 1 : t0; w < t1; w++)
                                    {
                                        var j = order[w];
                                        if (isFixed[i] && isFixed[j]) continue;
                                        double ux = px[i] - px[j], uy = py[i] - py[j], uz = pz[i] - pz[j];
                                        var d2 = ux * ux + uy * uy + uz * uz;
                                        if (d2 > cut2) continue;
                                        var d = Math.Max(Math.Sqrt(d2), 0.000001);
                                        var f = ((R * R) / d - R / 3) / d;
                                        dx[i] = dx[i] + ux * f;
                                        dy[i] = dy[i] + uy * f;
                                        dz[i] = dz[i] + uz * f;
                                        dx[j] = dx[j] - ux * f;
                                        dy[j] = dy[j] - uy * f;
                                        dz[j] = dz[j] - uz * f;
                                    }
                                }
                            }
                }

                // The links.
                Array.Clear(lx, 0, m);
                Array.Clear(ly, 0, m);
                Array.Clear(lz, 0, m);
                for (var e = 0; e < la.Count; e++)
                {
                    int a = la[e], b = lb[e];
                    double ux = px[a] - px[b], uy = py[a] - py[b], uz = pz[a] - pz[b];
                    var d = Math.Max(Math.Sqrt(ux * ux + uy * uy + uz * uz), 0.000001);
                    var f = ((d - R) * 0.5 * lw[e]) / d;
                    lx[b] = lx[b] + ux * f;
                    ly[b] = ly[b] + uy * f;
                    lz[b] = lz[b] + uz * f;
                    lx[a] = lx[a] - ux * f;
                    ly[a] = ly[a] - uy * f;
                    lz[a] = lz[a] - uz * f;
                }

                // The move.
                var cap = Cap0 * R * (1 - (double)step / Steps3d) + R / 20;
                for (var i = 0; i < m; i++)
                {
                    if (isFixed[i])
                    {
                        px[i] = anchor[3 * i];
                        py[i] = anchor[3 * i + 1];
                        pz[i] = anchor[3 * i + 2];
                        continue;
                    }

                    var vx = dx[i] + lx[i] * scale[i] + (anchor[3 * i] - px[i]) * pull[i];
                    var vy = dy[i] + ly[i] * scale[i] + (anchor[3 * i + 1] - py[i]) * pull[i];
                    var vz = dz[i] + lz[i] * scale[i] + (anchor[3 * i + 2] - pz[i]) * pull[i];
                    var len = Math.Max(Math.Sqrt(vx * vx + vy * vy + vz * vz), 0.000000001);
                    var shrink = Math.Min(1, cap / len);
                    px[i] = px[i] + vx * shrink;
                    py[i] = py[i] + vy * shrink;
                    pz[i] = pz[i] + vz * shrink;
                }
            }

            var pos = new double[3 * m];
            for (var i = 0; i < m; i++)
            {
                pos[3 * i] = px[i];
                pos[3 * i + 1] = py[i];
                pos[3 * i + 2] = pz[i];
            }

            return pos;
        }

        // ---- Step 9: what is placed after the steps, with H6 -----------------------------------------------------

        private static double Len3(double x, double y, double z) => Math.Sqrt(x * x + y * y + z * z);

        private static double Skin(List<double> a)
        {
            var m = a.Count;
            if (m == 0) return 0;
            a.Sort();
            var vi = (m - 1) * 0.9;
            int lo, hi;
            if (vi >= m - 1) lo = hi = m - 1;
            else
            {
                lo = (int)Math.Floor(vi);
                hi = lo + 1;
            }

            var t = vi - lo;
            var d = a[hi] - a[lo];
            return t >= 0.5 ? a[hi] - d * (1 - t) : a[lo] + d * t;
        }

        private static int[] Hang(int n, bool[] body, int[] hangCut, int[] hangDepth, bool[] off, int[] taken, int[] group,
            GroupPointsAnswer pts, int[] degree, double[] relaxed, bool[] keep, double[] hat, out double[] pos)
        {
            var k = taken.Length;
            var cpt = pts.Cpt;
            var plateAt = pts.PlateAt;
            var pinned = pts.Pinned;
            pos = new double[3 * n];
            var how = new int[n];
            for (var v = 0; v < n; v++) how[v] = -1;

            // The kept nodes (held, not pinned) and the near test against every one of them.
            var kept = new List<int>();
            if (keep != null)
                for (var v = 0; v < n; v++)
                    if (keep[v] && !pinned[v]) kept.Add(v);
            bool Near(double x, double y, double z)
            {
                foreach (var u in kept)
                    if (Len3(x - hat[3 * u], y - hat[3 * u + 1], z - hat[3 * u + 2]) < Line) return true;
                return false;
            }

            // The body.
            var nbody = 0;
            for (var v = 0; v < n; v++)
            {
                if (!body[v]) continue;
                for (var c = 0; c < 3; c++) pos[3 * v + c] = relaxed[3 * v + c];
                how[v] = taken[group[v]] == v ? HowCentre : HowWith;
                nbody++;
            }

            // The middle.
            double midX = 0, midY = 0, midZ = 0;
            for (var v = 0; v < n; v++)
            {
                if (!body[v]) continue;
                midX += relaxed[3 * v];
                midY += relaxed[3 * v + 1];
                midZ += relaxed[3 * v + 2];
            }

            midX /= nbody;
            midY /= nbody;
            midZ /= nbody;

            // The skin of each group.
            var skin = new double[k];
            for (var g = 0; g < k; g++)
            {
                var vals = new List<double>();
                for (var v = 0; v < n; v++)
                {
                    if (!body[v] || group[v] != g || pinned[v]) continue;
                    vals.Add(Len3(relaxed[3 * v] - cpt[3 * g], relaxed[3 * v + 1] - cpt[3 * g + 1], relaxed[3 * v + 2] - cpt[3 * g + 2]));
                }

                skin[g] = Skin(vals);
            }

            // What hangs: each cut in the one order, its nodes by depth, then the one order.
            var lists = new List<int>[n];
            for (var v = 0; v < n; v++)
            {
                if (hangCut[v] == -1) continue;
                if (lists[hangCut[v]] == null) lists[hangCut[v]] = new List<int>();
                lists[hangCut[v]].Add(v);
            }

            var fp = new double[3];
            for (var c = 0; c < n; c++)
            {
                var row = lists[c];
                if (row == null) continue;
                row.Sort((x, y) => hangDepth[x] != hangDepth[y] ? hangDepth[x] - hangDepth[y] : x - y);
                var g = group[c];

                // Outward.
                double ux = pos[3 * c] - cpt[3 * g], uy = pos[3 * c + 1] - cpt[3 * g + 1], uz = pos[3 * c + 2] - cpt[3 * g + 2];
                var lu = Len3(ux, uy, uz);
                if (lu > 0.000001)
                {
                    ux = ux / lu;
                    uy = uy / lu;
                    uz = uz / lu;
                }
                else ux = uy = uz = 0.0;

                double ox = cpt[3 * g] - midX, oy = cpt[3 * g + 1] - midY, oz = cpt[3 * g + 2] - midZ;
                var lo = Len3(ox, oy, oz);
                if (lo > 0.000001)
                {
                    ox = ox / lo;
                    oy = oy / lo;
                    oz = oz / lo;
                }
                else
                {
                    ox = 0.0;
                    oy = 0.0;
                    oz = 1.0;
                }

                ux = ux + 0.75 * ox;
                uy = uy + 0.75 * oy;
                uz = uz + 0.75 * oz;
                var l = Len3(ux, uy, uz);
                ux = ux / l;
                uy = uy / l;
                uz = uz / l;

                // t = u x (0, 0, 1), else u x (1, 0, 0); w = u x t.
                double tx = uy * 1.0 - uz * 0.0, ty = uz * 0.0 - ux * 1.0, tz = ux * 0.0 - uy * 0.0;
                if (Len3(tx, ty, tz) < 0.001)
                {
                    tx = uy * 0.0 - uz * 0.0;
                    ty = uz * 1.0 - ux * 0.0;
                    tz = ux * 0.0 - uy * 1.0;
                }

                var lt = Len3(tx, ty, tz);
                tx = tx / lt;
                ty = ty / lt;
                tz = tz / lt;
                double wx = uy * tz - uz * ty, wy = uz * tx - ux * tz, wz = ux * ty - uy * tx;
                var overPlace = pinned[c];
                double cux = ux, cuy = uy, cuz = uz;

                void FanPoint(int j, int d)
                {
                    var ang = Gold * j;
                    var fan = Rest * 0.5 * Math.Sqrt(j);
                    double ca = Math.Cos(ang), sa = Math.Sin(ang);
                    if (overPlace)
                    {
                        fp[0] = plateAt[3 * c] + ca * fan * 0.5;
                        fp[1] = plateAt[3 * c + 1] + sa * fan * 0.5;
                        fp[2] = plateAt[3 * c + 2] + Rest * d;
                    }
                    else
                    {
                        var reach = skin[g] + Rest * d;
                        fp[0] = cpt[3 * g] + cux * reach + (tx * ca + wx * sa) * fan;
                        fp[1] = cpt[3 * g + 1] + cuy * reach + (ty * ca + wy * sa) * fan;
                        fp[2] = cpt[3 * g + 2] + cuz * reach + (tz * ca + wz * sa) * fan;
                    }
                }

                HashSet<int> used = null;
                if (keep != null)
                {
                    // H6: first the claims of the kept nodes of the list.
                    used = new HashSet<int>();
                    foreach (var v in row)
                    {
                        if (!keep[v] || pinned[v]) continue;
                        var d = hangDepth[v];
                        double hx = hat[3 * v], hy = hat[3 * v + 1], hz = hat[3 * v + 2];
                        double axx, axy, axz;
                        if (overPlace)
                        {
                            axx = plateAt[3 * c];
                            axy = plateAt[3 * c + 1];
                            axz = plateAt[3 * c + 2] + Rest * d;
                        }
                        else
                        {
                            var reach = skin[g] + Rest * d;
                            axx = cpt[3 * g] + cux * reach;
                            axy = cpt[3 * g + 1] + cuy * reach;
                            axz = cpt[3 * g + 2] + cuz * reach;
                        }

                        var bound = Len3(hx - axx, hy - axy, hz - axz) + Line + Rest;
                        for (var j = 0;; j++)
                        {
                            var fan = Rest * 0.5 * Math.Sqrt(j);
                            if (!((overPlace ? fan * 0.5 : fan) < bound)) break;
                            FanPoint(j, d);
                            if (Len3(fp[0] - hx, fp[1] - hy, fp[2] - hz) < Line) used.Add(j);
                        }
                    }
                }

                for (var r = 0; r < row.Count; r++)
                {
                    var v = row[r];
                    var d = hangDepth[v];
                    how[v] = degree[v] == 1 && d == 1 ? HowLink : HowArm;
                    var j = r;
                    if (used != null)
                    {
                        if (keep[v] && !pinned[v]) continue;
                        for (j = 0;; j++)
                        {
                            if (used.Contains(j)) continue;
                            FanPoint(j, d);
                            if (!Near(fp[0], fp[1], fp[2])) break;
                        }

                        used.Add(j);
                    }

                    FanPoint(j, d);
                    pos[3 * v] = fp[0];
                    pos[3 * v + 1] = fp[1];
                    pos[3 * v + 2] = fp[2];
                }
            }

            // Off the main piece: placed by no rule of a run.
            for (var v = 0; v < n; v++)
                if (off[v] && !pinned[v]) how[v] = HowOff;

            // Kept: every kept node at its held point.
            foreach (var v in kept)
                for (var c = 0; c < 3; c++)
                    pos[3 * v + c] = hat[3 * v + c];

            // The map, last: every pinned node at its standing place.
            for (var v = 0; v < n; v++)
            {
                if (!pinned[v]) continue;
                for (var c = 0; c < 3; c++) pos[3 * v + c] = plateAt[3 * v + c];
                how[v] = HowMap;
            }

            for (var v = 0; v < n; v++)
                if (how[v] == -1) throw new InvalidOperationException("LayoutByLinks3d: a node was placed by no rule");
            return how;
        }
    }
}
