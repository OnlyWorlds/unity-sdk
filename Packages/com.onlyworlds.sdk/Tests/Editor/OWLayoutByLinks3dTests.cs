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
    /// The <c>by-links-3d</c> layout on the pinned schema distribution's fixture,
    /// <c>profiles/layout-by-links-3d.fixture.json</c> (the default layouts page, <c>profiles/layout.md</c>, serial 137),
    /// and its directions table, <c>profiles/layout-by-links-3d.directions.json</c>, both vendored byte for byte in
    /// <c>Tests/Editor/Fixtures/layout/</c>. Read through <see cref="OWJson"/>, which reads every double exactly. The
    /// fixture's <c>unplaced</c> suite is read whole by
    /// <see cref="OWLayout.UnplacedByLinks3d(JObject, System.Collections.IEnumerable)"/>, the ids compared exactly and in
    /// order.
    /// </summary>
    public class OWLayoutByLinks3dTests
    {
        /// <summary><c>profiles/layout-by-links-3d.fixture.json</c> in schema-dist <c>v0.34.1-dist.139</c>.</summary>
        private const string FixtureSha256 = "acd6da7252493485cafc24356a84c53656d078872a28c695c0f7554009b6fb2a";

        /// <summary><c>profiles/layout-by-links-3d.directions.json</c> in schema-dist <c>v0.34.1-dist.139</c>, the fixture's own <c>table_sha256</c>.</summary>
        private const string DirectionsSha256 = "37dc2bbb4a1769e4ddafc2d0a27c9889229c3ef483e8f7cd6c9ba66a0d1afb87";

        private static JObject _fixture;

        private static string Vendored(string file)
        {
            var root = TestPackage.Root();
            return root == null ? null : Path.Combine(root, "Tests", "Editor", "Fixtures", "layout", file);
        }

        private static JObject Fixture
        {
            get
            {
                if (_fixture != null) return _fixture;
                var path = Vendored("layout-by-links-3d.fixture.json");
                return path == null || !File.Exists(path) ? null : _fixture = OWJson.ParseObject(File.ReadAllText(path));
            }
        }

        private static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(File.ReadAllBytes(path)).Select(b => b.ToString("x2")));
        }

        [Test]
        public void TheFixtureAndTable_AreThePinnedDistsBytes()
        {
            var fixture = Vendored("layout-by-links-3d.fixture.json");
            var table = Vendored("layout-by-links-3d.directions.json");
            Assert.That(fixture != null && File.Exists(fixture) && File.Exists(table), Is.True, "the fixture and the table are vendored");
            Assert.That(Sha256(fixture), Is.EqualTo(FixtureSha256));
            Assert.That(Sha256(table), Is.EqualTo(DirectionsSha256));
            Assert.That((string)Fixture["table_sha256"], Is.EqualTo(DirectionsSha256));
            Assert.That((string)Fixture["method"], Is.EqualTo("by-links-3d"));
            Assert.That(Fixture["cases"].Count(), Is.EqualTo(116));
            Assert.That(Fixture["unplaced"].Count(), Is.EqualTo(9));
        }

        /// <summary>One test per <c>unplaced</c> case, named after it.</summary>
        public static IEnumerable<TestCaseData> UnplacedCases()
        {
            var fx = Fixture;
            if (fx == null)
            {
                yield return new TestCaseData(string.Empty).SetName("UnplacedByLinks3d_FixtureNotFound");
                yield break;
            }

            foreach (var c in fx["unplaced"].Cast<JObject>())
                yield return new TestCaseData((string)c["name"]).SetName("UnplacedByLinks3d_" + ((string)c["name"]).Replace('-', '_'));
        }

        [TestCaseSource(nameof(UnplacedCases))]
        public void UnplacedByLinks3d_GivesTheFixturesIds_InIdOrder(string name)
        {
            Assert.That(Fixture, Is.Not.Null, "the fixture is vendored");
            var c = (JObject)Fixture["unplaced"].First(x => (string)x["name"] == name);
            var got = OWLayout.UnplacedByLinks3d((JObject)c["graph"].DeepClone(), (JArray)c["placed"].DeepClone());
            Assert.That(got, Is.EqualTo(c["want"].Select(t => (string)t).ToArray()));
        }

        /// <summary>The page's tolerance for an element placed <c>arm</c> or <c>link</c> and every element of a ring piece; every other number is exact.</summary>
        private const double Within = 6e-8;

        [Test]
        public void TheDirectionsTable_IsTheVendoredTable_BitForBit()
        {
            var table = OWJson.ParseObject(File.ReadAllText(Vendored("layout-by-links-3d.directions.json")));
            var kMax = (int)table["k_max"];
            Assert.That(kMax, Is.EqualTo(OWLayout.DirectionsMax));
            var flat = new List<ulong>();
            for (var k = 1; k <= kMax; k++)
            {
                var row = (JArray)table["directions"][k.ToString()];
                Assert.That(row.Count, Is.EqualTo(k));
                foreach (var p in row)
                    for (var a = 0; a < 3; a++)
                        flat.Add((ulong)BitConverter.DoubleToInt64Bits((double)p[a]));
            }

            Assert.That(OWLayout.DirectionBits, Is.EqualTo(flat.ToArray()));
        }

        /// <summary>One test per case of the method's fixture, numbered and named after it.</summary>
        public static IEnumerable<TestCaseData> Cases()
        {
            var fx = Fixture;
            if (fx == null)
            {
                yield return new TestCaseData(-1).SetName("LayoutByLinks3d_FixtureNotFound");
                yield break;
            }

            var cases = (JArray)fx["cases"];
            for (var i = 0; i < cases.Count; i++)
            {
                var name = new string(((string)cases[i]["name"]).Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
                yield return new TestCaseData(i).SetName($"LayoutByLinks3d_{i:000}_{name}");
            }
        }

        /// <summary>
        /// The fixture's ring pieces (its <c>compare.pieces</c>): every piece other than the main one, of two nodes or
        /// more, none of them placed <c>map</c> in the want and none in the input's <c>held</c>.
        /// </summary>
        private static HashSet<string> RingNodes(JObject input, JObject want)
        {
            var ids = input["nodes"].Select(x => (string)x["id"]).ToList();
            var at = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < ids.Count; i++) at[ids[i]] = i;
            var parent = Enumerable.Range(0, ids.Count).ToArray();
            int Find(int a)
            {
                while (parent[a] != a) a = parent[a] = parent[parent[a]];
                return a;
            }

            foreach (var e in input["edges"])
            {
                if (!at.TryGetValue((string)e["source"] ?? string.Empty, out var a) || !at.TryGetValue((string)e["target"] ?? string.Empty, out var b) || a == b) continue;
                parent[Find(a)] = Find(b);
            }

            var pieces = Enumerable.Range(0, ids.Count).GroupBy(Find).Select(g => g.Select(i => ids[i]).ToList()).ToList();
            var least = pieces.Select(p => p.OrderBy(x => x, OWCodePointComparer.Instance).First()).ToList();
            var main = 0;
            for (var p = 1; p < pieces.Count; p++)
                if (pieces[p].Count > pieces[main].Count || (pieces[p].Count == pieces[main].Count && OWCodePointComparer.Instance.Compare(least[p], least[main]) < 0))
                    main = p;
            var map = new HashSet<string>(StringComparer.Ordinal);
            var wantIds = want["ids"].Select(t => (string)t).ToList();
            for (var i = 0; i < wantIds.Count; i++)
                if ((string)want["how"][i] == "map") map.Add(wantIds[i]);
            var held = input["held"] as JObject;
            var ring = new HashSet<string>(StringComparer.Ordinal);
            for (var p = 0; p < pieces.Count; p++)
            {
                if (p == main || pieces[p].Count < 2) continue;
                if (pieces[p].Any(id => map.Contains(id) || (held != null && held.Property(id) != null))) continue;
                ring.UnionWith(pieces[p]);
            }

            return ring;
        }

        private static string[] Strings(JToken list) => list.Select(t => t.Type == JTokenType.Null ? null : (string)t).ToArray();

        [TestCaseSource(nameof(Cases))]
        public void LayoutByLinks3d_GivesTheFixturesAnswer(int index)
        {
            Assert.That(Fixture, Is.Not.Null, "the fixture is vendored");
            var c = (JObject)Fixture["cases"][index];
            var name = (string)c["name"];
            Assert.That((int)c["steps"], Is.EqualTo(300), "STEPS is the method's own");
            var input = (JObject)c["input"].DeepClone();
            var graph = new JObject { ["nodes"] = input["nodes"], ["edges"] = input["edges"] };
            var rows = input["rows"] as JArray;
            var held = input.Property("held")?.Value;
            var heldUnit = input.Property("heldUnit")?.Value;
            var want = (JObject)c["want"];
            if (want.Property("refused") != null)
            {
                var ex = Assert.Throws<OWLayoutRefusedException>(() => OWLayout.LayoutByLinks3d(graph, rows, held, heldUnit), name);
                Assert.That(ex.Code, Is.EqualTo((string)want["refused"]), name);
                return;
            }

            var got = OWLayout.LayoutByLinks3d(graph, rows, held, heldUnit);
            Assert.That(got.Method, Is.EqualTo("by-links-3d"));
            Assert.That(got.Points.Select(p => p.Id), Is.EqualTo(Strings(want["ids"])), name + ": ids");
            Assert.That(got.Points.Select(p => p.How), Is.EqualTo(Strings(want["how"])), name + ": how");
            Assert.That(got.Points.Select(p => p.Group), Is.EqualTo(Strings(want["group"])), name + ": group");
            Assert.That(got.Points.Select(p => p.Part), Is.EqualTo(Strings(want["part"])), name + ": part");
            Assert.That(got.Points.Select(p => p.Held), Is.EqualTo(want["held"].Select(t => (bool)t)), name + ": held");
            Assert.That(got.Centres, Is.EqualTo(Strings(want["centres"])), name + ": centres");
            Assert.That(got.Unplaced, Is.EqualTo(Strings(want["unplaced"])), name + ": unplaced");

            var ring = RingNodes(input, want);
            var points = (JArray)want["points"];
            var off = new List<string>();
            for (var i = 0; i < got.Points.Count; i++)
            {
                var p = got.Points[i];
                var loose = ring.Contains(p.Id) || (!p.Held && (p.How == "arm" || p.How == "link"));
                var have = new[] { p.X, p.Y, p.Z };
                for (var a = 0; a < 3; a++)
                {
                    var w = (double)points[3 * i + a];
                    var ok = loose ? Math.Abs(have[a] - w) <= Within : have[a] == w;
                    if (!ok) off.Add($"{p.Id} {"xyz"[a]} ({p.How}{(ring.Contains(p.Id) ? ", ring" : string.Empty)}): {have[a]:R} against {w:R}");
                }
            }

            Assert.That(off, Is.Empty, $"{name}: {off.Count} coordinates differ from the fixture");
        }

        /// <summary>
        /// The page's saved Map (A saved <c>by-links-3d</c> Map): an answer saved as a Map marked <c>by-links-3d</c>, one
        /// whole-number Pin per placed element, reads back as a layout map of that method; its Pins' elements, as the
        /// map-space reading answers them, are <c>placed</c>, and the reading is the answer's own <c>unplaced</c>, and an
        /// element the world gains since is unplaced too. Handed back as the world's elements, the saved Map is no plate:
        /// the answer does not move.
        /// </summary>
        [Test]
        public void ASavedByLinks3dMap_ReadsBackAsALayoutMap_AndItsUnplacedIsTheAnswers()
        {
            var c = (JObject)Fixture["cases"].First(x => (string)x["name"] == "a saved layout Map beside a real Map");
            var graph = new JObject { ["nodes"] = c["input"]["nodes"].DeepClone(), ["edges"] = c["input"]["edges"].DeepClone() };
            var answer = OWLayout.LayoutByLinks3d(graph);
            Assert.That(answer.Unplaced, Is.Not.Empty, "the case leaves elements unplaced");

            var types = graph["nodes"].ToDictionary(n => (string)n["id"], n => (string)n["type"], StringComparer.Ordinal);
            var map = new JObject { ["id"] = "saved", ["type"] = "map", ["name"] = "saved", ["x_layout"] = new JObject { ["method"] = answer.Method } };
            var elements = new List<JToken> { map };
            var k = 0;
            foreach (var p in answer.Points)
                elements.Add(new JObject
                {
                    ["id"] = $"pin{k++:000}", ["type"] = "pin", ["map"] = "saved", ["element_type"] = types[p.Id], ["element_id"] = p.Id,
                    ["x"] = (long)Math.Round(p.X), ["y"] = (long)Math.Round(p.Y), ["z"] = (long)Math.Round(p.Z),
                });

            var maps = elements.Where(e => (string)e["type"] == "map").ToList();
            var pins = elements.Where(e => (string)e["type"] == "pin").ToList();
            var layout = OWMapSpace.LayoutOf(map, maps, pins);
            Assert.That(layout.IsLayoutMap, Is.True);
            Assert.That(layout.Layout, Is.EqualTo("by-links-3d"));

            var placed = OWMapSpace.Of(elements).Pins.Where(p => p.Map == "saved").Select(p => p.ElementId).ToList();
            Assert.That(placed.Count, Is.EqualTo(answer.Points.Count));
            Assert.That(OWLayout.UnplacedByLinks3d(graph, placed), Is.EqualTo(answer.Unplaced));

            var grown = (JObject)graph.DeepClone();
            ((JArray)grown["nodes"]).Add(new JObject { ["id"] = "zz-new", ["type"] = "character" });
            Assert.That(OWLayout.UnplacedByLinks3d(grown, placed), Is.EqualTo(answer.Unplaced.Concat(new[] { "zz-new" })));

            var again = OWLayout.LayoutByLinks3d(graph, elements);
            Assert.That(again.Points.Select(p => p.Id), Is.EqualTo(answer.Points.Select(p => p.Id)));
            for (var i = 0; i < again.Points.Count; i++)
                Assert.That(new[] { again.Points[i].X, again.Points[i].Y, again.Points[i].Z }, Is.EqualTo(new[] { answer.Points[i].X, answer.Points[i].Y, answer.Points[i].Z }), again.Points[i].Id);
        }

        [Test]
        public void TheGoldenAngle_IsThePagesExpression_AndThePackagesLiteral()
        {
            Assert.That(Math.PI * (3 - Math.Sqrt(5)), Is.EqualTo(2.399963229728653));
        }

        [Test]
        public void UnplacedByLinks3d_RefusesTwoNodesWithOneId()
        {
            var g = OWJson.ParseObject("{\"nodes\":[{\"id\":\"a\",\"type\":\"character\"},{\"id\":\"a\",\"type\":\"location\"}],\"edges\":[]}");
            Assert.Throws<ArgumentException>(() => OWLayout.UnplacedByLinks3d(g, new string[0]));
        }
    }
}
