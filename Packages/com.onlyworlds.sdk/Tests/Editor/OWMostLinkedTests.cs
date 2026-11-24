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
    /// <see cref="OWMostLinked.Of"/>, the most linked elements of each type: a draft reading beside the landing profile,
    /// held to the packages' own fixture (<c>Fixtures/most-linked/most-linked.fixture.json</c>, the same bytes as the
    /// TypeScript SDK's <c>test/fixtures/most-linked.fixture.json</c> and the Python package's copy, its sha256 pinned
    /// here), replayed whole, case by case by name: each answer compared byte for byte, key order included, as the
    /// TypeScript suite compares it. Ported from @onlyworlds/sdk 4.203.0 (fc4d560), test/most-linked.test.mjs.
    /// </summary>
    public class OWMostLinkedTests
    {
        /// <summary>The fixture's sha256, as the TypeScript and Python suites hold it.</summary>
        private const string Sha256 = "9a7b27d849cd1c268d86b754bcdb7954cb23b3ede85178023332d61e497489c4";

        private const int CaseCount = 18;

        private static JObject _fixture;

        private static string FixturePath
        {
            get
            {
                var root = TestPackage.Root();
                return root == null ? null : Path.Combine(root, "Tests", "Editor", "Fixtures", "most-linked", "most-linked.fixture.json");
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
                yield return new TestCaseData(string.Empty).SetName("MostLinked_FixtureNotFound");
                yield break;
            }

            var i = 0;
            foreach (var c in fixture["cases"].Cast<JObject>())
                yield return new TestCaseData((string)c["name"]).SetName($"MostLinked_{++i:D2}");
        }

        [Test]
        public void TheFixture_IsTheSharedBytes()
        {
            var path = FixturePath;
            Assert.IsTrue(path != null && File.Exists(path), "the vendored most-linked fixture was not found: " + path);
            using var sha = SHA256.Create();
            var hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
            Assert.AreEqual(Sha256, hash,
                "the vendored fixture is not the packages' bytes: re-vendor it (a checkout's line endings change the bytes)");
            Assert.AreEqual("most-linked", (string)Fixture["profile"]);
            var names = Fixture["cases"].Select(c => (string)c["name"]).ToList();
            Assert.AreEqual(CaseCount, names.Count, $"the fixture holds {CaseCount} cases");
            Assert.AreEqual(names.Count, names.Distinct().Count(), "a case is found by its name, so each name is once");
        }

        [TestCaseSource(nameof(Cases))]
        public void EveryCase_IsAnsweredAsTheFixtureSays(string name)
        {
            Assert.IsNotNull(Fixture, "the vendored most-linked fixture was not found");
            var c = Fixture["cases"].Cast<JObject>().Single(x => (string)x["name"] == name);
            var graph = GraphOf((JObject)c["graph"]);
            var want = (JObject)c["want"];

            if (want.TryGetValue("refused", out var refused))
            {
                var ex = Assert.Throws<OWLandingRefusedException>(() => OWMostLinked.Of(graph, OptionsOf(c)), name + ": not refused");
                Assert.AreEqual((string)refused, ex.Option, $"{name}: refused as {ex.Message}");
                StringAssert.Contains((string)refused, ex.Message, name + ": the message names the option");
                return;
            }

            // Byte for byte, key order included.
            Assert.AreEqual(want.ToString(Formatting.None), OWMostLinked.Of(graph, OptionsOf(c)).ToJson().ToString(Formatting.None), name);
        }

        [Test]
        public void NoOptions_AndEmptyOptions_AreTheDefault()
        {
            var c = Fixture["cases"].Cast<JObject>().First();
            var graph = GraphOf((JObject)c["graph"]);
            var want = c["want"].ToString(Formatting.None);
            Assert.AreEqual(want, OWMostLinked.Of(graph).ToJson().ToString(Formatting.None));
            Assert.AreEqual(want, OWMostLinked.Of(graph, null).ToJson().ToString(Formatting.None));
            Assert.AreEqual(want, OWMostLinked.Of(graph, new OWMostLinkedOptions()).ToJson().ToString(Formatting.None));
        }

        /// <summary>No graph is no reading; the typed option is refused as the JSON one is, by name, below 1 and past 2^53 - 1.</summary>
        [Test]
        public void NoGraph_AndATypedPerTypeOutOfRange_AreRefused()
        {
            var empty = GraphOf(JObject.Parse(@"{""nodes"":[],""edges"":[]}"));
            Assert.Throws<ArgumentNullException>(() => OWMostLinked.Of(null));
            Assert.Throws<ArgumentNullException>(() => OWMostLinkedOptions.FromJson(null));
            Assert.AreEqual("per_type", Assert.Throws<OWLandingRefusedException>(() => OWMostLinked.Of(empty, new OWMostLinkedOptions { PerType = 0 })).Option);
            Assert.AreEqual("per_type", Assert.Throws<OWLandingRefusedException>(() =>
                OWMostLinked.Of(empty, new OWMostLinkedOptions { PerType = OWMostLinkedOptions.MaxPerType + 1 })).Option);
            Assert.AreEqual(OWMostLinkedOptions.MaxPerType, OWMostLinked.Of(empty, new OWMostLinkedOptions { PerType = OWMostLinkedOptions.MaxPerType }).PerType);
            Assert.AreEqual(1, OWMostLinked.Of(empty, new OWMostLinkedOptions { PerType = 1 }).PerType);
            // An unknown key is refused before a bad value beside it is read.
            Assert.AreEqual("lens", Assert.Throws<OWLandingRefusedException>(() =>
                OWMostLinkedOptions.FromJson(JObject.Parse(@"{""per_type"":0,""lens"":""x""}"))).Option);
        }

        [Test]
        public void TheGraph_IsReadAndNotChanged()
        {
            foreach (var c in Fixture["cases"].Cast<JObject>())
            {
                if (c["want"]["refused"] != null) continue;
                var graph = GraphOf((JObject)c["graph"]);
                var before = GraphJson(graph);
                var once = OWMostLinked.Of(graph, OptionsOf(c)).ToJson().ToString(Formatting.None);
                var twice = OWMostLinked.Of(graph, OptionsOf(c)).ToJson().ToString(Formatting.None);
                Assert.AreEqual(once, twice, (string)c["name"]);
                Assert.AreEqual(before, GraphJson(graph), (string)c["name"] + ": the graph moved");
            }
        }

        /// <summary>
        /// Through the graph reader: a hidden element and map space are not counted for a reader who is not shown them,
        /// and a graph that brings the hidden element in counts it (the control, so the first reading can see it).
        /// </summary>
        [Test]
        public void ThroughTheGraphReader_AHiddenElementAndMapSpace_AreNotCounted()
        {
            var world = new List<JObject>
            {
                JObject.Parse(@"{""id"":""c1"",""type"":""character"",""name"":""Ada"",""friends"":[""c2""]}"),
                JObject.Parse(@"{""id"":""c2"",""type"":""character"",""name"":""Brin""}"),
                JObject.Parse(@"{""id"":""sec"",""type"":""event"",""name"":""The Secret"",""characters"":[""c1"",""c2""],""hidden"":true}"),
                JObject.Parse(@"{""id"":""m1"",""type"":""map"",""name"":""Chart""}"),
                JObject.Parse(@"{""id"":""p1"",""type"":""pin"",""name"":""Pin"",""map"":""m1"",""element_type"":""character"",""element_id"":""c1""}"),
            };
            var got = OWMostLinked.Of(OWGraph.Read(world, OWLensReader.Owner()));
            Assert.AreEqual(
                @"{""per_type"":3,""by_type"":{""character"":[{""id"":""c1"",""name"":""Ada"",""degree"":1},{""id"":""c2"",""name"":""Brin"",""degree"":1}]}}",
                got.ToJson().ToString(Formatting.None));
            var shown = OWMostLinked.Of(OWGraph.Read(world, OWLensReader.Owner(), new OWGraphOptions { Hidden = true }));
            Assert.AreEqual(@"[{""id"":""sec"",""name"":""The Secret"",""degree"":2}]", shown.ToJson()["by_type"]["event"].ToString(Formatting.None));
            Assert.AreEqual(2, shown.ByType["character"][0].Degree);
        }

        // -- Reading the fixture ---------------------------------------------------------

        /// <summary>A case's graph as the graph it states: its nodes (id, type, name, known_of) and edges, in their order.</summary>
        private static OWWorldGraph GraphOf(JObject g)
        {
            var nodes = ((JArray)g["nodes"]).Cast<JObject>().Select(n => new OWGraphNode((string)n["id"], (string)n["type"],
                n["name"], null, null, new string[0], n["known_of"]?.Type == JTokenType.Boolean && (bool)n["known_of"])).ToList();
            var edges = ((JArray)g["edges"]).Cast<JObject>().Select(e => new OWGraphEdge((string)e["source"], (string)e["field"],
                (string)e["target"], (int)e["index"])).ToList();
            return new OWWorldGraph(nodes, edges, new Dictionary<string, int>(), new Dictionary<string, int>(), null);
        }

        /// <summary>A case's options as JSON, or null when it gives none.</summary>
        private static OWMostLinkedOptions OptionsOf(JObject c) =>
            c["options"] is JObject o ? OWMostLinkedOptions.FromJson(o) : null;

        private static string GraphJson(OWWorldGraph g) =>
            new JObject
            {
                ["nodes"] = new JArray(g.Nodes.Select(n => (object)n.ToJson())),
                ["edges"] = new JArray(g.Edges.Select(e => (object)e.ToJson())),
            }.ToString(Formatting.None);
    }
}
