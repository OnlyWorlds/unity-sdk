using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The lens profile's graph section: <see cref="OWGraph.Read"/> on the graph kind of the lens fixture (schema-dist
    /// <c>profiles/lens.fixture.json</c>, vendored byte for byte in <c>Tests/Editor/Fixtures/lens/</c> from dist.96, its
    /// hash held by <see cref="OWLensTests"/>), each answer held to the section's invariants, the fixture's graph tables
    /// read back through the reader, and the TypeScript SDK's own vectors (<c>test/graph.test.mjs</c>, release 4.93.0)
    /// where C# can state them. The TypeScript SDK and the Python package (<c>world_graph</c>, 0.77.0) replay the same
    /// cases.
    /// </summary>
    /// <remarks>
    /// A graph case carries its own world: its elements as the owner is served them, a hidden one by its own
    /// <c>hidden: true</c> or, in two cases, by the world's <c>hidden</c> list, which the world handed to the reader here
    /// carries as the element's mark, as <see cref="OWLensTests"/> does. The fixture writes the options in the Python
    /// package's words (<c>map_space</c>); a reader's <c>as</c> is the owner reading as that reader
    /// (<see cref="OWLensReader.WithOwnerCounts"/>).
    /// </remarks>
    public class OWGraphTests
    {
        /// <summary>The fixture's graph cases, as dist.96 holds them (the 38th: one element of each of the 22 types; the 39th: an empty review mark, an object and a string).</summary>
        private const int GraphCount = 49;

        private static JObject _fixture;

        private static JObject Fixture
        {
            get
            {
                if (_fixture != null) return _fixture;
                var root = TestPackage.Root();
                var path = root == null ? null : Path.Combine(root, "Tests", "Editor", "Fixtures", "lens", "lens.fixture.json");
                return path == null || !File.Exists(path) ? null : _fixture = OWJson.ParseObject(File.ReadAllText(path));
            }
        }

        private static IEnumerable<JObject> GraphCases =>
            Fixture == null ? Enumerable.Empty<JObject>() : Fixture["cases"].Cast<JObject>().Where(c => (string)c["kind"] == "graph");

        /// <summary>
        /// A node's family and icon are the presentation sidecar's. Outside the Editor (a compile and test run with no
        /// engine) <c>Resources</c> loads nothing, so the package's own vendored sidecar is read from its text instead.
        /// </summary>
        [OneTimeSetUp]
        public void TheSidecar_IsRead()
        {
            string family = null;
            try
            {
                family = OWPresentation.FamilyOf("character");
            }
            catch (Exception)
            {
                // No engine behind Resources; read the text below.
            }

            if (family != null) return;
            var root = TestPackage.Root();
            if (root == null) return;
            _readFromText = true;
            try
            {
                OWPresentation.LoadText(File.ReadAllText(Path.Combine(root, "Runtime", "Resources", "ow-presentation.json")));
            }
            catch (Exception)
            {
                // The colours need the engine's parser; the type table is read before them.
            }
        }

        private static bool _readFromText;

        /// <summary>A sidecar read from its text is put back, so the next reader loads it as the package does.</summary>
        [OneTimeTearDown]
        public void TheSidecar_IsPutBack()
        {
            if (!_readFromText) return;
            _readFromText = false;
            OWPresentation.Reload();
        }

        /// <summary>One case per fixture graph case, named after it.</summary>
        public static IEnumerable<TestCaseData> Cases()
        {
            if (Fixture == null)
            {
                yield return new TestCaseData(string.Empty).SetName("Graph_FixtureNotFound");
                yield break;
            }

            var i = 0;
            foreach (var c in GraphCases)
                yield return new TestCaseData((string)c["name"]).SetName($"Graph_{++i:D2}");
        }

        [Test]
        public void TheFixture_HoldsDist88sGraphCases()
        {
            Assert.IsNotNull(Fixture, "the vendored lens fixture was not found");
            Assert.AreEqual(GraphCount, GraphCases.Count(), "dist.96's fixture holds 49 graph cases");
        }

        [TestCaseSource(nameof(Cases))]
        public void EveryGraphCase_IsAnsweredAsTheProfileSays(string name)
        {
            Assert.IsNotNull(Fixture, "the vendored lens fixture was not found");
            var c = GraphCases.Single(x => (string)x["name"] == name);
            var world = WorldOf((JObject)c["world"]);
            var reader = ReaderOf((JObject)c["reader"]);
            var options = OptionsOf(c);
            var want = (JObject)c["want"];

            if (want.TryGetValue("refused", out var refused))
            {
                var ex = Assert.Throws<OWGraphRefusedException>(() => OWGraph.Read(world, reader, options), name + ": not refused");
                Assert.AreEqual((string)refused, ex.Flag, $"{name}: refused as {ex.Message}");
                StringAssert.Contains((string)refused, ex.Message, name + ": the message names the flag");
                return;
            }

            var g = OWGraph.Read(world, reader, options);
            var got = g.ToJson();
            Assert.IsTrue(JToken.DeepEquals(want, got),
                $"{name}: gave {got.ToString(Formatting.None)}, want {want.ToString(Formatting.None)}");
            Invariants(g, c, world, reader, options);
        }

        [Test]
        public void TheGraphTables_AreTheFixtures()
        {
            Assert.IsNotNull(Fixture, "the vendored lens fixture was not found");
            var f = Fixture;
            CollectionAssert.AreEqual(Strings(f["graph_links"]), OWGraph.DeclaredLinks, "the declared links");
            CollectionAssert.AreEqual(new[] { "rooms", "hidden", "proposed", "map_space" }, Strings(f["graph_flags"]));
            CollectionAssert.AreEquivalent(Strings(f["graph_flags"]), OWGraph.Flags, "the flags");
            CollectionAssert.AreEqual(new[] { "hidden", "proposed" }, Strings(f["owner_only_flags"]));
            CollectionAssert.AreEquivalent(Strings(f["edges_cut_reasons"]), OWGraphCut.All, "the edges cut reasons");
            // The fixture's owner-only list names hidden, not_visible and anchor_not_visible; proposed is owner-only here
            // as in the TypeScript SDK and the Python package (no other rung sees a proposed element, so none counts one).
            CollectionAssert.IsSubsetOf(Strings(f["owner_only_edges_cut_reasons"]), OWGraphCut.OwnerOnly);
            CollectionAssert.AreEquivalent(new[] { OWGraphCut.Proposed },
                OWGraphCut.OwnerOnly.Except(Strings(f["owner_only_edges_cut_reasons"])), "owner-only beyond the fixture's list");
            var reasons = new HashSet<string>(Strings(f["edges_cut_reasons"]));
            foreach (var c in GraphCases)
            {
                foreach (var p in ((JObject)c["want"]["edges_cut"] ?? new JObject()).Properties())
                    Assert.IsTrue(reasons.Contains(p.Name), $"{c["name"]}: {p.Name}");
            }
        }

        /// <summary>The fixture's flags, read back through the reader: the owner may ask each; a contributor only those not the owner's; the public reader none.</summary>
        [Test]
        public void TheFlags_ReadBackThroughTheReader()
        {
            Assert.IsNotNull(Fixture, "the vendored lens fixture was not found");
            var ownerOnly = Strings(Fixture["owner_only_flags"]);
            foreach (var flag in Strings(Fixture["graph_flags"]))
            {
                var o = Flag(flag);
                Assert.DoesNotThrow(() => OWGraph.Read(new JArray(), OWLensReader.Owner(), o), flag);
                if (ownerOnly.Contains(flag))
                {
                    var ex = Assert.Throws<OWGraphRefusedException>(() => OWGraph.Read(new JArray(), OWLensReader.Contributor(), o), flag);
                    StringAssert.Contains("owner's alone", ex.Message, flag);
                }
                else
                {
                    Assert.DoesNotThrow(() => OWGraph.Read(new JArray(), OWLensReader.Contributor(), o), flag);
                }

                Assert.Throws<OWGraphRefusedException>(() => OWGraph.Read(new JArray(), OWLensReader.Public(), o), flag);
            }
        }

        /// <summary>The declared links are edges; another <c>x_</c> field is none.</summary>
        [Test]
        public void TheDeclaredLinks_AreEdges_AnotherXFieldIsNone()
        {
            foreach (var field in OWGraph.DeclaredLinks)
            {
                var world = new JArray
                {
                    new JObject { ["id"] = "a", ["type"] = "construct", ["name"] = "A", [field] = new JArray("b") },
                    new JObject { ["id"] = "b", ["type"] = "construct", ["name"] = "B" },
                };
                var g = OWGraph.Read(world, OWLensReader.Owner());
                Assert.AreEqual(1, g.Edges.Count, field);
                Assert.IsTrue(JToken.DeepEquals(new JObject { ["source"] = "a", ["field"] = field, ["target"] = "b", ["index"] = 0 },
                    g.Edges[0].ToJson()), field);
            }

            var other = new JArray
            {
                new JObject { ["id"] = "a", ["type"] = "construct", ["name"] = "A", ["x_other_links"] = new JArray("b") },
                new JObject { ["id"] = "b", ["type"] = "construct", ["name"] = "B" },
            };
            Assert.IsEmpty(OWGraph.Read(other, OWLensReader.Owner()).Edges);
        }

        /// <summary>
        /// For a target the lens cut, the lens's own reason wins over <c>not_visible</c>: a link to an anchor the reader
        /// cannot see is counted as the lens's <c>anchor_not_visible</c>, for the owner reading as a contributor, and not
        /// counted at all for the contributor itself. The fixture holds no such link.
        /// </summary>
        [Test]
        public void ALensCutReason_WinsOverNotVisible()
        {
            var world = new JArray
            {
                new JObject { ["id"] = "c1", ["type"] = "character", ["name"] = "Ada", ["friends"] = new JArray("h1") },
                new JObject { ["id"] = "h1", ["type"] = "character", ["name"] = "Heir", [OWPayload.Hidden] = true },
                new JObject
                {
                    ["id"] = "L", ["type"] = "construct", ["supertype"] = "Lens", ["characters"] = new JArray("c1", "h1"),
                    ["x_lens"] = new JObject { ["depth"] = 0 },
                },
            };
            var options = new OWGraphOptions { LensId = "L" };
            var asContributor = OWGraph.Read(world, OWLensReader.Contributor().WithOwnerCounts(), options);
            CollectionAssert.AreEqual(new[] { "c1" }, asContributor.Nodes.Select(n => n.Id));
            Assert.IsTrue(JToken.DeepEquals(new JObject { ["anchor_not_visible"] = 1 }, asContributor.ToJson()["edges_cut"]),
                asContributor.ToJson().ToString(Formatting.None));
            var contributor = OWGraph.Read(world, OWLensReader.Contributor(), options);
            Assert.IsEmpty(contributor.EdgesCut, contributor.ToJson().ToString(Formatting.None));
        }

        /// <summary>A refused flag is refused before the world is read: the world here throws the moment it is enumerated.</summary>
        [Test]
        public void ARefusedFlag_IsRefusedBeforeAnythingIsRead()
        {
            var world = new Unread();
            StringAssert.Contains("hidden is the owner's alone", Assert.Throws<OWGraphRefusedException>(() =>
                OWGraph.Read(world, OWLensReader.Contributor(), new OWGraphOptions { Hidden = true })).Message);
            StringAssert.Contains("proposed is the owner's alone", Assert.Throws<OWGraphRefusedException>(() =>
                OWGraph.Read(world, OWLensReader.Guest(new string[0]), new OWGraphOptions { Proposed = true })).Message);
            StringAssert.Contains("public reader takes no flag; it asked rooms", Assert.Throws<OWGraphRefusedException>(() =>
                OWGraph.Read(world, OWLensReader.Public(), new OWGraphOptions { Rooms = true })).Message);
            StringAssert.Contains("it asked map_space", Assert.Throws<OWGraphRefusedException>(() =>
                OWGraph.Read(world, OWLensReader.Public(), new OWGraphOptions { MapSpace = true })).Message);
            StringAssert.Contains("it asked rooms", Assert.Throws<OWGraphRefusedException>(() =>
                OWGraph.Read(world, OWLensReader.Public().WithOwnerCounts(), new OWGraphOptions { Rooms = true })).Message);
            // The same world read without a refused flag does read the elements.
            Assert.Throws<Unread.ReadException>(() => OWGraph.Read(world, OWLensReader.Owner(), new OWGraphOptions { Hidden = true }));
        }

        /// <summary>
        /// What C# can say of a call that is not a reading: no reader, and a lens given both as a body and as an id. (The
        /// TypeScript SDK's unknown option, flag that is not a boolean and fractional <c>asOf</c> are not expressible
        /// through <see cref="OWGraphOptions"/>.) A public reader that asks no flag reads an empty world as an empty graph.
        /// </summary>
        [Test]
        public void WhatIsNotAReading_IsRefused()
        {
            Assert.Throws<ArgumentNullException>(() => OWGraph.Read(new JArray(), null));
            var both = new OWGraphOptions { Lens = new JObject { ["type"] = "construct", ["supertype"] = "Lens" }, LensId = "L1" };
            var ex = Assert.Throws<ArgumentException>(() => OWGraph.Read(new JArray(), OWLensReader.Owner(), both));
            Assert.IsNotInstanceOf<OWGraphRefusedException>(ex);
            var empty = OWGraph.Read(new JArray(), OWLensReader.Public(), new OWGraphOptions { Rooms = false, MapSpace = false });
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse("{\"nodes\":[],\"edges\":[],\"edges_cut\":{},\"cut\":{},\"unreadable\":null}"),
                empty.ToJson()), empty.ToJson().ToString(Formatting.None));
            Assert.IsTrue(empty.IsReadable);
        }

        // -- DegreeOf (the TypeScript SDK 4.95.0's degreeOf, the Python package 0.78.0's degree_of) --------------------

        /// <summary>
        /// The TypeScript SDK 4.95.0's vector (<c>test/graph.test.mjs</c>): each node's edges in or out, a mirror pair two
        /// at each end, a link to itself once, every node a key with 0 included, in the graph's node order. The two ids
        /// that are inherited keys in JavaScript are kept: here they are plain ids. Kills: a self-link counted twice, an
        /// edge counted at one end only, a node with no edge left out, the keys in another order than the nodes'.
        /// </summary>
        [Test]
        public void DegreeOf_CountsEachNodesEdges_AMirrorPairTwo_ALinkToItselfOnce_EveryNodeAKey()
        {
            var world = new List<JObject>
            {
                JObject.Parse(@"{""id"":""c1"",""type"":""character"",""name"":""Ada"",""friends"":[""c2"",""c1""]}"),
                JObject.Parse(@"{""id"":""c2"",""type"":""character"",""name"":""Bo"",""friends"":[""c1""]}"),
                JObject.Parse(@"{""id"":""r1"",""type"":""relation"",""name"":""Pact"",""characters"":[""c1"",""c2"",""c3""]}"),
                JObject.Parse(@"{""id"":""c3"",""type"":""character"",""name"":""Cy""}"),
                JObject.Parse(@"{""id"":""o1"",""type"":""object"",""name"":""Lamp""}"),
                JObject.Parse(@"{""id"":""constructor"",""type"":""object"",""name"":""An id like an inherited key""}"),
                JObject.Parse(@"{""id"":""__proto__"",""type"":""character"",""name"":""Another"",""objects"":[""constructor""]}"),
            };

            var g = OWGraph.Read(world, OWLensReader.Owner());
            var d = OWGraph.DegreeOf(g);
            CollectionAssert.AreEqual(g.Nodes.Select(n => n.Id).ToList(), d.Keys.ToList(), "every node a key, in the nodes' order");
            Assert.AreEqual(4, d["c1"], "c1->c2, c2->c1, c1->c1 once, r1->c1");
            Assert.AreEqual(3, d["c2"]);
            Assert.AreEqual(3, d["r1"]);
            Assert.AreEqual(1, d["c3"]);
            Assert.AreEqual(0, d["o1"]);
            Assert.AreEqual(1, d["constructor"]);
            Assert.AreEqual(1, d["__proto__"]);
            Assert.AreEqual(g.Nodes.Count, d.Count);
            Assert.Throws<ArgumentNullException>(() => OWGraph.DegreeOf(null));
        }

        /// <summary>
        /// The TypeScript SDK 4.95.0's second vector: on every fixture graph case's answer (its <c>want</c>, built as the
        /// graph it states), each node's degree equals a direct count of the edges that touch it. Kills: a count that
        /// reads anything but the graph's own edges.
        /// </summary>
        [Test]
        public void DegreeOf_OnEveryFixtureGraph_IsADirectCountOfItsEdges()
        {
            Assert.IsNotNull(Fixture, "the vendored lens fixture was not found");
            var checkedCases = 0;
            foreach (var c in GraphCases)
            {
                var want = (JObject)c["want"];
                if (!(want["nodes"] is JArray nodes)) continue;
                var edges = (JArray)want["edges"];
                var graph = new OWWorldGraph(
                    nodes.Cast<JObject>().Select(n => new OWGraphNode((string)n["id"], (string)n["type"], (string)n["name"],
                        (string)n["family"], (string)n["icon"], Strings(n["marks"]), n["known_of"]?.Type == JTokenType.Boolean && (bool)n["known_of"])).ToList(),
                    edges.Cast<JObject>().Select(e => new OWGraphEdge((string)e["source"], (string)e["field"], (string)e["target"],
                        (int)e["index"])).ToList(),
                    new Dictionary<string, int>(), new Dictionary<string, int>(), null);

                var d = OWGraph.DegreeOf(graph);
                foreach (var n in nodes.Cast<JObject>())
                {
                    var id = (string)n["id"];
                    var direct = edges.Cast<JObject>().Count(e => (string)e["source"] == id || (string)e["target"] == id);
                    Assert.AreEqual(direct, d[id], $"{c["name"]}: {id}");
                }

                checkedCases++;
            }

            Assert.Greater(checkedCases, 1);
        }

        /// <summary>
        /// The room mark's five shapes (<see cref="OWRoomTests.MarkShapes"/>) through the unlensed graph: the owner's
        /// graph leaves a room element out and its <c>rooms</c> flag brings it back marked, and the public reader's view
        /// never holds it. Kills: a reader of the graph that reads the mark apart from <see cref="OWRoom.IsRoomElement"/>.
        /// </summary>
        [TestCaseSource(typeof(OWRoomTests), nameof(OWRoomTests.MarkShapes))]
        public void TheRoomMark_CountsByPresence_InTheGraph(string markJson, bool marks)
        {
            var world = new List<JObject>
            {
                JObject.Parse(@"{""id"":""c1"",""type"":""character"",""name"":""Ada""}"),
                JObject.Parse(@"{""id"":""m1"",""type"":""narrative"",""name"":""A line"",""supertype"":""Message"",""characters"":[""c1""]}"),
            };
            world[1][OWRoom.MarkField] = JToken.Parse(markJson);

            Assert.AreEqual(!marks, OWGraph.Read(world, OWLensReader.Owner()).Nodes.Any(n => n.Id == "m1"),
                "the owner's graph leaves a room element out");
            var flagged = OWGraph.Read(world, OWLensReader.Owner(), new OWGraphOptions { Rooms = true }).Nodes.Single(n => n.Id == "m1");
            Assert.AreEqual(marks, flagged.Marks.Contains(OWGraphMark.Room), "the rooms flag marks a room element");
            Assert.AreEqual(!marks, OWGraph.Read(world, OWLensReader.Public()).Nodes.Any(n => n.Id == "m1"),
                "the public reader's view leaves a room element out");
        }

        // -- OutDegreeOf (the TypeScript SDK 4.98.0's outDegreeOf, the Python package 0.80.0's out_degree_of) ------------

        /// <summary>
        /// The TypeScript SDK 4.98.0's vector (<c>test/graph.test.mjs</c>): each node's distinct targets among its own
        /// links, two fields naming one target once, a link to itself none, a mirror pair 1 at each end, every node a key
        /// with 0 included, in the graph's node order. Kills: an edge counted per field, a self-link counted, a target
        /// counted at the target's end, a node with no edge left out, the keys in another order than the nodes'.
        /// </summary>
        [Test]
        public void OutDegreeOf_CountsDistinctTargets_TwoFieldsToOneTargetOnce_ALinkToItselfNone_AMirrorPairOneEach()
        {
            var world = new List<JObject>
            {
                JObject.Parse(@"{""id"":""c1"",""type"":""character"",""name"":""Ada"",""friends"":[""c2"",""c1""],""rivals"":[""c2""]}"),
                JObject.Parse(@"{""id"":""c2"",""type"":""character"",""name"":""Bo"",""friends"":[""c1""]}"),
                JObject.Parse(@"{""id"":""r1"",""type"":""relation"",""name"":""Pact"",""characters"":[""c1"",""c2"",""c3""]}"),
                JObject.Parse(@"{""id"":""c3"",""type"":""character"",""name"":""Cy""}"),
                JObject.Parse(@"{""id"":""o1"",""type"":""object"",""name"":""Lamp""}"),
                JObject.Parse(@"{""id"":""constructor"",""type"":""object"",""name"":""An id like an inherited key""}"),
                JObject.Parse(@"{""id"":""__proto__"",""type"":""character"",""name"":""Another"",""objects"":[""constructor""]}"),
            };

            var g = OWGraph.Read(world, OWLensReader.Owner());
            var d = OWGraph.OutDegreeOf(g);
            CollectionAssert.AreEqual(g.Nodes.Select(n => n.Id).ToList(), d.Keys.ToList(), "every node a key, in the nodes' order");
            Assert.AreEqual(1, d["c1"], "friends and rivals both name c2: once; c1 itself: none");
            Assert.AreEqual(1, d["c2"], "the mirror of c1's c2");
            Assert.AreEqual(3, d["r1"]);
            Assert.AreEqual(0, d["c3"]);
            Assert.AreEqual(0, d["o1"]);
            Assert.AreEqual(0, d["constructor"]);
            Assert.AreEqual(1, d["__proto__"]);
            Assert.AreEqual(g.Nodes.Count, d.Count);
            Assert.Throws<ArgumentNullException>(() => OWGraph.OutDegreeOf(null));
        }

        /// <summary>
        /// The TypeScript SDK 4.98.0's second vector: on every fixture graph case's answer (its <c>want</c>, built as the
        /// graph it states), each node's out-degree equals a direct count of the distinct targets of the edges it is the
        /// source of, a link to itself left out. Kills: a count that reads anything but the graph's own edges.
        /// </summary>
        [Test]
        public void OutDegreeOf_OnEveryFixtureGraph_IsADirectCountOfDistinctTargets()
        {
            Assert.IsNotNull(Fixture, "the vendored lens fixture was not found");
            var checkedCases = 0;
            foreach (var c in GraphCases)
            {
                var want = (JObject)c["want"];
                if (!(want["nodes"] is JArray nodes)) continue;
                var edges = (JArray)want["edges"];
                var graph = new OWWorldGraph(
                    nodes.Cast<JObject>().Select(n => new OWGraphNode((string)n["id"], (string)n["type"], (string)n["name"],
                        (string)n["family"], (string)n["icon"], Strings(n["marks"]), n["known_of"]?.Type == JTokenType.Boolean && (bool)n["known_of"])).ToList(),
                    edges.Cast<JObject>().Select(e => new OWGraphEdge((string)e["source"], (string)e["field"], (string)e["target"],
                        (int)e["index"])).ToList(),
                    new Dictionary<string, int>(), new Dictionary<string, int>(), null);

                var d = OWGraph.OutDegreeOf(graph);
                foreach (var n in nodes.Cast<JObject>())
                {
                    var id = (string)n["id"];
                    var direct = edges.Cast<JObject>()
                        .Where(e => (string)e["source"] == id && (string)e["target"] != id)
                        .Select(e => (string)e["target"]).Distinct().Count();
                    Assert.AreEqual(direct, d[id], $"{c["name"]}: {id}");
                }

                checkedCases++;
            }

            Assert.Greater(checkedCases, 1);
        }

        /// <summary>
        /// The served reader (this SDK's own rung): a contributor's served bodies, read as they are, give the contributor's
        /// graph, in every unlensed contributor case.
        /// </summary>
        [Test]
        public void TheServedReader_OnAContributorsBodies_GraphsAsTheContributor()
        {
            Assert.IsNotNull(Fixture, "the vendored lens fixture was not found");
            var checkedCases = 0;
            foreach (var c in GraphCases)
            {
                var r = (JObject)c["reader"];
                if ((string)r["rung"] != "contributor" || c["want"]["refused"] != null) continue;
                var options = OptionsOf(c);
                if (options.Lens != null || options.LensId != null) continue;
                var world = WorldOf((JObject)c["world"]);
                var contributor = ReaderOf(r);
                var served = OWLens.ServedView(world, contributor);
                var want = OWGraph.Read(world, contributor, options).ToJson();
                var got = OWGraph.Read(served, OWLensReader.Served(contributor.Character), options).ToJson();
                Assert.IsTrue(JToken.DeepEquals(want, got),
                    $"{c["name"]}: served {got.ToString(Formatting.None)}, contributor {want.ToString(Formatting.None)}");
                checkedCases++;
            }

            Assert.Greater(checkedCases, 1);
        }

        // -- The invariants --------------------------------------------------------------

        /// <summary>The section's invariants on one answer, as the TypeScript SDK's replay checks them.</summary>
        private static void Invariants(OWWorldGraph g, JObject c, List<JObject> world, OWLensReader reader, OWGraphOptions options)
        {
            var name = (string)c["name"];
            var ids = g.Nodes.Select(n => n.Id).ToList();
            Assert.AreEqual(ids.Count, ids.Distinct().Count(), name + ": one node per id");
            var nodes = new HashSet<string>(ids);
            var keys = g.Edges.Select(e => $"{e.Source}\u0000{e.Field}\u0000{e.Target}\u0000{e.Index}").ToList();
            Assert.AreEqual(keys.Count, keys.Distinct().Count(), name + ": one edge per (source, field, target, index)");
            foreach (var e in g.Edges)
                Assert.IsTrue(nodes.Contains(e.Source) && nodes.Contains(e.Target), name + ": an edge between nodes only");
            foreach (var n in g.Nodes.Where(n => n.KnownOf))
                Assert.IsFalse(g.Edges.Any(e => e.Source == n.Id), name + ": a known-of node has no edge out");

            var cutKeys = g.Cut.Where(p => p.Value > 0).Select(p => p.Key).ToList();
            var edgeKeys = g.EdgesCut.Where(p => p.Value > 0).Select(p => p.Key).ToList();
            if (reader.Rung == OWLensRung.Public)
            {
                Assert.IsFalse(g.Nodes.Any(n => n.Marks.Count > 0), name + ": a public graph holds no room, hidden or proposed node");
                foreach (var k in new[] { "room", "hidden", "proposed" })
                    Assert.IsFalse(cutKeys.Contains(k) || edgeKeys.Contains(k), $"{name}: a public graph has no count of {k}");
            }

            if (!reader.OwnerCounts)
            {
                foreach (var k in new[] { "hidden", "proposed", "not_visible", "anchor_not_visible" })
                    Assert.IsFalse(cutKeys.Contains(k) || edgeKeys.Contains(k), $"{name}: a reader that is not the owner has no count of {k}");
            }

            Assert.AreEqual(g.ToJson().ToString(Formatting.None), OWGraph.Read(world, reader, options).ToJson().ToString(Formatting.None),
                name + ": the same input gives the same bytes");

            if (options.Lens == null && options.LensId == null)
            {
                Assert.IsNull(g.RefusedKey, name + ": unreadable is null without a lens");
                return;
            }

            var lensOptions = new OWLensOptions { AsOf = options.AsOf, Now = options.Now, Clock = options.Clock, Members = options.Members };
            var r = options.Lens != null
                ? OWLens.Resolve(world, options.Lens, reader, lensOptions)
                : OWLens.Resolve(world, options.LensId, reader, lensOptions);
            CollectionAssert.AreEqual(r.Kept.OrderBy(i => i, StringComparer.Ordinal), ids.OrderBy(i => i, StringComparer.Ordinal),
                name + ": a lens's graph has exactly the lens's kept as its nodes");
            CollectionAssert.AreEquivalent(r.Cut.Where(p => p.Value > 0), g.Cut.Where(p => p.Value > 0), name + ": the lens's cut rides along unchanged");
            Assert.AreEqual(r.RefusedKey, g.RefusedKey, name + ": unreadable is the lens reading's");

            // Every link from a kept node to an element in the world is an edge or counted, read as the owner (the same
            // view, the owner's counts); less the links the view leaves out, so is every link of the reader's own.
            var ownerView = reader.OwnerCounts ? reader : reader.WithOwnerCounts();
            var go = OWGraph.Read(world, ownerView, new OWGraphOptions
            {
                Lens = options.Lens, LensId = options.LensId, AsOf = options.AsOf, Now = options.Now, Clock = options.Clock,
                Members = options.Members,
            });
            var inWorld = new HashSet<string>(world.Select(e => (string)e["id"]));
            int Linked(JToken v) => v is JValue s && s.Type == JTokenType.String && inWorld.Contains((string)s) ? 1 : 0;
            var links = 0;
            foreach (var n in go.Nodes.Where(n => !n.KnownOf))
            {
                var el = world.First(x => (string)x["id"] == n.Id);
                foreach (var (field, many) in OWLens.LinkFieldsOf(n.Type))
                {
                    if (!many) links += Linked(el[field]);
                    else if (el[field] is JArray list) links += list.Sum(Linked);
                }

                foreach (var field in new[] { "x_lens_leads_to", "x_spine_depends_on" })
                {
                    if (el[field] is JArray list) links += list.Sum(Linked);
                }
            }

            var unseen = new[] { "not_visible", "anchor_not_visible" };
            Assert.AreEqual(links, go.Edges.Count + go.EdgesCut.Values.Sum(), name + ": every link from a kept node is an edge or counted");
            // The reader's own links are those to an element it sees (a proposed one too, below the public rung: its link
            // is served, its edge cut). Counts are per reader: an element the owner counts anchor_not_visible but the reader sees (an id
            // under a field of another type) is the reader's outside_lens or a child's reason, never left uncounted.
            var sees = new HashSet<string>(OWLens.ServedView(world, reader).Select(b => (string)b["id"]));
            if (reader.Rung != OWLensRung.Public) sees.UnionWith(world.Where(OWPayload.IsProposedOrInDoubt).Select(e => (string)e["id"]));
            var inView = 0;
            foreach (var n in go.Nodes.Where(n => !n.KnownOf))
            {
                var el = world.First(x => (string)x["id"] == n.Id);
                int Seen(JToken v) => Linked(v) == 1 && sees.Contains((string)v) ? 1 : 0;
                foreach (var (field, many) in OWLens.LinkFieldsOf(n.Type))
                {
                    if (!many) inView += Seen(el[field]);
                    else if (el[field] is JArray list) inView += list.Sum(Seen);
                }

                foreach (var field in new[] { "x_lens_leads_to", "x_spine_depends_on" })
                {
                    if (el[field] is JArray list) inView += list.Sum(Seen);
                }
            }

            Assert.AreEqual(inView, g.Edges.Count + g.EdgesCut.Where(p => !unseen.Contains(p.Key)).Sum(p => p.Value),
                name + ": every link from a kept node to an element the reader sees is an edge or counted");
        }

        // -- The fixture's words ---------------------------------------------------------

        /// <summary>The case world's elements, each the world's <c>hidden</c> list names carrying the hidden mark.</summary>
        private static List<JObject> WorldOf(JObject fixtureWorld)
        {
            var hidden = new HashSet<string>(Strings(fixtureWorld["hidden"]));
            var list = new List<JObject>();
            foreach (var e in fixtureWorld["elements"].Cast<JObject>())
            {
                var copy = (JObject)e.DeepClone();
                if (hidden.Contains((string)copy["id"])) copy[OWPayload.Hidden] = true;
                list.Add(copy);
            }

            return list;
        }

        /// <summary>A case reader: its rung, its Character, a guest's lists; <c>as</c> is the owner reading as that reader.</summary>
        internal static OWLensReader ReaderOf(JObject r)
        {
            if (r["as"] is JObject asReader) return ReaderOf(asReader).WithOwnerCounts();
            var character = r["character"]?.Type == JTokenType.String ? (string)r["character"] : null;
            switch ((string)r["rung"])
            {
                case "owner": return OWLensReader.Owner(character);
                case "contributor": return OWLensReader.Contributor(character);
                case "public": return OWLensReader.Public();
                case "guest": return OWLensReader.Guest(Strings(r["whole"]), Strings(r["known_of"]), character);
                default: throw new ArgumentException("a fixture reader of unknown rung: " + r["rung"]);
            }
        }

        /// <summary>A case's options in <see cref="OWGraphOptions"/>' words; the world's members ride along for the mail rule.</summary>
        internal static OWGraphOptions OptionsOf(JObject c)
        {
            var o = c["options"] as JObject ?? new JObject();
            bool On(string key) => o[key]?.Type == JTokenType.Boolean && (bool)o[key];
            var lens = o["lens"];
            return new OWGraphOptions
            {
                Lens = lens as JObject,
                LensId = lens?.Type == JTokenType.String ? (string)lens : null,
                AsOf = o["as_of"]?.Type == JTokenType.Integer ? (int?)o["as_of"] : null,
                Clock = o["clock"]?.Type == JTokenType.String
                    ? DateTimeOffset.Parse((string)o["clock"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)
                    : (DateTimeOffset?)null,
                Members = c["world"]["members"] as JArray,
                Rooms = On("rooms"),
                Hidden = On("hidden"),
                Proposed = On("proposed"),
                MapSpace = On("map_space"),
            };
        }

        // -- The lens profile owner's rulings on the graph section's silences (each a case in its next serial) ----------
        // G1 (an edge to an anchor the reader cannot see carries the lens's anchor_not_visible) was already this reader's:
        // ALensCutReason_WinsOverNotVisible.

        /// <summary>Ruling G2: a Pin's generic pair is placement, never an edge; its map link is. Kills: an edge to the pinned element.</summary>
        [Test]
        public void RulingG2_APinsPairIsPlacement_NeverAnEdge()
        {
            var world = new JArray(
                JObject.Parse(@"{""id"":""m"",""type"":""map"",""name"":""Map""}"),
                JObject.Parse(@"{""id"":""p"",""type"":""pin"",""name"":""Pin"",""map"":""m"",""element_type"":""character"",""element_id"":""a""}"),
                JObject.Parse(@"{""id"":""a"",""type"":""character"",""name"":""Ada""}"));
            var g = OWGraph.Read(world, OWLensReader.Owner(), new OWGraphOptions { MapSpace = true });
            Assert.AreEqual(@"[{""source"":""p"",""field"":""map"",""target"":""m"",""index"":0}]",
                new JArray(g.Edges.Select(e => e.ToJson())).ToString(Formatting.None));
        }

        /// <summary>Ruling G3: a node's name is the stored value, null when absent, nothing invented. Kills: a number's name read as null.</summary>
        [Test]
        public void RulingG3_ANodesName_IsTheStoredValue()
        {
            var world = new JArray(
                JObject.Parse(@"{""id"":""a"",""type"":""character"",""name"":7}"),
                JObject.Parse(@"{""id"":""b"",""type"":""character""}"),
                JObject.Parse(@"{""id"":""c"",""type"":""character"",""name"":""""}"));
            var names = new JArray(OWGraph.Read(world, OWLensReader.Owner()).Nodes.Select(n => n.ToJson()["name"]));
            Assert.AreEqual(@"[7,null,""""]", names.ToString(Formatting.None));
        }

        /// <summary>Ruling G4: a node's type is as stored, never folded; its family is still read by the slug. Kills: the type folded.</summary>
        [Test]
        public void RulingG4_ANodesType_IsAsStored()
        {
            var node = OWGraph.Read(new JArray(JObject.Parse(@"{""id"":""a"",""type"":""Character"",""name"":""Ada""}")),
                OWLensReader.Owner()).Nodes.Single();
            Assert.AreEqual("Character", node.Type);
            Assert.AreEqual(OWPresentation.FamilyOf("character"), node.Family);
            // The packages' twin run met a padded type too: as stored, its padding kept.
            var padded = OWGraph.Read(new JArray(JObject.Parse(@"{""id"":""a"",""type"":"" character "",""name"":""Ada""}")),
                OWLensReader.Public()).Nodes.Single();
            Assert.AreEqual(" character ", padded.Type, "a public reader's node, as stored");
        }

        /// <summary>
        /// Ruling G5: an edge's index is the target's place in the link list as served to that reader; keel serves a
        /// contributor the link to a proposed element, so the index counts it. A hidden id is not served, and the public
        /// rung is served no proposed link. Kills: a contributor's index that skips the proposed link.
        /// </summary>
        [Test]
        public void RulingG5_AContributorsIndex_CountsAServedProposedLink()
        {
            var world = new JArray(
                JObject.Parse(@"{""id"":""a"",""type"":""character"",""name"":""Ada"",""friends"":[""h"",""px"",""b""]}"),
                JObject.Parse(@"{""id"":""h"",""type"":""character"",""name"":""Hal"",""hidden"":true}"),
                JObject.Parse(@"{""id"":""px"",""type"":""character"",""name"":""Pax"",""x_review_proposed"":{""by"":""m1""}}"),
                JObject.Parse(@"{""id"":""b"",""type"":""character"",""name"":""Brin""}"));
            Assert.AreEqual(1, OWGraph.Read(world, OWLensReader.Contributor()).Edges.Single().Index, "served [Pax, Brin]: Brin at 1");
            Assert.AreEqual(0, OWGraph.Read(world, OWLensReader.Public()).Edges.Single().Index, "the public rung: Brin at 0");
        }

        /// <summary>
        /// Ruling G5 on bodies already served: a contributor's served <c>friends</c> keeps the proposed id, whose body it is
        /// never served, so the served reader's list is the list as served and the index is the place in it. Kills: a
        /// served reader's index that counts only the bodies it was given.
        /// </summary>
        [Test]
        public void RulingG5_AServedReadersIndex_IsThePlaceInTheListAsGiven()
        {
            var world = new JArray(
                JObject.Parse(@"{""id"":""a"",""type"":""character"",""name"":""Ada"",""friends"":[""h"",""px"",""b""]}"),
                JObject.Parse(@"{""id"":""h"",""type"":""character"",""name"":""Hal"",""hidden"":true}"),
                JObject.Parse(@"{""id"":""px"",""type"":""character"",""name"":""Pax"",""x_review_proposed"":{""by"":""m1""}}"),
                JObject.Parse(@"{""id"":""b"",""type"":""character"",""name"":""Brin""}"));
            var served = OWLens.ServedView(world, OWLensReader.Contributor());
            CollectionAssert.AreEqual(new[] { "px", "b" }, Strings(served.Single(e => (string)e["id"] == "a")["friends"]),
                "the contributor is served [Pax, Brin], Pax's body not");
            Assert.AreEqual(1, OWGraph.Read(served, OWLensReader.Served()).Edges.Single().Index, "served [Pax, Brin]: Brin at 1");
        }

        /// <summary>
        /// A public reader's served link lists leave a proposed id out, as keel's wire serves the public rung, so the
        /// index has no gap: a visible target after a proposed one is at 0. Kills: a public list that keeps the proposed
        /// id, or an index that counts it.
        /// </summary>
        [Test]
        public void APublicReadersServedList_LeavesAProposedIdOut_AndItsIndexHasNoGap()
        {
            var world = new JArray(
                JObject.Parse(@"{""id"":""a"",""type"":""character"",""name"":""Ada"",""friends"":[""px"",""b""]}"),
                JObject.Parse(@"{""id"":""px"",""type"":""character"",""name"":""Pax"",""x_review_proposed"":{""by"":""m1""}}"),
                JObject.Parse(@"{""id"":""b"",""type"":""character"",""name"":""Brin""}"));
            var served = OWLens.ServedView(world, OWLensReader.Public());
            CollectionAssert.AreEqual(new[] { "b" }, Strings(served.Single(e => (string)e["id"] == "a")["friends"]),
                "the public rung is served [Brin]");
            Assert.AreEqual(@"[{""source"":""a"",""field"":""friends"",""target"":""b"",""index"":0}]",
                new JArray(OWGraph.Read(world, OWLensReader.Public()).Edges.Select(e => e.ToJson())).ToString(Formatting.None),
                "Brin at 0");
        }

        /// <summary>
        /// Ruling 1 reaches the edge list: a link reaches only an element of its field's target type, so a Character's
        /// <c>friends</c> naming a Location's id names nothing, a dangling link, neither an edge nor counted. The owner's
        /// index is still the place in the list as stored. Kills: an edge, or a count, for a wrongly typed id.
        /// </summary>
        [Test]
        public void RulingB_AWronglyTypedId_DrawsNoEdge_AndIsNotCounted()
        {
            var world = new JArray(
                JObject.Parse(@"{""id"":""c"",""type"":""character"",""name"":""C"",""friends"":[""p"",""h"",""d""],""location"":""p""}"),
                JObject.Parse(@"{""id"":""p"",""type"":""location"",""name"":""P""}"),
                JObject.Parse(@"{""id"":""h"",""type"":""location"",""name"":""H"",""hidden"":true}"),
                JObject.Parse(@"{""id"":""d"",""type"":""character"",""name"":""D""}"));
            var g = OWGraph.Read(world, OWLensReader.Owner());
            Assert.AreEqual(@"[{""source"":""c"",""field"":""friends"",""target"":""d"",""index"":2},{""source"":""c"",""field"":""location"",""target"":""p"",""index"":0}]",
                new JArray(g.Edges.Select(e => e.ToJson())).ToString(Formatting.None), "the friend D and the location, never P as a friend");
            CollectionAssert.IsEmpty(g.EdgesCut, "the hidden Location under friends is no link, so not counted hidden");
            var c = OWGraph.Read(world, OWLensReader.Contributor());
            Assert.AreEqual(@"[{""source"":""c"",""field"":""friends"",""target"":""d"",""index"":0},{""source"":""c"",""field"":""location"",""target"":""p"",""index"":0}]",
                new JArray(c.Edges.Select(e => e.ToJson())).ToString(Formatting.None), "a contributor is served friends [D]");
        }

        /// <summary>
        /// The cut counts each element once, so an edge to an element the lens cut only as a wrongly typed anchor (a
        /// Location named under <c>characters</c>) reads the lens's reason, never <c>outside_lens</c>. Guards what this
        /// reader already does. Kills: the edge's target looked up by its own type and id.
        /// </summary>
        [Test]
        public void AnEdgeToAnElementTheLensCutAsAWronglyTypedAnchor_ReadsTheLenssReason()
        {
            var world = new JArray(
                JObject.Parse(@"{""id"":""o"",""type"":""object"",""name"":""Ledger"",""location"":""x""}"),
                JObject.Parse(@"{""id"":""x"",""type"":""location"",""name"":""Harbour""}"));
            var lens = JObject.Parse(@"{""id"":""L"",""type"":""construct"",""name"":""L"",""supertype"":""Lens"",""characters"":[""x""],""objects"":[""o""]}");
            var g = OWGraph.Read(world, OWLensReader.Owner(), new OWGraphOptions { Lens = lens }).ToJson();
            Assert.AreEqual(@"[[""o""],{""anchor_not_visible"":1},{""anchor_not_visible"":1}]",
                new JArray(new JArray(g["nodes"].Select(n => n["id"])), g["cut"], g["edges_cut"]).ToString(Formatting.None));
        }

        /// <summary>
        /// Counts are per reader, and an edge with them: below the owner an element whose owner-side reason was
        /// <c>anchor_not_visible</c> and which a child cuts by <c>types</c> is cut by the child's reason, so the edge to
        /// it is cut by that reason, never left uncounted. Guards what this reader already does. Kills: the unseen anchor
        /// recorded for every reader and dropped at the count.
        /// </summary>
        [Test]
        public void BelowTheOwner_AnEdgeToAnUnseenAnchorACutChildReaches_IsCutByTheChildsReason()
        {
            var world = new JArray(
                JObject.Parse(@"{""id"":""U"",""type"":""construct"",""name"":""U"",""supertype"":""Lens"",""characters"":[""x""],""constructs"":[""C""],""x_lens"":{""depth"":0}}"),
                JObject.Parse(@"{""id"":""C"",""type"":""construct"",""name"":""C"",""supertype"":""Lens"",""characters"":[""y""],""x_lens"":{""types"":[""character""]}}"),
                JObject.Parse(@"{""id"":""y"",""type"":""character"",""name"":""Ysolde"",""birthplace"":""x""}"),
                JObject.Parse(@"{""id"":""x"",""type"":""location"",""name"":""Harbour""}"));
            var g = OWGraph.Read(world, OWLensReader.Contributor(), new OWGraphOptions { LensId = "U" }).ToJson();
            Assert.AreEqual(@"[[""y""],{""types"":1},{""types"":1}]",
                new JArray(new JArray(g["nodes"].Select(n => n["id"])), g["cut"], g["edges_cut"]).ToString(Formatting.None));
        }

        /// <summary>
        /// Hidden beats known_of: a hidden element named in a guest's <c>known_of</c> is no node, and a link to it is left
        /// out of the guest's served list, so the next edge's index has no gap; for the guest it is not counted, and for
        /// the owner reading as the guest it is <c>not_visible</c>, a target the reader does not see. Kills: the hidden
        /// element drawn as a known-of node.
        /// </summary>
        [Test]
        public void AHiddenElementInAGuestsKnownOf_IsNoNode_AndALinkToItIsNotVisible()
        {
            var world = new JArray(
                JObject.Parse(@"{""id"":""c1"",""type"":""character"",""name"":""Ada"",""friends"":[""h"",""b""]}"),
                JObject.Parse(@"{""id"":""h"",""type"":""character"",""name"":""Hal"",""hidden"":true}"),
                JObject.Parse(@"{""id"":""b"",""type"":""character"",""name"":""Brin""}"));
            var guest = OWLensReader.Guest(new[] { "c1", "b" }, new[] { "h" });
            var g = OWGraph.Read(world, guest).ToJson();
            Assert.AreEqual(@"[[""b"",""c1""],[{""source"":""c1"",""field"":""friends"",""target"":""b"",""index"":0}],{}]",
                new JArray(new JArray(g["nodes"].Select(n => n["id"])), g["edges"], g["edges_cut"]).ToString(Formatting.None));
            var asGuest = OWGraph.Read(world, guest.WithOwnerCounts()).ToJson();
            Assert.AreEqual(@"[[""b"",""c1""],{""not_visible"":1}]",
                new JArray(new JArray(asGuest["nodes"].Select(n => n["id"])), asGuest["edges_cut"]).ToString(Formatting.None));
        }

        private static OWGraphOptions Flag(string flag) => new OWGraphOptions
        {
            Rooms = flag == "rooms", Hidden = flag == "hidden", Proposed = flag == "proposed", MapSpace = flag == "map_space",
        };

        private static List<string> Strings(JToken token) =>
            token is JArray array ? array.Select(t => (string)t).ToList() : new List<string>();

        /// <summary>A world that throws the moment anything enumerates it.</summary>
        private sealed class Unread : IEnumerable<JToken>
        {
            public sealed class ReadException : Exception
            {
                public ReadException() : base("read")
                {
                }
            }

            public IEnumerator<JToken> GetEnumerator() => throw new ReadException();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
