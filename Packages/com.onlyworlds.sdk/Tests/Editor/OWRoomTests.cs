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
    /// The room profile's conformance fixture (schema-dist <c>profiles/room.fixture.json</c>, vendored byte for byte in
    /// <c>Tests/Editor/Fixtures/room/</c> from dist.71: the ninth pair's two cases at serial 66, the three trim cases at 71), read case by case through
    /// <see cref="OWRoom.IsRoomElement"/>, and its pairs held equal to <see cref="OWRoom.Pairs"/>. The TypeScript SDK
    /// and the Python package run the same cases; the tests beside the fixture are the TypeScript SDK's (test/room.test.mjs,
    /// 4.57.2). When the dist moves the fixture, re-vendor it and update the hash below.
    /// </summary>
    public class OWRoomTests
    {
        /// <summary>The fixture's sha256 as dist.71's MANIFEST.json records it.</summary>
        private const string DistSha256 = "4e74c04a16e30a8be54ea77f4fcb65baadacc2459dc35f164b5360ffa9131919";

        private const string World = "0199c3a1-7b2e-7c3d-8e4f-5a6b7c8d9e0f";

        private static JObject _fixture;

        private static string FixturePath
        {
            get
            {
                var root = TestPackage.Root();
                return root == null ? null : Path.Combine(root, "Tests", "Editor", "Fixtures", "room", "room.fixture.json");
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

        /// <summary>One case per fixture case, named after it.</summary>
        public static IEnumerable<TestCaseData> Cases()
        {
            var fixture = Fixture;
            if (fixture == null)
            {
                yield return new TestCaseData(string.Empty).SetName("Room_FixtureNotFound");
                yield break;
            }

            foreach (var c in fixture["cases"].Cast<JObject>())
            {
                var name = (string)c["name"];
                yield return new TestCaseData(name).SetName("Room_" + name.Replace('-', '_'));
            }
        }

        [Test]
        public void TheFixture_IsTheDistsBytes()
        {
            var path = FixturePath;
            Assert.IsTrue(path != null && File.Exists(path), "the vendored room fixture was not found: " + path);
            using var sha = SHA256.Create();
            var hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
            Assert.AreEqual(DistSha256, hash,
                "the vendored fixture is not dist.71's: re-vendor it from schema-dist (a checkout's line endings change the bytes)");
        }

        [Test]
        public void ThePairs_AreTheFixturesInItsOrder_AndTheMarkIsXRoomOf()
        {
            Assert.IsNotNull(Fixture, "the vendored room fixture was not found");
            Assert.AreEqual("room", (string)Fixture["profile"]);
            var pairs = Fixture["pairs"].Cast<JObject>().Select(p => ((string)p["type"], (string)p["supertype"])).ToList();
            CollectionAssert.AreEqual(pairs, OWRoom.Pairs.ToList());
            Assert.AreEqual(9, OWRoom.Pairs.Count);
            Assert.AreEqual(OWRoom.MarkField, (string)Fixture["mark"]["field"]);
            Assert.GreaterOrEqual(((JArray)Fixture["cases"]).Count, 28, "dist.71's fixture holds 28 cases");
        }

        [TestCaseSource(nameof(Cases))]
        public void EveryCase_IsInTheRoomAsTheProfileSays(string name)
        {
            Assert.IsNotNull(Fixture, "the vendored room fixture was not found");
            var c = Fixture["cases"].Cast<JObject>().Single(x => (string)x["name"] == name);

            Assert.AreEqual((bool)c["in_room"], OWRoom.IsRoomElement((JObject)c["element"]), $"case {name}: {c["element"]}");
        }

        // -- Beside the fixture (the TypeScript SDK's own tests) -------------------

        private static JObject El(object type, object supertype, object mark)
        {
            var el = new JObject();
            if (type != null) el["type"] = JToken.FromObject(type);
            if (supertype != null) el["supertype"] = JToken.FromObject(supertype);
            if (mark != null) el[OWRoom.MarkField] = JToken.FromObject(mark);
            return el;
        }

        /// <summary>One code point as a string, so an invisible one is named in the source.</summary>
        private static string U(int codePoint) => char.ConvertFromUtf32(codePoint);

        [Test]
        public void AnElementWithoutAStringTypeSupertypeOrMark_IsNotInTheRoom()
        {
            Assert.IsFalse(OWRoom.IsRoomElement(null));
            Assert.IsFalse(OWRoom.IsRoomElement(new JObject()));
            Assert.IsFalse(OWRoom.IsRoomElement(El("narrative", null, World)));
            Assert.IsFalse(OWRoom.IsRoomElement(El(null, "Message", World)));
            Assert.IsFalse(OWRoom.IsRoomElement(El("narrative", "   ", World)));
            Assert.IsFalse(OWRoom.IsRoomElement(El("narrative", "Message", "")));
            Assert.IsTrue(OWRoom.IsRoomElement(El("narrative", "Message", World)));
        }

        /// <summary>
        /// The mark's five shapes (the schema steward's ruling for serial 83; its fixture is not yet published): a mark
        /// counts by presence, as the API reads it. Any JSON value but null counts, and a string only when something is
        /// left after trimming White_Space; the value is never compared. Each case is the mark as JSON and whether it
        /// marks. Shared by every reader of the room mark (<see cref="OWGraphTests"/>, <see cref="OWLensTests"/>).
        /// </summary>
        public static IEnumerable<TestCaseData> MarkShapes()
        {
            yield return new TestCaseData("7", true); // a number
            yield return new TestCaseData("\"\"", false); // an empty string
            yield return new TestCaseData("\"  \"", false); // White_Space only
            yield return new TestCaseData("\"\\u001f\"", true); // U+001F is not White_Space
            yield return new TestCaseData("\"  " + World + "\\t\"", true); // an id padded, never compared
        }

        /// <summary>
        /// One rule for both marks: <c>x_room_of</c> read as <c>x_charter_of</c> is. Kills: a number read as no room mark,
        /// a blank string read as one, a language's own trim (Python's <c>strip()</c> takes U+001F) for White_Space, the
        /// padded value compared or trimmed away.
        /// </summary>
        [TestCaseSource(nameof(MarkShapes))]
        public void BothMarks_CountByPresence(string markJson, bool marks)
        {
            var mark = JToken.Parse(markJson);
            Assert.AreEqual(marks, OWRoom.IsRoomElement(El("narrative", "Message", mark)), "x_room_of " + markJson);
            Assert.AreEqual(marks, OWRoom.IsCharterElement(Aid("law", "Charter", mark)), "x_charter_of " + markJson);
        }

        [Test]
        public void CaseFolds_ByUnicodeSimpleCaseFolding()
        {
            // U+017F LATIN SMALL LETTER LONG S simple-folds to "s"; ToLowerInvariant leaves it.
            var longS = U(0x17F);
            Assert.IsTrue(OWRoom.IsRoomElement(El("NARRATIVE", "Me" + longS + longS + "age", World)));
            Assert.IsTrue(OWRoom.IsRoomElement(El("Relation", "\tWORK ITEM\n", World)));
        }

        [Test]
        public void Trims_UnicodeWhiteSpaceAtBothEnds_AndNothingElse()
        {
            var ideographic = U(0x3000);
            var noBreak = U(0xA0);
            var paragraph = U(0x2029);
            var bom = U(0xFEFF);
            var zeroWidth = U(0x200B);

            Assert.IsTrue(OWRoom.IsRoomElement(El("narrative", " Message" + ideographic, World)));
            Assert.IsTrue(OWRoom.IsRoomElement(El(noBreak + "narrative" + paragraph, "Message", World)));
            // A byte order mark and a zero width space are not White_Space: they stay, so the word is not the pair's.
            Assert.IsFalse(OWRoom.IsRoomElement(El("narrative", bom + "Message", World)));
            Assert.IsFalse(OWRoom.IsRoomElement(El(bom + "narrative", "Message", World)));
            Assert.IsFalse(OWRoom.IsRoomElement(El("narrative", zeroWidth + "Message", World)));
            // Nothing inside the value is touched.
            Assert.IsFalse(OWRoom.IsRoomElement(El("relation", "Work" + ideographic + "item", World)));
        }

        [Test]
        public void ThePairIsTheWholeWord()
        {
            Assert.IsFalse(OWRoom.IsRoomElement(El("narrative", "Messages", World)));
            Assert.IsFalse(OWRoom.IsRoomElement(El("law", "Charter", World)));
            Assert.IsFalse(OWRoom.IsRoomElement(El("narrative", "Spec", World)), "a pair is a type and a supertype together");
        }

        private static JObject Aid(object type, object supertype, object mark)
        {
            var el = new JObject();
            if (type != null) el["type"] = JToken.FromObject(type);
            if (supertype != null) el["supertype"] = JToken.FromObject(supertype);
            if (mark != null) el[OWRoom.ReadingAidMarkField] = JToken.FromObject(mark);
            return el;
        }

        /// <summary>
        /// The reading aids (room profile, serial 82; the lens profile's rule 4): the charter, its guide and its domains,
        /// by the pair AND the mark <c>x_charter_of</c>. Kills: the pair alone read as an aid (a novel's Charter is lore),
        /// the room's mark read for an aid's, a mark without a pair, a supertype compared as written.
        /// </summary>
        [Test]
        public void AReadingAid_IsThePairAndTheMark_AndNeverARoomElement()
        {
            foreach (var (type, supertype) in OWRoom.ReadingAidPairs)
            {
                Assert.IsTrue(OWRoom.IsCharterElement(Aid(type, supertype, World)), $"{type}/{supertype}");
                Assert.IsFalse(OWRoom.IsCharterElement(Aid(type, supertype, null)), $"{type}/{supertype} without the mark is lore");
                Assert.IsFalse(OWRoom.IsCharterElement(Aid(type, supertype, "")), $"{type}/{supertype} with an empty mark");
                Assert.IsFalse(OWRoom.IsRoomElement(Aid(type, supertype, World)), $"{type}/{supertype} is not a room element");
                Assert.IsFalse(OWRoom.IsCharterElement(El(type, supertype, World)), $"{type}/{supertype} with the room's mark");
            }

            Assert.IsTrue(OWRoom.IsCharterElement(Aid(" LAW ", "charter" + U(0x3000), World)), "trimmed and folded");
            Assert.IsFalse(OWRoom.IsCharterElement(Aid("law", "Charters", World)), "the whole word");
            Assert.IsFalse(OWRoom.IsCharterElement(Aid("construct", "Charter", World)), "a pair is a type and a supertype together");
            Assert.IsFalse(OWRoom.IsCharterElement(Aid("construct", "Lens", World)), "a mark without a pair");
            Assert.IsFalse(OWRoom.IsCharterElement(null));
        }

        /// <summary>
        /// The TypeScript SDK 4.98.0's vector for <c>isCharterElement</c> (<c>test/lens.test.mjs</c>; the Python package
        /// 0.80.0 holds the same): the mark counts when it is present and not null, and a string mark only when something
        /// is left after trimming White_Space. Kills: a number read as no mark, a blank string read as a mark, a JSON null
        /// read as a mark, the room's mark read for this one.
        /// </summary>
        [Test]
        public void ACharterElements_Mark_CountsWhenPresentAndNotNull_AStringOnlyWhenNotBlank()
        {
            Assert.IsTrue(OWRoom.IsCharterElement(JObject.Parse(@"{""id"":""rules"",""type"":""law"",""supertype"":""Charter"",""x_charter_of"":""w1""}")));
            Assert.IsFalse(OWRoom.IsCharterElement(JObject.Parse(@"{""id"":""charter"",""type"":""law"",""supertype"":"" charter ""}")), "the pair without the mark: lore");
            Assert.IsTrue(OWRoom.IsCharterElement(JObject.Parse(@"{""type"":""construct"",""supertype"":""Domain"",""x_charter_of"":""w1""}")));
            Assert.IsTrue(OWRoom.IsCharterElement(JObject.Parse(@"{""type"":""construct"",""supertype"":""GUIDE"",""x_charter_of"":""w1""}")), "folded");
            Assert.IsFalse(OWRoom.IsCharterElement(Aid("law", "Charters", "w1")), "the whole word");
            Assert.IsFalse(OWRoom.IsCharterElement(Aid("object", "Charter", "w1")));
            Assert.IsFalse(OWRoom.IsCharterElement(Aid("law", "Charter", "")), "an empty string");
            Assert.IsFalse(OWRoom.IsCharterElement(Aid("law", "Charter", " " + U(0x3000))), "a blank string: White_Space trimmed");
            Assert.IsFalse(OWRoom.IsCharterElement(Aid("law", "Charter", "  ")), "two spaces");
            Assert.IsFalse(OWRoom.IsCharterElement(JObject.Parse(@"{""type"":""law"",""supertype"":""Charter"",""x_charter_of"":null}")), "a JSON null");
            Assert.IsTrue(OWRoom.IsCharterElement(Aid("law", "Charter", 7)), "a number is a mark");
            Assert.IsTrue(OWRoom.IsCharterElement(Aid("law", "Charter", " w1 ")), "a string with something left after trimming");
            Assert.IsTrue(OWRoom.IsCharterElement(Aid("law", "Charter", U(0xFEFF))), "a byte order mark is not White_Space");
            Assert.IsFalse(OWRoom.IsCharterElement(El("law", "Charter", "w1")), "the room's mark is not this one");
        }
    }
}
