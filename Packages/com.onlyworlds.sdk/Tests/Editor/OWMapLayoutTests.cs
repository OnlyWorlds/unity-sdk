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
    /// The map-space profile's layout reading (schema-dist <c>profiles/map-space.md</c>, rules 8 to 10, serial 110): every
    /// entry of the fixture's <c>layout_cases</c> (the copy in <c>Tests/Editor/Fixtures/map-space/</c>, whose bytes
    /// <see cref="OWMapSpaceTests"/> holds to the dist's), replayed whole, case by case by name, through
    /// <see cref="OWMapSpace.LayoutOf"/>. A case is read as the page says: <c>map</c> is the case's Map row with that id,
    /// <c>maps</c> its Map rows, <c>pins</c> its Pin rows; its <c>want</c> is the whole answer, keys in order. Then the
    /// TypeScript SDK's (4.142.0, <c>test/layout-of.test.mjs</c>) and the Python package's (0.110.0,
    /// <c>tests/test_layout_of.py</c>) vectors beyond the fixture.
    /// </summary>
    public class OWMapLayoutTests
    {
        /// <summary>The fixture's layout cases, as dist.119 holds them (seventeen at dist.110, 21 at dist.111).</summary>
        private const int LayoutCaseCount = 23;

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

        private static IEnumerable<JObject> LayoutCases() => Fixture["layout_cases"].Cast<JObject>();

        /// <summary>One case per layout case, numbered in the fixture's order; the case is found by its name.</summary>
        public static IEnumerable<TestCaseData> Cases()
        {
            var fixture = Fixture;
            if (fixture == null || !(fixture["layout_cases"] is JArray))
            {
                yield return new TestCaseData(string.Empty).SetName("MapLayout_FixtureNotFound");
                yield break;
            }

            var i = 0;
            foreach (var c in LayoutCases())
                yield return new TestCaseData((string)c["name"]).SetName($"MapLayout_{++i:D2}");
        }

        /// <summary>
        /// A case read as the page says, once per Map row it names: by <c>map</c>, the one Map row with that id; by
        /// <c>map_index</c> (since dist.111, for rows with no id to name them by), each row at those positions in its
        /// elements, every one a Map row. <c>maps</c> are the case's Map rows, <c>pins</c> its Pin rows. A case names its
        /// Map one way, never both and never neither: a reader that looked a row up by an absent id would find a row
        /// with no id by accident and read one row where the case names two.
        /// </summary>
        private static List<OWMapLayout> Reads(JObject c)
        {
            var name = (string)c["name"];
            var elements = ((JArray)c["elements"]).Cast<JObject>().ToList();
            var maps = elements.Where(e => (string)e["type"] == "map").ToList();
            var pins = elements.Where(e => (string)e["type"] == "pin").ToList();
            var byId = c["map"] != null;
            var byIndex = c["map_index"] != null;
            Assert.IsTrue(byId != byIndex, name + ": a case names its Map by map or by map_index, one of the two");
            var read = new List<JObject>();
            if (byId)
            {
                Assert.AreEqual(JTokenType.String, c["map"].Type, name + ": map is an id");
                read.Add(maps.SingleOrDefault(e => e["id"]?.Type == JTokenType.String && (string)e["id"] == (string)c["map"]));
                Assert.IsNotNull(read[0], name + ": no Map row with the case's id");
            }
            else
            {
                var index = c["map_index"] as JArray;
                Assert.IsTrue(index != null && index.Count > 0, name + ": map_index is a list of positions");
                foreach (var i in index)
                {
                    Assert.AreEqual(JTokenType.Integer, i.Type, name + ": a position is a whole number");
                    var at = (int)i;
                    Assert.IsTrue(at >= 0 && at < elements.Count, name + ": position " + at + " is in the elements");
                    Assert.AreEqual("map", (string)elements[at]["type"], name + ": position " + at + " is a Map row");
                    read.Add(elements[at]);
                }
            }

            return read.Select(map => OWMapSpace.LayoutOf(map, maps, pins)).ToList();
        }

        /// <summary>A case that names one Map row (by its id): its one answer.</summary>
        private static OWMapLayout Read(JObject c)
        {
            var all = Reads(c);
            Assert.AreEqual(1, all.Count, (string)c["name"] + ": one Map row read");
            return all[0];
        }

        [Test]
        public void TheFixture_HoldsDist119sLayoutCases_EachNamedOnce_BesideItsCases()
        {
            Assert.IsNotNull(Fixture, "the vendored map-space fixture was not found");
            Assert.IsInstanceOf<JArray>(Fixture["layout_cases"], "dist.111's fixture holds layout_cases beside cases");
            var names = LayoutCases().Select(c => (string)c["name"]).ToList();
            Assert.AreEqual(LayoutCaseCount, names.Count, "dist.119's fixture holds 23 layout cases");
            Assert.AreEqual(names.Count, names.Distinct().Count(), "a case is found by its name, so each name is once");
            CollectionAssert.IsSubsetOf(new[] { "parentless-layout-beside-ground", "ground-beside-parentless-layout" }, names,
                "dist.119's two cases: a layout Map and its ground, neither with a parent_map, each read in turn");
            Assert.AreEqual(35, Fixture["cases"].Count(), "the map-space reading's cases beside them (35 since dist.119)");
        }

        [TestCaseSource(nameof(Cases))]
        public void EveryLayoutCase_IsAnsweredAsTheProfileSays(string name)
        {
            Assert.IsNotNull(Fixture, "the vendored map-space fixture was not found");
            var c = LayoutCases().Single(x => (string)x["name"] == name);
            var want = (JObject)c["want"];
            var n = 0;
            foreach (var answer in Reads(c))
            {
                var got = answer.ToJson();
                var row = $"{name} (row {++n})";
                CollectionAssert.AreEqual(new[] { "layout", "ground", "hand" },
                    got.Properties().Select(p => p.Name).ToList(), row + ": all three keys, in order");
                foreach (var p in want.Properties())
                    Assert.AreEqual(p.Value.ToString(Formatting.None), got[p.Name].ToString(Formatting.None), $"{row}: {p.Name}");
                Assert.AreEqual(want.ToString(Formatting.None), got.ToString(Formatting.None), row + ": the whole answer");
            }
        }

        /// <summary>
        /// The replay reads every row a case names: one per <c>map</c> case, each of a <c>map_index</c> case's positions
        /// (dist.111's <c>map-without-a-string-id</c> names two, a row with no <c>id</c> and one whose <c>id</c> is 7).
        /// </summary>
        [Test]
        public void TheReplay_ReadsEveryRowACaseNames()
        {
            Assert.IsNotNull(Fixture, "the vendored map-space fixture was not found");
            var byIndex = LayoutCases().Where(c => c["map_index"] != null).ToList();
            CollectionAssert.AreEqual(new[] { "map-without-a-string-id" }, byIndex.Select(c => (string)c["name"]).ToList(),
                "dist.111 names one case's Maps by map_index");
            Assert.AreEqual(2, Reads(byIndex[0]).Count, "its two rows, each read");
            Assert.AreEqual(LayoutCaseCount - 1 + 2, LayoutCases().Sum(c => Reads(c).Count), "every row a case names, read once");
        }

        /// <summary>The map-space reading is unchanged by rules 8 to 10: its answer on every layout case carries no layout key.</summary>
        [Test]
        public void TheMapSpaceReading_CarriesNoLayoutKey()
        {
            Assert.IsNotNull(Fixture, "the vendored map-space fixture was not found");
            foreach (var c in LayoutCases())
            {
                var got = OWMapSpace.Of((JArray)c["elements"]).ToJson();
                CollectionAssert.AreEqual(new[] { "maps", "zones", "pins", "out" }, got.Properties().Select(p => p.Name).ToList());
                var rows = ((JArray)got["maps"]).Concat((JArray)got["pins"]).Cast<JObject>();
                Assert.IsFalse(rows.SelectMany(r => r.Properties()).Any(p => p.Name.StartsWith("x_", StringComparison.Ordinal)
                    || p.Name == "layout" || p.Name == "hand"), (string)c["name"]);
            }
        }

        /// <summary>The reading is pure: the rows are not moved, the same case gives the same bytes, and the typed answer says what its JSON says.</summary>
        [Test]
        public void TheReading_IsPure_AndItsTypedAnswerMatchesItsJson()
        {
            Assert.IsNotNull(Fixture, "the vendored map-space fixture was not found");
            foreach (var c in LayoutCases())
            {
                var before = c["elements"].ToString(Formatting.None);
                var first = Reads(c);
                var second = Reads(c);
                Assert.AreEqual(before, c["elements"].ToString(Formatting.None), (string)c["name"] + ": the elements moved");
                var want = (JObject)c["want"];
                for (var i = 0; i < first.Count; i++)
                {
                    var once = first[i];
                    Assert.AreEqual(once.ToJson().ToString(Formatting.None), second[i].ToJson().ToString(Formatting.None), (string)c["name"]);
                    Assert.AreEqual(want["layout"].Type == JTokenType.Null ? null : (string)want["layout"], once.Layout);
                    Assert.AreEqual(want["layout"].Type != JTokenType.Null, once.IsLayoutMap);
                    Assert.AreEqual(want["ground"].Type == JTokenType.Null ? null : (string)want["ground"], once.Ground);
                    CollectionAssert.AreEqual(want["hand"].Select(h => (string)h).ToList(), once.Hand);
                }
            }
        }

        // -- Beyond the fixture: the TypeScript SDK's vectors (test/layout-of.test.mjs, 4.142.0) ----------------------

        private const string M = "m1";

        private static JObject Row(string json) => OWJson.ParseObject(json);

        private static JObject Trail => Row(@"{""type"":""map"",""id"":""m1"",""x_layout"":{""method"":""trail-by-place""}}");

        private static JObject Board => Row(@"{""type"":""map"",""id"":""m1"",""x_layout"":{""method"":""hand""}}");

        private static JObject Pin(string id, bool hand) =>
            new JObject { ["type"] = "pin", ["id"] = id, ["map"] = M, ["x_hand"] = hand };

        private static string Json(OWMapLayout l) => l.ToJson().ToString(Formatting.None);

        /// <summary>U+FF5E sorts before U+1F600 by code point; as UTF-16 units the surrogate D83D sorts first.</summary>
        [Test]
        public void Hand_IsInCodePointOrder_NeverUtf16Order()
        {
            var low = char.ConvertFromUtf32(0xFF5E);
            var high = char.ConvertFromUtf32(0x1F600);
            var pins = new[] { Pin(high, true), Pin(low, true) };
            CollectionAssert.AreEqual(new[] { low, high }, OWMapSpace.LayoutOf(Trail, new[] { Trail }, pins).Hand);
            CollectionAssert.AreEqual(new[] { low, high }, OWMapSpace.LayoutOf(Board, new JObject[0], pins).Hand);
        }

        [Test]
        public void ARowWithoutANonEmptyStringId_IsNoPin_AndOfTwoRowsWithOneId_TheLastServedIsRead()
        {
            var pins = new JToken[]
            {
                Row(@"{""type"":""pin"",""map"":""m1"",""x_hand"":true}"),
                Row(@"{""type"":""pin"",""id"":"""",""map"":""m1"",""x_hand"":true}"),
                Row(@"{""type"":""pin"",""id"":7,""map"":""m1"",""x_hand"":true}"),
                JValue.CreateNull(),
                new JValue("p"),
                Row(@"{""type"":""pin"",""id"":""p1"",""map"":""m1"",""x_hand"":true}"),
                Row(@"{""type"":""pin"",""id"":""p1"",""map"":""other"",""x_hand"":true}"),
                Row(@"{""type"":""pin"",""id"":""p2"",""map"":""m1""}"),
                Row(@"{""type"":""pin"",""id"":""p2"",""map"":""m1"",""x_hand"":true}"),
            };
            Assert.AreEqual(@"{""layout"":""trail-by-place"",""ground"":null,""hand"":[""p2""]}",
                Json(OWMapSpace.LayoutOf(Trail, new[] { Trail }, pins)));
        }

        [Test]
        public void AMapRowWithoutAnId_IsNoLayoutMap_AndAnXLayoutThatIsAList_IsNoLayout()
        {
            var pins = new[] { Pin("p1", true) };
            const string none = @"{""layout"":null,""ground"":null,""hand"":[]}";
            Assert.AreEqual(none, Json(OWMapSpace.LayoutOf(Row(@"{""type"":""map"",""x_layout"":{""method"":""hand""}}"), new JObject[0], pins)));
            Assert.AreEqual(none, Json(OWMapSpace.LayoutOf(Row(@"{""type"":""map"",""id"":""m1"",""x_layout"":[{""method"":""hand""}]}"), new JObject[0], pins)));
        }

        /// <summary>
        /// Reading H says a non-empty string id (dist.119's words): a Map row whose <c>id</c> is <c>""</c> is no Map, so
        /// no layout map, even with a method and a Pin naming <c>""</c> as its map. No fixture case holds this row yet.
        /// </summary>
        [Test]
        public void AMapRowWhoseIdIsTheEmptyString_IsNoLayoutMap()
        {
            var map = Row(@"{""type"":""map"",""id"":"""",""x_layout"":{""method"":""hand""}}");
            var pins = new[] { Row(@"{""type"":""pin"",""id"":""p1"",""map"":""""}") };
            Assert.AreEqual(@"{""layout"":null,""ground"":null,""hand"":[]}", Json(OWMapSpace.LayoutOf(map, new[] { map }, pins)));
        }

        [Test]
        public void AGroundNamedByAMapRowTheReaderCannotReadAsAnElement_IsNoGround()
        {
            var grounded = Row(@"{""type"":""map"",""id"":""m1"",""x_layout"":{""method"":""trail-by-place"",""settings"":{""ground"":{""map"":""g""}}}}");
            Assert.AreEqual("g", OWMapSpace.LayoutOf(grounded, new[] { grounded, Row(@"{""type"":""map"",""id"":""g""}") }, new JObject[0]).Ground);
            Assert.IsNull(OWMapSpace.LayoutOf(grounded, new[] { grounded, Row(@"{""type"":""map"",""name"":""g""}") }, new JObject[0]).Ground);
            var flat = Row(@"{""type"":""map"",""id"":""m1"",""x_layout"":{""method"":""trail-by-place"",""settings"":{""ground"":""g""}}}");
            Assert.IsNull(OWMapSpace.LayoutOf(flat, new[] { Row(@"{""type"":""map"",""id"":""g""}") }, new JObject[0]).Ground);
        }

        [Test]
        public void TheAnswer_IsTheCallersOwn()
        {
            var c = LayoutCases().Single(x => (string)x["name"] == "board-every-pin-is-hand");
            var got = Read(c);
            got.ToJson()["hand"].Last.AddAfterSelf("planted");
            if (got.Hand is IList<string> list && !list.IsReadOnly) list.Add("planted");
            Assert.AreEqual(c["want"].ToString(Formatting.None), Json(Read(c)));
        }

        [Test]
        public void NoMapOrNoList_IsRefused()
        {
            Assert.Throws<ArgumentNullException>(() => OWMapSpace.LayoutOf(null, new JObject[0], new JObject[0]));
            Assert.Throws<ArgumentNullException>(() => OWMapSpace.LayoutOf(Trail, null, new JObject[0]));
            Assert.Throws<ArgumentNullException>(() => OWMapSpace.LayoutOf(Trail, new JObject[0], null));
        }

        // -- Beyond the fixture: the Python package's vectors (tests/test_layout_of.py, 0.110.0) ---------------------

        [Test]
        public void RowsAreReadAsTheMapSpaceReadingReadsThem_OnABoard()
        {
            var board = Row(@"{""type"":""map"",""id"":""m"",""x_layout"":{""method"":""hand""}}");
            var pins = new JToken[]
            {
                Row(@"{""type"":""pin"",""id"":""b"",""map"":""m""}"),
                Row(@"{""type"":""pin"",""id"":"""",""map"":""m""}"),
                Row(@"{""type"":""pin"",""id"":7,""map"":""m""}"),
                Row(@"{""type"":""pin"",""map"":""m""}"),
                new JValue("not a row"),
                Row(@"{""type"":""pin"",""id"":""a"",""map"":""m""}"),
                Row(@"{""type"":""pin"",""id"":""a"",""map"":""elsewhere""}"),
            };
            Assert.AreEqual(@"{""layout"":""hand"",""ground"":null,""hand"":[""b""]}", Json(OWMapSpace.LayoutOf(board, new[] { board }, pins)));
        }

        [Test]
        public void AMethodThatIsNotANonEmptyString_IsNoLayout()
        {
            foreach (var method in new[] { "true", "false", "1", "1.0", "null", @"[""hand""]", @"{""method"":""hand""}", @"""""" })
            {
                var map = Row(@"{""type"":""map"",""id"":""m"",""x_layout"":{""method"":" + method + "}}");
                var pin = Row(@"{""type"":""pin"",""id"":""p"",""map"":""m"",""x_hand"":true}");
                Assert.AreEqual(@"{""layout"":null,""ground"":null,""hand"":[]}", Json(OWMapSpace.LayoutOf(map, new[] { map }, new[] { pin })), method);
            }
        }

        /// <summary>U+FFFF sorts before U+10000 by code point, after it by UTF-16 unit.</summary>
        [Test]
        public void Hand_IsInCodePointOrder_AboveUFFFF()
        {
            var board = Row(@"{""type"":""map"",""id"":""m"",""x_layout"":{""method"":""hand""}}");
            var above = char.ConvertFromUtf32(0x10000);
            var edge = "\uFFFF";
            var pins = new[]
            {
                new JObject { ["type"] = "pin", ["id"] = above, ["map"] = "m" },
                new JObject { ["type"] = "pin", ["id"] = edge, ["map"] = "m" },
            };
            CollectionAssert.AreEqual(new[] { edge, above }, OWMapSpace.LayoutOf(board, new[] { board }, pins).Hand);
        }

        [Test]
        public void Settings_AreNeverReturned()
        {
            var ground = Row(@"{""type"":""map"",""id"":""g"",""width"":30,""height"":20}");
            var map = Row(@"{""type"":""map"",""id"":""m"",""x_layout"":{""method"":""trail-by-place"",""settings"":{""ground"":{""map"":""g"",""width"":3,""height"":2}}}}");
            Assert.AreEqual(@"{""layout"":""trail-by-place"",""ground"":""g"",""hand"":[]}",
                Json(OWMapSpace.LayoutOf(map, new[] { map, ground }, new JObject[0])));
        }

        [Test]
        public void AGroundMapThatIsNoId_IsNoGround()
        {
            var ground = Row(@"{""type"":""map"",""id"":""g""}");
            foreach (var named in new[] { @"""""", "5", "true", "null", @"[""g""]", @"{""id"":""g""}" })
            {
                var map = Row(@"{""type"":""map"",""id"":""m"",""x_layout"":{""method"":""trail-by-place"",""settings"":{""ground"":{""map"":" + named + "}}}}");
                Assert.IsNull(OWMapSpace.LayoutOf(map, new[] { map, ground }, new JObject[0]).Ground, named);
            }
        }
    }
}
