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
    /// The map-space profile's conformance fixture (schema-dist <c>profiles/map-space.fixture.json</c>, vendored byte for
    /// byte in <c>Tests/Editor/Fixtures/map-space/</c> from dist.106), replayed whole, case by case by name, through
    /// <see cref="OWMapSpace.Of"/>: each case's elements as one reader is served them, and its <c>want</c>, the whole
    /// answer with its keys in order. The TypeScript SDK's <c>mapSpace</c> and the Python package's <c>map_space</c>
    /// replay the same cases. When the dist moves the fixture, re-vendor it and update the hash and the count below.
    /// </summary>
    public class OWMapSpaceTests
    {
        /// <summary>The fixture's sha256 as dist.106's MANIFEST.json records it.</summary>
        private const string DistSha256 = "7622c9cc44f7254481b29add8846e2077e431353f993e8fc95702d7a530279af";

        /// <summary>The fixture's cases, as dist.106 holds them.</summary>
        private const int CaseCount = 34;

        private static JObject _fixture;

        private static string FixturePath
        {
            get
            {
                var root = TestPackage.Root();
                return root == null ? null : Path.Combine(root, "Tests", "Editor", "Fixtures", "map-space", "map-space.fixture.json");
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
                yield return new TestCaseData(string.Empty).SetName("MapSpace_FixtureNotFound");
                yield break;
            }

            var i = 0;
            foreach (var c in fixture["cases"].Cast<JObject>())
                yield return new TestCaseData((string)c["name"]).SetName($"MapSpace_{++i:D2}");
        }

        [Test]
        public void TheFixture_IsTheDistsBytes()
        {
            var path = FixturePath;
            Assert.IsTrue(path != null && File.Exists(path), "the vendored map-space fixture was not found: " + path);
            using var sha = SHA256.Create();
            var hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
            Assert.AreEqual(DistSha256, hash,
                "the vendored fixture is not dist.106's: re-vendor it from schema-dist (a checkout's line endings change the bytes)");
        }

        [Test]
        public void TheFixture_HoldsDist106sCases_EachNamedOnce()
        {
            Assert.IsNotNull(Fixture, "the vendored map-space fixture was not found");
            Assert.AreEqual("map-space", (string)Fixture["profile"]);
            var names = Fixture["cases"].Select(c => (string)c["name"]).ToList();
            Assert.AreEqual(CaseCount, names.Count, "dist.106's fixture holds 34 cases");
            Assert.AreEqual(names.Count, names.Distinct().Count(), "a case is found by its name, so each name is once");
        }

        [TestCaseSource(nameof(Cases))]
        public void EveryCase_IsAnsweredAsTheProfileSays(string name)
        {
            Assert.IsNotNull(Fixture, "the vendored map-space fixture was not found");
            var c = Fixture["cases"].Cast<JObject>().Single(x => (string)x["name"] == name);
            var got = OWMapSpace.Of((JArray)c["elements"]).ToJson();
            var want = (JObject)c["want"];
            CollectionAssert.AreEqual(new[] { "maps", "zones", "pins", "out" },
                got.Properties().Select(p => p.Name).ToList(), name + ": all four keys, in order");
            foreach (var p in want.Properties())
                Assert.AreEqual(p.Value.ToString(Formatting.None), got[p.Name].ToString(Formatting.None), $"{name}: {p.Name}");
            Assert.AreEqual(want.ToString(Formatting.None), got.ToString(Formatting.None), name + ": the whole answer");
        }

        /// <summary>The same elements give the same answer, byte for byte, and the elements are not changed.</summary>
        [Test]
        public void TheSameElements_GiveTheSameBytes_AndAreNotChanged()
        {
            Assert.IsNotNull(Fixture, "the vendored map-space fixture was not found");
            foreach (var c in Fixture["cases"].Cast<JObject>())
            {
                var elements = (JArray)c["elements"];
                var before = elements.ToString(Formatting.None);
                var once = OWMapSpace.Of(elements).ToJson().ToString(Formatting.None);
                var twice = OWMapSpace.Of(elements).ToJson().ToString(Formatting.None);
                Assert.AreEqual(once, twice, (string)c["name"]);
                Assert.AreEqual(before, elements.ToString(Formatting.None), (string)c["name"] + ": the elements moved");
            }
        }

        /// <summary>
        /// The typed answer says what its JSON says: a measured width, a z past depth in <c>ZMax</c>, a Pin with one key
        /// read as neither, a scale as a double, the reasons counted by name.
        /// </summary>
        [Test]
        public void TheTypedAnswer_MatchesItsJson()
        {
            var world = new JArray(
                JObject.Parse(@"{""type"":""map"",""id"":""m1"",""height"":50,""depth"":10,""x_metres_per_pixel"":0.05}"),
                JObject.Parse(@"{""type"":""zone"",""id"":""z1"",""shape"":""line""}"),
                JObject.Parse(@"{""type"":""zone"",""id"":""z2""}"),
                JObject.Parse(@"{""type"":""marker"",""id"":""k2"",""zone"":""z1"",""map"":""m1"",""x"":40,""y"":4,""z"":30}"),
                JObject.Parse(@"{""type"":""marker"",""id"":""k1"",""zone"":""z1"",""map"":""m1"",""x"":10,""y"":2,""order"":0}"),
                JObject.Parse(@"{""type"":""marker"",""id"":""k3"",""zone"":""z9"",""map"":""m1"",""x"":99,""y"":99}"),
                JObject.Parse(@"{""type"":""pin"",""id"":""p1"",""map"":""m1"",""x"":5,""y"":5,""element_type"":""character""}"),
                JObject.Parse(@"{""type"":""pin"",""id"":""p2"",""map"":""m9"",""x"":5,""y"":5}"));
            var space = OWMapSpace.Of(world);

            var map = space.Maps.Single();
            Assert.AreEqual(40, map.Width, "measured from the Map's own points, never a dangling Marker's");
            Assert.AreEqual(50, map.Height);
            Assert.IsTrue(map.Measured);
            Assert.AreEqual(10, map.Depth);
            Assert.AreEqual(30, map.ZMax, "a z past depth is kept and shows here");
            Assert.AreEqual(0.05, map.Scale);

            var outline = space.Zones.Single();
            Assert.AreEqual(OWMapSpaceShape.Line, outline.Shape);
            CollectionAssert.AreEqual(new[] { "[10, 2, 0]", "[40, 4, 30]" }, outline.Points.Select(p => p.ToString()),
                "a stated order before an unstated one");

            var pin = space.Pins.Single();
            Assert.IsNull(pin.ElementType, "one key alone reads both null");
            Assert.IsNull(pin.ElementId);

            Assert.AreEqual(1, space.Out[OWMapSpaceReason.NoMarkers]);
            Assert.AreEqual(1, space.Out[OWMapSpaceReason.Dangling]);
            Assert.AreEqual(1, space.Out[OWMapSpaceReason.NoMap]);
            Assert.IsFalse(space.Out.ContainsKey(OWMapSpaceReason.NoPoint), "a reason with nothing counted is absent");
            Assert.AreEqual(@"{""no_markers"":1,""dangling"":1,""no_map"":1}", space.ToJson()["out"].ToString(Formatting.None));

        }

        /// <summary>
        /// The page's readings A and J (since dist.106 also the fixture's <c>marker-dangling-before-no-point</c> and
        /// <c>pin-no-map-before-no-point</c>): a row both unplaced and without a point counts once, by what is served
        /// before its shape (a Marker <c>dangling</c>, a Pin <c>no_map</c>), never <c>no_point</c>.
        /// </summary>
        [Test]
        public void ReadingA_ARowNotPlacedAndWithoutAPoint_CountsOnce_ByWhatIsServed()
        {
            var world = new JArray(
                JObject.Parse(@"{""type"":""map"",""id"":""m1"",""width"":10,""height"":10}"),
                JObject.Parse(@"{""type"":""zone"",""id"":""z1""}"),
                JObject.Parse(@"{""type"":""marker"",""id"":""k1"",""zone"":""z1"",""map"":""m1"",""x"":1,""y"":1}"),
                JObject.Parse(@"{""type"":""marker"",""id"":""k2"",""zone"":""z9"",""map"":""m1"",""y"":1}"),
                JObject.Parse(@"{""type"":""marker"",""id"":""k3"",""zone"":""z1"",""map"":""m9"",""x"":1.5,""y"":1}"),
                JObject.Parse(@"{""type"":""pin"",""id"":""p1"",""map"":""m9"",""x"":""1"",""y"":1}"));
            Assert.AreEqual(@"{""dangling"":2,""no_map"":1}", OWMapSpace.Of(world).ToJson()["out"].ToString(Formatting.None));
        }

        /// <summary>No elements is no reading; an empty world reads empty, every key present.</summary>
        [Test]
        public void AnEmptyWorld_ReadsEmpty_AndNoElementsIsRefused()
        {
            Assert.Throws<ArgumentNullException>(() => OWMapSpace.Of(null));
            Assert.AreEqual(@"{""maps"":[],""zones"":[],""pins"":[],""out"":{}}",
                OWMapSpace.Of(new JArray()).ToJson().ToString(Formatting.None));
        }

        // -- Whole numbers past 2^53: a reader holds whole numbers only where it holds them exactly ------------------

        /// <summary>The TypeScript SDK's vector (its <c>test/map-space.test.mjs</c>, 4.129.2), as JSON text, with <c>{0}</c> to <c>{4}</c> for its numbers.</summary>
        private const string PastSafeVector = @"{{""elements"": [
    {{""type"": ""map"", ""id"": ""m"", ""width"": 10, ""height"": 10}},
    {{""type"": ""zone"", ""id"": ""z""}},
    {{""type"": ""marker"", ""id"": ""k1"", ""map"": ""m"", ""zone"": ""z"", ""x"": 1, ""y"": 1, ""z"": {0}}},
    {{""type"": ""marker"", ""id"": ""k2"", ""map"": ""m"", ""zone"": ""z"", ""x"": {1}, ""y"": 2}},
    {{""type"": ""marker"", ""id"": ""k3"", ""map"": ""m"", ""zone"": ""z"", ""x"": 3, ""y"": 3, ""z"": {2}}},
    {{""type"": ""pin"", ""id"": ""p1"", ""map"": ""m"", ""x"": {3}, ""y"": 1}}
  ]}}";

        private static OWMapSpace ReadPastSafe(params string[] numbers) =>
            OWMapSpace.Of((JArray)OWJson.ParseObject(string.Format(PastSafeVector, numbers))["elements"]);

        /// <summary>
        /// The vector with a fraction written: each number is a double token, and a double past 2^53 - 1 may be a value
        /// the parse had to round (9007199254740993.0 reads as 2^53, -9223372036854775809.0 as -2^63), so it is not
        /// whole, as the TypeScript SDK reads it. 2^53 - 1 is the largest a double holds exactly: whole. k2's x and p1's
        /// x are past it: no point. k3's z is past it: read as 0, as any z that is not whole, and the point stays placed.
        /// </summary>
        [Test]
        public void ADoublePast2To53_IsNotWhole_ReadFromJsonText()
        {
            var got = ReadPastSafe("9007199254740991.0", "-9223372036854775809.0", "9007199254740993.0", "9007199254740992.0");
            Assert.AreEqual(@"[{""zone"":""z"",""map"":""m"",""shape"":""polygon"",""points"":[[1,1,9007199254740991],[3,3,0]]}]",
                got.ToJson()["zones"].ToString(Formatting.None));
            Assert.AreEqual("[]", got.ToJson()["pins"].ToString(Formatting.None));
            Assert.AreEqual(@"{""no_point"":2}", got.ToJson()["out"].ToString(Formatting.None));
            Assert.AreEqual(9007199254740991L, got.Maps[0].ZMax);
        }

        /// <summary>The same with an exponent, as some writers spell a large double: 2^53 is past the bound, 2^53 - 1 is not.</summary>
        [Test]
        public void ADoublePast2To53_IsNotWhole_WrittenWithAnExponent()
        {
            var got = ReadPastSafe("9.007199254740991e15", "-9.223372036854775809e18", "9.007199254740993e15", "9.007199254740992e15");
            Assert.AreEqual(@"[[1,1,9007199254740991],[3,3,0]]", got.ToJson()["zones"][0]["points"].ToString(Formatting.None));
            Assert.AreEqual(0, got.Pins.Count);
            Assert.AreEqual(2, got.Out[OWMapSpaceReason.NoPoint]);
            var edge = ReadPastSafe("-9007199254740991.0", "-9007199254740991.0", "1.0", "-9.007199254740991e15");
            Assert.AreEqual(@"[[1,1,-9007199254740991],[-9007199254740991,2,0],[3,3,1]]", edge.ToJson()["zones"][0]["points"].ToString(Formatting.None),
                "-(2^53 - 1) is whole");
            Assert.AreEqual(1, edge.Pins.Count, "-(2^53 - 1) is whole");
        }

        /// <summary>
        /// The vector exactly as the TypeScript SDK writes it, integers with no fraction. Here a JSON integer is read as
        /// written, exactly, to 64 bits (Newtonsoft gives a long; past it a BigInteger, which is not whole): this reader's
        /// integer range is what it holds exactly, so 2^53 and 2^53 + 1 are whole here, where a double reader rounds them.
        /// Only k2's x, past 64 bits, has no point.
        /// </summary>
        [Test]
        public void AnIntegerPast2To53_IsReadExactly_To64Bits_ReadFromJsonText()
        {
            var got = ReadPastSafe("9007199254740991", "-9223372036854775809", "9007199254740993", "9007199254740992");
            Assert.AreEqual(@"[[1,1,9007199254740991],[3,3,9007199254740993]]", got.ToJson()["zones"][0]["points"].ToString(Formatting.None));
            Assert.AreEqual(@"[{""pin"":""p1"",""map"":""m"",""element_type"":null,""element_id"":null,""x"":9007199254740992,""y"":1,""z"":0}]",
                got.ToJson()["pins"].ToString(Formatting.None));
            Assert.AreEqual(@"{""no_point"":1}", got.ToJson()["out"].ToString(Formatting.None));
            Assert.AreEqual(9007199254740993L, got.Maps[0].ZMax);
            var edges = ReadPastSafe("9223372036854775807", "-9223372036854775808", "9223372036854775808", "1");
            Assert.AreEqual(@"[[1,1,9223372036854775807],[-9223372036854775808,2,0],[3,3,0]]", edges.ToJson()["zones"][0]["points"].ToString(Formatting.None),
                "int64's own edges are whole; one past them is not (k3's z read as 0)");
        }
    }
}
