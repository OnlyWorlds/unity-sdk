using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// <see cref="OWLayout.LayoutByKind(JObject)"/> and <see cref="OWLayout.LayoutAround(JObject, string, int)"/> on the
    /// pinned schema distribution's fixture, <c>profiles/layout.fixture.json</c> (the default layouts page,
    /// <c>profiles/layout.md</c>, serial 132), vendored byte for byte in <c>Tests/Editor/Fixtures/layout/layout.fixture.json</c>.
    /// Read through <see cref="OWJson"/>, which reads every double exactly. by-kind compares exactly, every x and y with
    /// <c>==</c>: the method is chaotic, so two runtimes agree bit for bit or not at all. around compares within 1e-12:
    /// each of its points is one cosine and one sine, and runtimes' cosines may differ in the last place. The fixture's
    /// third suite, <c>unplaced</c>, is read whole by <see cref="OWLayout.UnplacedByKind(JObject, System.Collections.IEnumerable)"/>
    /// and <see cref="OWLayout.UnplacedAround(JObject, string, System.Collections.IEnumerable, int)"/>, by each case's method word,
    /// the ids compared exactly and in order. Then the TypeScript SDK's
    /// other vectors (<c>test/layout-by-kind.test.mjs</c>). When the pin moves, copy the dist's file over the vendored one,
    /// regenerate the table (<c>codegen/generate_layout_anchors.py</c>), and update the hash; <c>codegen/check_drift.py</c>
    /// names a stale copy.
    /// </summary>
    public class OWLayoutTests
    {
        /// <summary>The fixture's sha256: <c>profiles/layout.fixture.json</c> in schema-dist <c>v0.34.1-dist.139</c> (dist.137 added two <c>unplaced</c> cases, 17 in all).</summary>
        private const string FixtureSha256 = "392b5b19b21d5beb1994692f51a8d8df3ef431f93bc279a7fc42ab0b0db508a5";

        private const double AroundTolerance = 1e-12;

        private static JObject _fixture;

        private static string FixturePath
        {
            get
            {
                var root = TestPackage.Root();
                return root == null ? null : Path.Combine(root, "Tests", "Editor", "Fixtures", "layout", "layout.fixture.json");
            }
        }

        private static JObject Fixture
        {
            get
            {
                if (_fixture != null) return _fixture;
                var path = FixturePath;
                return path == null || !File.Exists(path) ? null : _fixture = OWJson.ParseObject(File.ReadAllText(path));
            }
        }

        private static JObject Case(string name) => (JObject)Fixture["cases"].First(c => (string)c["name"] == name);

        private static JObject Around(string name) => (JObject)Fixture["around"].First(c => (string)c["name"] == name);

        private static JObject Graph(JObject c) => (JObject)c["graph"].DeepClone();

        /// <summary>One test per by-kind case, named after it.</summary>
        public static IEnumerable<TestCaseData> ByKindCases()
        {
            var fx = Fixture;
            if (fx == null)
            {
                yield return new TestCaseData(string.Empty).SetName("LayoutByKind_FixtureNotFound");
                yield break;
            }

            foreach (var c in fx["cases"].Cast<JObject>())
                yield return new TestCaseData((string)c["name"]).SetName("LayoutByKind_" + ((string)c["name"]).Replace('-', '_'));
        }

        /// <summary>One test per around case, named after it.</summary>
        public static IEnumerable<TestCaseData> AroundCases()
        {
            var fx = Fixture;
            if (fx == null)
            {
                yield return new TestCaseData(string.Empty).SetName("LayoutAround_FixtureNotFound");
                yield break;
            }

            foreach (var c in fx["around"].Cast<JObject>())
                yield return new TestCaseData((string)c["name"]).SetName("LayoutAround_" + ((string)c["name"]).Replace('-', '_'));
        }

        [Test]
        public void TheFixture_IsThePinnedDistsBytes_WithSeventeenExactCasesAndTenRings()
        {
            var path = FixturePath;
            Assert.That(path != null && File.Exists(path), Is.True, "the fixture is vendored");
            using (var sha = SHA256.Create())
            {
                var hex = string.Concat(sha.ComputeHash(File.ReadAllBytes(path)).Select(b => b.ToString("x2")));
                Assert.That(hex, Is.EqualTo(FixtureSha256));
            }

            Assert.That(((JObject)Fixture["compare"]).Properties().Select(p => p.Name), Is.EqualTo(new[] { "cases", "around", "unplaced" }));
            Assert.That(Fixture["cases"].Count(), Is.EqualTo(17));
            Assert.That(Fixture["around"].Count(), Is.EqualTo(10));
        }

        [Test]
        public void TheAnchorTable_IsTheFixturesAnchors_BitForBit()
        {
            var anchors = (JObject)Fixture["anchors"];
            Assert.That(anchors.Properties().Select(p => p.Name), Is.EqualTo(Enumerable.Range(1, 22).Select(k => k.ToString())));
            var flat = new List<ulong>();
            for (var k = 1; k <= 22; k++)
                foreach (var p in (JArray)anchors[k.ToString()])
                {
                    flat.Add((ulong)BitConverter.DoubleToInt64Bits((double)p[0]));
                    flat.Add((ulong)BitConverter.DoubleToInt64Bits((double)p[1]));
                }

            Assert.That(OWLayout.AnchorBits, Is.EqualTo(flat.ToArray()));
        }

        [Test]
        public void TheGroups_AreTheFixturesAndThePresentationDefaults()
        {
            var order = Fixture["group_order"].Select(t => (string)t).ToList();
            Assert.That(order, Is.EqualTo(new[] { "blue", "green", "pink", "amber" }));
            var groups = (JObject)Fixture["groups"];
            Assert.That(groups.Count, Is.EqualTo(22));
            Assert.That(OWLayout.GroupRank.Count, Is.EqualTo(22));
            foreach (var p in groups.Properties())
                Assert.That(OWLayout.GroupRank[p.Name], Is.EqualTo(order.IndexOf((string)p.Value)), p.Name);
            Assert.That(OWElementTypes.Slugs.OrderBy(s => s, StringComparer.Ordinal), Is.EqualTo(OWLayout.GroupRank.Keys.OrderBy(s => s, StringComparer.Ordinal)));
        }

        // The TypeScript SDK 4.177.0 marks both answers' version key: the layout profile's mark carries none, and a
        // method's word is its version. Nothing replaces it. by-links-3d's Version stays, as npm's BY_LINKS_3D_VERSION.
        [Test]
        public void TheTwoLayoutVersions_AreDeprecatedWithNoReplacement_AsAWarning()
        {
            foreach (var type in new[] { typeof(OWLayoutByKind), typeof(OWLayoutAround) })
            {
                var obsolete = (ObsoleteAttribute)Attribute.GetCustomAttribute(type.GetProperty("Version"), typeof(ObsoleteAttribute), false);
                Assert.IsNotNull(obsolete, $"{type.Name}.Version is not marked [Obsolete]");
                Assert.IsFalse(obsolete.IsError, $"{type.Name}.Version: a warning, not an error");
                Assert.AreEqual("Since 0.58.0. Leaves in 1.0. The layout profile's mark carries no version key.", obsolete.Message, type.Name);
            }
            Assert.IsNull(Attribute.GetCustomAttribute(typeof(OWLayoutByLinks3d).GetProperty("Version"), typeof(ObsoleteAttribute)), "by-links-3d's stays");
        }

        [TestCaseSource(nameof(ByKindCases))]
        public void LayoutByKind_GivesTheFixturesPoints_Exactly(string name)
        {
            Assert.That(Fixture, Is.Not.Null, "the fixture is vendored");
            var c = Case(name);
            var got = OWLayout.LayoutByKind(Graph(c));
            Assert.That(got.Method, Is.EqualTo("by-kind"));
#pragma warning disable CS0618 // deprecated, and still 1 until it leaves
            Assert.That(got.Version, Is.EqualTo(1));
#pragma warning restore CS0618
            var want = (JArray)c["want"];
            var nodes = (JArray)c["graph"]["nodes"];
            Assert.That(got.Points.Count, Is.EqualTo(want.Count));
            var off = 0;
            for (var k = 0; k < want.Count; k++)
            {
                Assert.That(got.Points[k].Id, Is.EqualTo((string)nodes[k]["id"]));
                if (!(got.Points[k].X == (double)want[k][0] && got.Points[k].Y == (double)want[k][1])) off++;
            }

            Assert.That(off, Is.EqualTo(0), $"{off} of {want.Count} points differ from the fixture");
        }

        /// <summary>One test per <c>unplaced</c> case, named after it: the fixture's whole list, read by its method word.</summary>
        public static IEnumerable<TestCaseData> UnplacedCases()
        {
            var fx = Fixture;
            if (fx == null)
            {
                yield return new TestCaseData(string.Empty).SetName("Unplaced_FixtureNotFound");
                yield break;
            }

            foreach (var c in fx["unplaced"].Cast<JObject>())
                yield return new TestCaseData((string)c["name"]).SetName("Unplaced_" + ((string)c["name"]).Replace('-', '_'));
        }

        [TestCaseSource(nameof(UnplacedCases))]
        public void Unplaced_GivesTheFixturesIds_InIdOrder(string name)
        {
            Assert.That(Fixture, Is.Not.Null, "the fixture is vendored");
            var c = (JObject)Fixture["unplaced"].First(x => (string)x["name"] == name);
            var placed = (JArray)c["placed"].DeepClone();
            var method = (string)c["method"];
            IReadOnlyList<string> got;
            if (method == "by-kind") got = OWLayout.UnplacedByKind(Graph(c), placed);
            else if (method == "around") got = OWLayout.UnplacedAround(Graph(c), (string)c["focus"], placed, (int)c["steps"]);
            else throw new AssertionException($"{name}: a method word this suite does not hold, {method}");
            Assert.That(got, Is.EqualTo(c["want"].Select(t => (string)t).ToArray()));
        }

        [Test]
        public void TheUnplacedSuite_IsSeventeenCases_ByKindAndAroundOnly()
        {
            var methods = Fixture["unplaced"].Select(c => (string)c["method"]).ToList();
            Assert.That(methods.Count, Is.EqualTo(17));
            Assert.That(methods.Distinct().OrderBy(m => m, StringComparer.Ordinal), Is.EqualTo(new[] { "around", "by-kind" }));
        }

        [Test]
        public void Unplaced_RefusesAPlacedThatIsNoList_AndReadsAPlainListOfStrings()
        {
            var g = OWJson.ParseObject("{\"nodes\":[{\"id\":\"b\",\"type\":\"character\"},{\"id\":\"a\",\"type\":\"location\"}],\"edges\":[]}");
            Assert.Throws<ArgumentException>(() => OWLayout.UnplacedByKind(g, null));
            Assert.Throws<ArgumentException>(() => OWLayout.UnplacedByKind(g, "a"));
            Assert.That(OWLayout.UnplacedByKind(g, new[] { "b", "", null, "z" }), Is.EqualTo(new[] { "a" }));
            Assert.That(OWLayout.UnplacedByKind(g, new HashSet<string>()), Is.EqualTo(new[] { "a", "b" }));
            Assert.That(OWLayout.UnplacedByKind(g, new object[] { 7, "a" }), Is.EqualTo(new[] { "b" }));
        }

        [TestCaseSource(nameof(AroundCases))]
        public void LayoutAround_GivesTheFixturesRing_Within1e12(string name)
        {
            Assert.That(Fixture, Is.Not.Null, "the fixture is vendored");
            var c = Around(name);
            var got = OWLayout.LayoutAround(Graph(c), (string)c["focus"], (int)c["steps"]);
            Assert.That(got.Method, Is.EqualTo("around"));
#pragma warning disable CS0618 // deprecated, and still 1 until it leaves
            Assert.That(got.Version, Is.EqualTo(1));
#pragma warning restore CS0618
            var want = (JArray)c["want"];
            Assert.That(got.Points.Select(p => p.Id + ":" + p.Depth), Is.EqualTo(want.Select(w => (string)w[0] + ":" + (int)w[1])));
            for (var k = 0; k < want.Count; k++)
            {
                var p = got.Points[k];
                Assert.That(Math.Abs(p.X - (double)want[k][2]) <= AroundTolerance && Math.Abs(p.Y - (double)want[k][3]) <= AroundTolerance,
                    Is.True, $"{p.Id}: ({p.X:R}, {p.Y:R}) against ({(double)want[k][2]:R}, {(double)want[k][3]:R})");
            }
        }

        // -- The TypeScript SDK's other vectors -----------------------------------

        private static string Key(OWLayoutPoint p) => $"{BitConverter.DoubleToInt64Bits(p.X)}:{BitConverter.DoubleToInt64Bits(p.Y)}";

        private static string Key(OWLayoutAroundPoint p) => $"{p.Depth}:{BitConverter.DoubleToInt64Bits(p.X)}:{BitConverter.DoubleToInt64Bits(p.Y)}";

        /// <summary>The TypeScript SDK test's seeded shuffle (its generator, seed 99), so a failure reruns the same.</summary>
        private static List<JToken> Shuffle(IEnumerable<JToken> items, Func<double> next)
        {
            var a = items.ToList();
            for (var i = a.Count - 1; i > 0; i--)
            {
                var j = (int)Math.Floor(next() * (i + 1));
                var t = a[i];
                a[i] = a[j];
                a[j] = t;
            }

            return a;
        }

        [Test]
        public void TheOrderNodesAndLinksAreHandedIn_MovesNoPoint()
        {
            uint s = 99;
            Func<double> next = () => (s = unchecked(s * 1664525u + 1013904223u)) / 4294967296.0;
            foreach (var name in new[] { "hyperion", "past-324", "a-self-link-pulls-nothing" })
            {
                var g = Graph(Case(name));
                var mixed = new JObject { ["nodes"] = new JArray(Shuffle(g["nodes"], next)), ["edges"] = new JArray(Shuffle(g["edges"], next)) };
                var got = OWLayout.LayoutByKind(mixed);
                Assert.That(got.Points.Select(p => p.Id), Is.EqualTo(mixed["nodes"].Select(x => (string)x["id"])), name);
                var plain = OWLayout.LayoutByKind(g).Points.ToDictionary(p => p.Id, Key);
                Assert.That(got.Points.ToDictionary(p => p.Id, Key), Is.EqualTo(plain), name);
            }

            var c = Around("two-steps");
            var g2 = Graph(c);
            var mixed2 = new JObject { ["nodes"] = new JArray(Shuffle(g2["nodes"], next)), ["edges"] = new JArray(Shuffle(g2["edges"], next)) };
            var got2 = OWLayout.LayoutAround(mixed2, (string)c["focus"], 2);
            var order = mixed2["nodes"].Select((x, k) => new { id = (string)x["id"], k }).ToDictionary(x => x.id, x => x.k);
            for (var k = 1; k < got2.Points.Count; k++)
                Assert.That(order[got2.Points[k - 1].Id], Is.LessThan(order[got2.Points[k].Id]), "in the order handed");
            Assert.That(got2.Points.ToDictionary(p => p.Id, Key), Is.EqualTo(OWLayout.LayoutAround(g2, (string)c["focus"], 2).Points.ToDictionary(p => p.Id, Key)));
        }

        [Test]
        public void ByKind_ALinkPullsBothEndsAlike_SoItsDirectionMovesNothing()
        {
            var c = Case("two-elements-linked");
            var g = Graph(c);
            var e = g["edges"][0];
            var flipped = new JObject
            {
                ["nodes"] = g["nodes"],
                ["edges"] = new JArray(new JObject { ["source"] = e["target"], ["target"] = e["source"], ["field"] = "x", ["index"] = 0 }),
            };
            var got = OWLayout.LayoutByKind(flipped).Points;
            var want = (JArray)c["want"];
            for (var k = 0; k < want.Count; k++)
                Assert.That(got[k].X == (double)want[k][0] && got[k].Y == (double)want[k][1], Is.True);
        }

        [Test]
        public void ByKind_FitsAFrameOfMinusOneToOneOnTheLongerAxis_Centred()
        {
            foreach (var name in new[] { "hyperion", "moppetopia", "past-324" })
            {
                var points = OWLayout.LayoutByKind(Graph(Case(name))).Points;
                double wx = points.Max(p => p.X) - points.Min(p => p.X), wy = points.Max(p => p.Y) - points.Min(p => p.Y);
                Assert.That(Math.Abs(Math.Max(wx, wy) - 2), Is.LessThan(1e-12), name);
                Assert.That(Math.Abs(points.Max(p => p.X) + points.Min(p => p.X)), Is.LessThan(1e-12), name);
                Assert.That(Math.Abs(points.Max(p => p.Y) + points.Min(p => p.Y)), Is.LessThan(1e-12), name);
            }
        }

        [Test]
        public void ByKind_TheEmptyGraphAnswersNoPoints_OneElementAnswersZero()
        {
            var none = OWLayout.LayoutByKind(new JObject { ["nodes"] = new JArray(), ["edges"] = new JArray() });
            Assert.That(none.Points, Is.Empty);
            var one = OWLayout.LayoutByKind(new JObject { ["nodes"] = new JArray(new JObject { ["id"] = "a", ["type"] = "zone" }), ["edges"] = new JArray() });
            Assert.That(one.Points.Select(p => $"{p.Id} {p.X} {p.Y}"), Is.EqualTo(new[] { "a 0 0" }));
        }

        [Test]
        public void ByKind_ALinkToAnIdThatIsNotANode_IsPassedOver()
        {
            var c = Case("two-elements-linked");
            var g = Graph(c);
            ((JArray)g["edges"]).Add(new JObject { ["source"] = g["nodes"][0]["id"], ["target"] = "not-a-node", ["field"] = "friends", ["index"] = 0 });
            var got = OWLayout.LayoutByKind(g).Points;
            var want = (JArray)c["want"];
            for (var k = 0; k < want.Count; k++)
                Assert.That(got[k].X == (double)want[k][0] && got[k].Y == (double)want[k][1], Is.True);
        }

        /// <summary>
        /// The graph reader's nodes carry the presentation sidecar's family. Outside the Editor <c>Resources</c> loads
        /// nothing, so the package's own vendored sidecar is read from its text, as <see cref="OWGraphTests"/> reads it.
        /// </summary>
        private static void ReadTheSidecar()
        {
            string family = null;
            try
            {
                family = OWPresentation.GroupOf("character");
            }
            catch (Exception)
            {
                // No engine behind Resources; read the text below.
            }

            if (family != null) return;
            var root = TestPackage.Root();
            if (root == null) return;
            try
            {
                OWPresentation.LoadText(File.ReadAllText(Path.Combine(root, "Runtime", "Resources", "ow-presentation.json")));
            }
            catch (Exception)
            {
                // The colours need the engine's parser; the type table is read before them.
            }
        }

        [Test]
        public void ByKind_RunsOnWorldGraphsAnswerAsItComes()
        {
            ReadTheSidecar();
            var elements = new JArray(
                new JObject { ["id"] = "01a1d4ef-0000-7000-8000-000000000002", ["type"] = "location", ["name"] = "Harbour" },
                new JObject { ["id"] = "01a1d4ef-0000-7000-8000-000000000001", ["type"] = "character", ["name"] = "Mara", ["birthplace"] = "01a1d4ef-0000-7000-8000-000000000002" },
                new JObject { ["id"] = "01a1d4ef-0000-7000-8000-000000000003", ["type"] = "creature", ["name"] = "Gull", ["location"] = "01a1d4ef-0000-7000-8000-000000000002" });
            var graph = OWGraph.Read(elements, OWLensReader.Owner());
            var got = OWLayout.LayoutByKind(graph);
            Assert.That(got.Points.Select(p => p.Id), Is.EqualTo(graph.Nodes.Select(n => n.Id)));
            Assert.That(got.Points.All(p => !double.IsNaN(p.X) && !double.IsInfinity(p.X) && !double.IsNaN(p.Y) && !double.IsInfinity(p.Y)), Is.True);
            var around = OWLayout.LayoutAround(graph, "01a1d4ef-0000-7000-8000-000000000002");
            Assert.That(around.Points.Select(p => p.Depth), Is.EqualTo(new[] { 1, 0, 1 }));
        }

        private static void Refused(JObject graph, string words)
        {
            var a = Assert.Throws<ArgumentException>(() => OWLayout.LayoutByKind(graph));
            Assert.That(a.Message, Does.StartWith("LayoutByKind: ").And.Contains(words));
            var b = Assert.Throws<ArgumentException>(() => OWLayout.LayoutAround(graph, "a"));
            Assert.That(b.Message, Does.StartWith("LayoutAround: ").And.Contains(words));
        }

        private static JObject G(JArray nodes, JArray edges = null)
        {
            var g = new JObject();
            if (nodes != null) g["nodes"] = nodes;
            if (edges != null) g["edges"] = edges;
            return g;
        }

        private static JObject N(string id, string type) => new JObject { ["id"] = id, ["type"] = type };

        [Test]
        public void AGraphItCannotRead_IsRefusedByName()
        {
            Refused(null, "the graph is { nodes, edges }");
            Refused(G(new JArray()), "the graph is { nodes, edges }");
            Refused(G(new JArray(JValue.CreateNull()), new JArray()), "node 0 is not an object");
            Refused(G(new JArray(new JObject { ["type"] = "character" }), new JArray()), "node 0 has no id");
            Refused(G(new JArray(N("a", "starship")), new JArray()), "'starship' is not one of the 22 element types");
            Refused(G(new JArray(N("a", "toString")), new JArray()), "'toString' is not one of the 22 element types");
            Refused(G(new JArray(new JObject { ["id"] = "a", ["type"] = "character", ["name"] = 7 }), new JArray()), "name is a string or null");
            Refused(G(new JArray(N("a", "character"), N("a", "location")), new JArray()), "the id a is a node twice");
            Refused(G(new JArray(N("a", "character")), new JArray(new JObject { ["source"] = "a" })), "edge 0 is not { source, target }");
            Refused(G(new JArray(N("a", "character")), new JArray(new JObject { ["source"] = "a", ["target"] = "a", ["field"] = 3 })), "edge 0: field is a string");
            Refused(G(new JArray(N("a", "character")), new JArray(new JObject { ["source"] = "a", ["target"] = "a", ["field"] = null })), "edge 0: field is a string");
            Refused(G(new JArray(N("a", "character")), new JArray(new JObject { ["source"] = "a", ["target"] = "a", ["index"] = double.NaN })), "edge 0: index is a number");
            Refused(G(new JArray(N("a", "character")), new JArray(new JObject { ["source"] = "a", ["target"] = "a", ["index"] = "0" })), "edge 0: index is a number");
            Refused(G(new JArray(N("a", "character")), new JArray(new JObject { ["source"] = "a", ["target"] = "a", ["index"] = null })), "edge 0: index is a number");
        }

        [Test]
        public void Around_StepsIsOneOrTwo_AndTheFocusIsANode()
        {
            var g = G(new JArray(N("a", "character")), new JArray());
            foreach (var steps in new[] { 0, 3, -1 })
                Assert.That(Assert.Throws<ArgumentException>(() => OWLayout.LayoutAround(g, "a", steps)).Message, Does.Contain("LayoutAround: steps is 1 or 2"));
            Assert.That(Assert.Throws<ArgumentException>(() => OWLayout.LayoutAround(G(new JArray(), new JArray()), "a")).Message,
                Does.Contain("the focus a is not a node of the graph"));
            Assert.Throws<ArgumentException>(() => OWLayout.LayoutAround(g, "b"));
            Assert.Throws<ArgumentException>(() => OWLayout.LayoutAround(g, null));
            Assert.That(OWLayout.LayoutAround(g, "a").Points.Count, Is.EqualTo(1));
        }

        [Test]
        public void Around_OneStepAnswersTheNeighboursOnly_TwoAddTheirs()
        {
            var one = Around("one-step-of-the-same");
            var two = Around("two-steps");
            var d1 = OWLayout.LayoutAround(Graph(one), (string)one["focus"]).Points;
            var d2 = OWLayout.LayoutAround(Graph(two), (string)two["focus"], 2).Points;
            Assert.That(d1.All(p => p.Depth <= 1) && d1.Count(p => p.Depth == 0) == 1, Is.True);
            Assert.That(d2.Any(p => p.Depth == 2), Is.True);
            Assert.That(d2.Where(p => p.Depth < 2).Select(p => p.Id + Key(p)), Is.EqualTo(d1.Select(p => p.Id + Key(p))));
        }

        private static List<string> RingByAngle(OWLayoutAround around, Dictionary<string, string> names) =>
            around.Points.Where(p => p.Depth == 1)
                .Select(p => new { name = names[p.Id], a = (Math.Atan2(p.Y, p.X) + Math.PI / 2 + 4 * Math.PI) % (2 * Math.PI) })
                .OrderBy(p => p.a).Select(p => p.name).ToList();

        [Test]
        public void Around_NamesOrderByCodePoint_NeverByLocale()
        {
            var c = Around("names-by-code-point-not-locale");
            var names = c["graph"]["nodes"].ToDictionary(x => (string)x["id"], x => (string)x["name"]);
            var ring = RingByAngle(OWLayout.LayoutAround(Graph(c), (string)c["focus"]), names);
            Assert.That(ring, Is.EqualTo(new[] { "Bea", "Zed", "adam", "bea", "Émile", "Øyvind", "\U0001D400lpha" }));
        }

        private static bool Negative(double v) => BitConverter.DoubleToInt64Bits(v) < 0;

        [Test]
        public void MinAndMax_AreJavaScripts()
        {
            Assert.That(Negative(OWLayout.MinJs(0.0, -0.0)) && Negative(OWLayout.MinJs(-0.0, 0.0)), Is.True, "min(+0, -0) is -0, either way round");
            Assert.That(Negative(OWLayout.MaxJs(0.0, -0.0)) || Negative(OWLayout.MaxJs(-0.0, 0.0)), Is.False, "max(+0, -0) is +0, either way round");
            Assert.That(double.IsNaN(OWLayout.MinJs(double.NaN, 1)) && double.IsNaN(OWLayout.MaxJs(1, double.NaN)), Is.True, "NaN if either is");
            Assert.That(OWLayout.MinJs(1, 2), Is.EqualTo(1));
            Assert.That(OWLayout.MaxJs(1, 2), Is.EqualTo(2));
        }

        [Test]
        public void Around_NamesOrderByCodePoint_NotByUtf16CodeUnit()
        {
            // U+FF5A (one code unit, 0xFF5A) sorts before U+1D400 (a surrogate pair, 0xD835 0xDC00) by code point, after it
            // by UTF-16 code unit. The fixture's names-by-code-point-not-utf-16 case sets the two orders apart as well
            // (serial 132); this pair is the smallest that does.
            var nodes = new JArray(N("f", "character"), new JObject { ["id"] = "p", ["type"] = "character", ["name"] = "\U0001D400" },
                new JObject { ["id"] = "q", ["type"] = "character", ["name"] = "ｚ" });
            var edges = new JArray(new JObject { ["source"] = "f", ["target"] = "p", ["field"] = "friends", ["index"] = 0 },
                new JObject { ["source"] = "f", ["target"] = "q", ["field"] = "friends", ["index"] = 1 });
            var names = new Dictionary<string, string> { ["f"] = null, ["p"] = "\U0001D400", ["q"] = "ｚ" };
            Assert.That(RingByAngle(OWLayout.LayoutAround(G(nodes, edges), "f"), names), Is.EqualTo(new[] { "ｚ", "\U0001D400" }));
        }
    }
}
