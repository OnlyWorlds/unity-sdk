using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The landing profile's conformance fixture (schema-dist <c>profiles/landing.fixture.json</c>, vendored byte for
    /// byte in <c>Tests/Editor/Fixtures/landing/</c> from dist.103), replayed whole, case by case by name, through
    /// <see cref="OWLanding.Of"/>: each case's graph (in <see cref="OWGraph.Read"/>'s shape, only the keys the reader
    /// reads) built as the graph it states, its options read as JSON (<see cref="OWLandingOptions.FromJson"/>), and the
    /// keys its <c>want</c> holds compared, the whole answer with its keys in order where the case is marked
    /// <c>whole</c>. The TypeScript SDK's <c>landingOf</c> and the Python package's <c>landing_of</c> replay the same
    /// cases. When the dist moves the fixture, re-vendor it and update the hash and the count below.
    /// </summary>
    public class OWLandingTests
    {
        /// <summary>The fixture's sha256 as dist.103's MANIFEST.json records it.</summary>
        private const string DistSha256 = "bf6512dea464946e6fa3855d3a942446ecc97aabc564258d5ba801a6f7e01475";

        /// <summary>The fixture's cases, as dist.103 holds them.</summary>
        private const int CaseCount = 30;

        private static JObject _fixture;

        private static string FixturePath
        {
            get
            {
                var root = TestPackage.Root();
                return root == null ? null : Path.Combine(root, "Tests", "Editor", "Fixtures", "landing", "landing.fixture.json");
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

        /// <summary>One case per fixture case, numbered in the fixture's order; the case is found by its name.</summary>
        public static IEnumerable<TestCaseData> Cases()
        {
            var fixture = Fixture;
            if (fixture == null)
            {
                yield return new TestCaseData(string.Empty).SetName("Landing_FixtureNotFound");
                yield break;
            }

            var i = 0;
            foreach (var c in fixture["cases"].Cast<JObject>())
                yield return new TestCaseData((string)c["name"]).SetName($"Landing_{++i:D2}");
        }

        [Test]
        public void TheFixture_IsTheDistsBytes()
        {
            var path = FixturePath;
            Assert.IsTrue(path != null && File.Exists(path), "the vendored landing fixture was not found: " + path);
            using var sha = SHA256.Create();
            var hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
            Assert.AreEqual(DistSha256, hash,
                "the vendored fixture is not dist.103's: re-vendor it from schema-dist (a checkout's line endings change the bytes)");
        }

        [Test]
        public void TheFixture_HoldsDist103sCases_EachNamedOnce()
        {
            Assert.IsNotNull(Fixture, "the vendored landing fixture was not found");
            Assert.AreEqual("landing", (string)Fixture["profile"]);
            var names = Fixture["cases"].Select(c => (string)c["name"]).ToList();
            Assert.AreEqual(CaseCount, names.Count, "dist.103's fixture holds 30 cases");
            Assert.AreEqual(names.Count, names.Distinct().Count(), "a case is found by its name, so each name is once");
        }

        [TestCaseSource(nameof(Cases))]
        public void EveryCase_IsAnsweredAsTheProfileSays(string name)
        {
            Assert.IsNotNull(Fixture, "the vendored landing fixture was not found");
            var c = Fixture["cases"].Cast<JObject>().Single(x => (string)x["name"] == name);
            var graph = GraphOf((JObject)c["graph"]);
            var want = (JObject)c["want"];

            if (want.TryGetValue("refused", out var refused))
            {
                var ex = Assert.Throws<OWLandingRefusedException>(() => OWLanding.Of(graph, OptionsOf(c)), name + ": not refused");
                Assert.AreEqual((string)refused, ex.Option, $"{name}: refused as {ex.Message}");
                StringAssert.Contains((string)refused, ex.Message, name + ": the message names the option");
                return;
            }

            var got = OWLanding.Of(graph, OptionsOf(c)).ToJson();
            CollectionAssert.AreEqual(new[] { "mode", "size", "clusters", "unclustered", "gaps" },
                got.Properties().Select(p => p.Name).ToList(), name + ": all five keys, in order");
            if (c["whole"]?.Type == JTokenType.Boolean && (bool)c["whole"])
            {
                CollectionAssert.AreEqual(want.Properties().Select(p => p.Name).ToList(),
                    got.Properties().Select(p => p.Name).ToList(), name + ": the whole answer's keys in order");
                Assert.AreEqual(want.ToString(Formatting.None), got.ToString(Formatting.None), name + ": the whole answer");
                return;
            }

            foreach (var p in want.Properties())
            {
                Assert.AreEqual(p.Value.ToString(Formatting.None), got[p.Name].ToString(Formatting.None),
                    $"{name}: {p.Name}");
            }
        }

        /// <summary>The same graph and options give the same answer, byte for byte, and the graph is not changed.</summary>
        [Test]
        public void TheSameGraph_GivesTheSameBytes_AndIsNotChanged()
        {
            Assert.IsNotNull(Fixture, "the vendored landing fixture was not found");
            foreach (var c in Fixture["cases"].Cast<JObject>())
            {
                if (c["want"]["refused"] != null) continue;
                var graph = GraphOf((JObject)c["graph"]);
                var before = GraphJson(graph);
                var once = OWLanding.Of(graph, OptionsOf(c)).ToJson().ToString(Formatting.None);
                var twice = OWLanding.Of(graph, OptionsOf(c)).ToJson().ToString(Formatting.None);
                Assert.AreEqual(once, twice, (string)c["name"]);
                Assert.AreEqual(before, GraphJson(graph), (string)c["name"] + ": the graph moved");
            }
        }

        /// <summary>
        /// The typed options are refused as the JSON ones are: a value under 1 named, before anything is read; null and
        /// absent are the defaults (5, 1, 24); a whole number with a zero fraction is that number; no graph is no reading.
        /// </summary>
        [Test]
        public void TheOptions_AreRefusedByName_AndNullIsTheDefault()
        {
            var empty = GraphOf(JObject.Parse(@"{""nodes"":[],""edges"":[]}"));
            Assert.AreEqual("heads", Assert.Throws<OWLandingRefusedException>(() => OWLanding.Of(empty, new OWLandingOptions { Heads = 0 })).Option);
            Assert.AreEqual("hops", Assert.Throws<OWLandingRefusedException>(() => OWLanding.Of(empty, new OWLandingOptions { Hops = -1 })).Option);
            Assert.AreEqual("size", Assert.Throws<OWLandingRefusedException>(() => OWLanding.Of(empty, new OWLandingOptions { Size = 0 })).Option);
            Assert.Throws<ArgumentNullException>(() => OWLanding.Of(null));
            Assert.Throws<ArgumentNullException>(() => OWLandingOptions.FromJson(null));

            Assert.AreEqual(24, OWLanding.Of(empty).Size);
            Assert.AreEqual(24, OWLanding.Of(empty, new OWLandingOptions()).Size);
            Assert.AreEqual(3, OWLanding.Of(empty, OWLandingOptions.FromJson(JObject.Parse(@"{""size"":3.0}"))).Size);
            Assert.AreEqual("size", Assert.Throws<OWLandingRefusedException>(() =>
                OWLandingOptions.FromJson(JObject.Parse(@"{""size"":true}"))).Option);
            Assert.AreEqual("size", Assert.Throws<OWLandingRefusedException>(() =>
                OWLandingOptions.FromJson(JObject.Parse(@"{""size"":3000000000}"))).Option, "past Int32 is refused here, by name");
            // An unknown key is refused before a bad value beside it is read.
            Assert.AreEqual("lens", Assert.Throws<OWLandingRefusedException>(() =>
                OWLandingOptions.FromJson(JObject.Parse(@"{""heads"":0,""lens"":""x""}"))).Option);
        }

        /// <summary>
        /// Through the graph reader: a guest who does not see a Character's place reads it as having none
        /// (<c>no_place</c>), and the owner, who does, reads it placed; the place itself is the owner's
        /// <c>nobody_here</c> no more. The landing reads exactly what the graph serves.
        /// </summary>
        [Test]
        public void ThroughTheGraphReader_APlaceTheVisitorMayNotSee_IsNoPlace()
        {
            var world = new List<JObject>
            {
                JObject.Parse(@"{""id"":""c1"",""type"":""character"",""name"":""Mara"",""location"":""l1""}"),
                JObject.Parse(@"{""id"":""l1"",""type"":""location"",""name"":""Hollow"",""hidden"":true}"),
            };
            var owner = OWLanding.Of(OWGraph.Read(world, OWLensReader.Owner(), new OWGraphOptions { Hidden = true }));
            CollectionAssert.AreEqual(new[] { "near_orphan:c1", "near_orphan:l1" }, owner.Gaps.Select(g => g.Kind + ":" + g.Id));
            var contributor = OWLanding.Of(OWGraph.Read(world, OWLensReader.Contributor()));
            CollectionAssert.AreEqual(new[] { "orphan:c1", "no_place:c1" }, contributor.Gaps.Select(g => g.Kind + ":" + g.Id));
            Assert.IsEmpty(contributor.Clusters, "a Character with no edge is no head");
        }

        // -- Reading the fixture ---------------------------------------------------------

        /// <summary>A case's graph as the graph it states: its nodes (id, type, known_of) and edges, in their order.</summary>
        private static OWWorldGraph GraphOf(JObject g)
        {
            var nodes = ((JArray)g["nodes"]).Cast<JObject>().Select(n => new OWGraphNode((string)n["id"], (string)n["type"],
                n["name"], null, null, new string[0], n["known_of"]?.Type == JTokenType.Boolean && (bool)n["known_of"])).ToList();
            var edges = ((JArray)g["edges"]).Cast<JObject>().Select(e => new OWGraphEdge((string)e["source"], (string)e["field"],
                (string)e["target"], (int)e["index"])).ToList();
            return new OWWorldGraph(nodes, edges, new Dictionary<string, int>(), new Dictionary<string, int>(), null);
        }

        /// <summary>A case's options as JSON, or null when it gives none.</summary>
        private static OWLandingOptions OptionsOf(JObject c) =>
            c["options"] is JObject o ? OWLandingOptions.FromJson(o) : null;

        private static string GraphJson(OWWorldGraph g) =>
            new JObject
            {
                ["nodes"] = new JArray(g.Nodes.Select(n => (object)n.ToJson())),
                ["edges"] = new JArray(g.Edges.Select(e => (object)e.ToJson())),
            }.ToString(Formatting.None);
    }
}
