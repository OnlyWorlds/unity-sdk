using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The map-space profile's frame reading (schema-dist <c>profiles/map-space.md</c>, rules 13 and 14, serial 139):
    /// every entry of the fixture's <c>frame_cases</c> (the copy in <c>Tests/Editor/Fixtures/map-space/</c>, whose bytes
    /// <see cref="OWMapSpaceTests"/> holds to the dist's), replayed whole, case by case by name, through
    /// <see cref="OWMapSpace.CarryOf"/>. A case is read as the page says: <c>map</c> is the last Map row served with the
    /// case's id (or each row <c>map_index</c> lists, whatever its type), <c>maps</c> every row of the case, of which the
    /// reader keeps the Maps itself, and <c>point</c> the case's. A number in the answer compares as a 64-bit float,
    /// exactly and with no tolerance (<c>100.0</c> and <c>100</c> are one number); every other part as written, key
    /// order included. The TypeScript SDK's <c>test/carry-of.test.mjs</c> (4.183.0) replays the same cases.
    /// </summary>
    public class OWMapCarryTests
    {
        private static JObject _fixture;

        private static JObject Fixture
        {
            get
            {
                if (_fixture != null) return _fixture;
                var root = TestPackage.Root();
                var path = root == null ? null : Path.Combine(root, "Tests", "Editor", "Fixtures", "map-space", "map-space.fixture.json");
                return path == null || !File.Exists(path) ? null : _fixture = OWJson.ParseObject(File.ReadAllText(path));
            }
        }

        private static List<JObject> FrameCases() => Fixture["frame_cases"].Cast<JObject>().ToList();

        /// <summary>One case per frame case, numbered in the fixture's order; the case is found by its name.</summary>
        public static IEnumerable<TestCaseData> Cases()
        {
            var fixture = Fixture;
            if (fixture == null || !(fixture["frame_cases"] is JArray))
            {
                yield return new TestCaseData(string.Empty).SetName("MapCarry_FixtureNotFound");
                yield break;
            }

            var i = 0;
            foreach (var c in FrameCases())
                yield return new TestCaseData((string)c["name"]).SetName($"MapCarry_{++i:D2}");
        }

        /// <summary>
        /// The rows a case reads: by <c>map</c>, the last row served whose <c>type</c> is <c>map</c> and whose id is the
        /// case's; by <c>map_index</c>, each row at those positions, whatever its type (dist.139's
        /// <c>row-read-whose-type-is-Map</c> reads a row whose <c>type</c> is <c>Map</c>). Never both, never neither.
        /// </summary>
        private static List<JObject> RowsRead(JObject c)
        {
            var name = (string)c["name"];
            var elements = ((JArray)c["elements"]).Cast<JObject>().ToList();
            var byId = c["map"] != null;
            var byIndex = c["map_index"] != null;
            Assert.IsTrue(byId != byIndex, name + ": a case names its Map by map or by map_index, one of the two");
            if (byIndex)
            {
                var index = c["map_index"] as JArray;
                Assert.IsTrue(index != null && index.Count > 0, name + ": map_index is a list of positions");
                return index.Select(i =>
                {
                    Assert.AreEqual(JTokenType.Integer, i.Type, name + ": a position is a whole number");
                    var at = (int)i;
                    Assert.IsTrue(at >= 0 && at < elements.Count, name + ": position " + at + " is in the elements");
                    return elements[at];
                }).ToList();
            }

            Assert.AreEqual(JTokenType.String, c["map"].Type, name + ": map is an id");
            var rows = elements.Where(e => e["type"]?.Type == JTokenType.String && (string)e["type"] == "map"
                && e["id"]?.Type == JTokenType.String && (string)e["id"] == (string)c["map"]).ToList();
            Assert.IsTrue(rows.Count > 0, name + ": no Map row with the case's id");
            return new List<JObject> { rows[rows.Count - 1] };
        }

        /// <summary>Each row a case names, read with the case's point and every row of the case as <c>maps</c>.</summary>
        private static List<OWMapCarry> Reads(JObject c)
        {
            var point = (JArray)c["point"];
            Assert.AreEqual(2, point.Count, (string)c["name"] + ": a point is [x, y]");
            var elements = (JArray)c["elements"];
            return RowsRead(c).Select(row => OWMapSpace.CarryOf(row, (double)point[0], (double)point[1], elements)).ToList();
        }

        /// <summary>
        /// Exactly equal: a number as a 64-bit float (<c>==</c>, so 0 and -0 are one number and <c>100.0</c> is 100), a
        /// list item by item, an object key by key in order, everything else as written.
        /// </summary>
        private static void Same(JToken got, JToken want, string path)
        {
            if (want.Type == JTokenType.Integer || want.Type == JTokenType.Float)
            {
                Assert.IsTrue(got.Type == JTokenType.Integer || got.Type == JTokenType.Float, path + ": a number, got " + got.ToString(Formatting.None));
                var g = OWMapSpace.ToDouble((JValue)got);
                var w = OWMapSpace.ToDouble((JValue)want);
                Assert.IsTrue(g == w, string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}: {1:R} is not {2:R}", path, g, w));
                return;
            }

            if (want is JArray wl)
            {
                Assert.IsInstanceOf<JArray>(got, path + ": a list");
                var gl = (JArray)got;
                Assert.AreEqual(wl.Count, gl.Count, path + ": length");
                for (var i = 0; i < wl.Count; i++) Same(gl[i], wl[i], $"{path}[{i}]");
                return;
            }

            if (want is JObject wo)
            {
                Assert.IsInstanceOf<JObject>(got, path + ": an object");
                var go = (JObject)got;
                CollectionAssert.AreEqual(wo.Properties().Select(p => p.Name).ToList(), go.Properties().Select(p => p.Name).ToList(),
                    path + ": keys in order");
                foreach (var p in wo.Properties()) Same(go[p.Name], p.Value, path + "." + p.Name);
                return;
            }

            Assert.AreEqual(want.ToString(Formatting.None), got.ToString(Formatting.None), path);
        }

        [Test]
        public void TheFixture_HoldsTheFrameCases_EachNamedOnce_AndEachIsReplayed()
        {
            Assert.IsNotNull(Fixture, "the vendored map-space fixture was not found");
            Assert.AreEqual("map-space", (string)Fixture["profile"]);
            Assert.IsInstanceOf<JArray>(Fixture["frame_cases"], "dist.139's fixture holds frame_cases beside cases and layout_cases");
            var cases = FrameCases();
            Assert.Greater(cases.Count, 0, "the fixture holds no frame case");
            var names = cases.Select(c => (string)c["name"]).ToList();
            Assert.AreEqual(names.Count, names.Distinct().Count(), "a case is found by its name, so each name is once");

            // The count is the fixture's own: every case it holds is one test of the source, never a typed number.
            var sourced = Cases().Select(t => (string)t.Arguments[0]).ToList();
            CollectionAssert.AreEqual(names, sourced, "the case source yields every frame case, once, in the fixture's order");
        }

        [TestCaseSource(nameof(Cases))]
        public void EveryFrameCase_IsAnsweredAsTheProfileSays(string name)
        {
            Assert.IsNotNull(Fixture, "the vendored map-space fixture was not found");
            var c = FrameCases().Single(x => (string)x["name"] == name);
            var want = (JObject)c["want"];
            var n = 0;
            foreach (var answer in Reads(c))
            {
                var row = $"{name} (row {++n})";
                var got = answer.ToJson();
                CollectionAssert.AreEqual(new[] { "parent_frame", "lint", "points", "stop" },
                    got.Properties().Select(p => p.Name).ToList(), row + ": all four keys, in order");
                Same(got, want, row);
            }

            Assert.Greater(n, 0, name + ": no row read");
        }

        /// <summary>The replay reads every row a case names: one per <c>map</c> case, each of a <c>map_index</c> case's positions.</summary>
        [Test]
        public void TheReplay_ReadsEveryRowACaseNames()
        {
            Assert.IsNotNull(Fixture, "the vendored map-space fixture was not found");
            var cases = FrameCases();
            var byIndex = cases.Where(c => c["map_index"] != null).ToList();
            var named = cases.Count - byIndex.Count + byIndex.Sum(c => ((JArray)c["map_index"]).Count);
            Assert.AreEqual(named, cases.Sum(c => Reads(c).Count), "every row a case names, read once");
        }

        /// <summary>The typed answer says what its JSON says, and the reading is pure: the rows are not moved, and the same case gives the same bytes.</summary>
        [Test]
        public void TheTypedAnswer_MatchesItsJson_AndTheReadingIsPure()
        {
            Assert.IsNotNull(Fixture, "the vendored map-space fixture was not found");
            foreach (var c in FrameCases())
            {
                var name = (string)c["name"];
                var before = c["elements"].ToString(Formatting.None);
                var first = Reads(c);
                var second = Reads(c);
                Assert.AreEqual(before, c["elements"].ToString(Formatting.None), name + ": the elements moved");
                var want = (JObject)c["want"];
                for (var i = 0; i < first.Count; i++)
                {
                    var once = first[i];
                    Assert.AreEqual(once.ToJson().ToString(Formatting.None), second[i].ToJson().ToString(Formatting.None), name);
                    if (want["parent_frame"].Type == JTokenType.Null) Assert.IsNull(once.ParentFrame, name);
                    else CollectionAssert.AreEqual(want["parent_frame"].Select(t => (double)t).ToList(), once.ParentFrame, name);
                    Assert.AreEqual(want["lint"].Type == JTokenType.Null ? null : (string)want["lint"], once.Lint, name);
                    Assert.AreEqual(want["stop"].Type == JTokenType.Null ? null : (string)want["stop"], once.Stop, name);
                    var wantPoints = ((JArray)want["points"]).Cast<JObject>()
                        .Select(p => (map: (string)p["map"], x: (double)p["x"], y: (double)p["y"])).ToList();
                    CollectionAssert.AreEqual(wantPoints, once.Points.Select(p => (map: p.Map, x: p.X, y: p.Y)).ToList(), name);
                }
            }
        }

        /// <summary>The map-space and layout readings are unchanged by rules 13 and 14: neither answer carries a frame key.</summary>
        [Test]
        public void TheOtherReadings_CarryNoFrameKey()
        {
            Assert.IsNotNull(Fixture, "the vendored map-space fixture was not found");
            foreach (var c in FrameCases())
            {
                var elements = (JArray)c["elements"];
                var got = OWMapSpace.Of(elements).ToJson();
                foreach (var m in ((JArray)got["maps"]).Cast<JObject>())
                    Assert.IsFalse(m.Properties().Any(p => p.Name == "parent_frame" || p.Name == "x_parent_frame"),
                        (string)c["name"] + ": a map-space entry carries a frame key");
            }
        }

        // -- Beyond the fixture: the refusals (the TypeScript SDK's TypeError, test/carry-of.test.mjs) and npm's choices

        private static JObject Row(string json) => OWJson.ParseObject(json);

        [Test]
        public void CarryOf_RefusesAPointThatIsNotTwoFiniteNumbers_AndANullMapOrMaps()
        {
            var m = Row(@"{""type"":""map"",""id"":""a"",""width"":10,""height"":10}");
            var maps = new JArray(m);
            foreach (var (x, y) in new[] { (double.NaN, 2d), (1d, double.NaN), (double.PositiveInfinity, 2d), (1d, double.NegativeInfinity) })
                Assert.Throws<ArgumentException>(() => OWMapSpace.CarryOf(m, x, y, maps), $"[{x}, {y}]");
            Assert.Throws<ArgumentNullException>(() => OWMapSpace.CarryOf(null, 1, 2, maps));
            Assert.Throws<ArgumentNullException>(() => OWMapSpace.CarryOf(m, 1, 2, null));
        }

        /// <summary>
        /// npm's choice where the profile is silent: the walk's first level is the row passed, never a row of
        /// <c>maps</c> with its id, and a Map not among <c>maps</c> is still read (a parent naming it, not served, is
        /// <c>dangling</c>).
        /// </summary>
        [Test]
        public void CarryOf_StartsFromTheRowPassed_EvenWhenMapsHoldsAnotherWithItsId()
        {
            var land = Row(@"{""type"":""map"",""id"":""land"",""width"":1000,""height"":800}");
            var passed = Row(@"{""type"":""map"",""id"":""town"",""width"":100,""height"":50,""parent_map"":""land"",""x_parent_frame"":[100,200,300,300]}");
            var other = Row(@"{""type"":""map"",""id"":""town"",""width"":10,""height"":10,""parent_map"":""land"",""x_parent_frame"":[0,0,10,10]}");
            var a = OWMapSpace.CarryOf(passed, 20, 10, new JArray(land, other));
            Assert.AreEqual(OWMapCarryStop.Top, a.Stop);
            Assert.AreEqual(new OWMapCarryPoint("land", 140, 220), a.Points[1], "carried by the row passed, not maps' row");

            var ring = Row(@"{""type"":""map"",""id"":""up"",""width"":10,""height"":10,""parent_map"":""down"",""x_parent_frame"":[0,0,5,5]}");
            var down = Row(@"{""type"":""map"",""id"":""down"",""width"":10,""height"":10,""parent_map"":""up"",""x_parent_frame"":[0,0,5,5]}");
            var b = OWMapSpace.CarryOf(down, 4, 4, new JArray(ring));
            Assert.AreEqual(OWMapCarryStop.Dangling, b.Stop, "up names down, which is not served");
            Assert.AreEqual(2, b.Points.Count);
        }

        /// <summary>npm's choice where the profile bounds no magnitude: a whole size past 2^53 divides, read as its nearest double.</summary>
        [Test]
        public void CarryOf_DividesByAWholeSizePastTwoToThe53()
        {
            var land = Row(@"{""type"":""map"",""id"":""land"",""width"":10,""height"":10}");
            var town = Row(@"{""type"":""map"",""id"":""town"",""width"":18014398509481984,""height"":18014398509481984.0,""parent_map"":""land"",""x_parent_frame"":[0,0,4,4]}");
            var a = OWMapSpace.CarryOf(town, 9007199254740992, 9007199254740992, new JArray(land));
            Assert.AreEqual(OWMapCarryStop.Top, a.Stop);
            Assert.AreEqual(new OWMapCarryPoint("land", 2, 2), a.Points[1]);
        }
    }
}
