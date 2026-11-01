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
    /// The record profile's conformance fixture (schema-dist <c>profiles/record.fixture.json</c>, vendored byte for byte
    /// in <c>Tests/Editor/Fixtures/record/</c> from dist.102), replayed case by case: <see cref="OWRecord.MarkOf"/> on each
    /// mark case, <see cref="OWRecord.Of"/> on each records case. keel's reader, the TypeScript SDK (4.124.0) and the
    /// Python package (0.100.0) answer the same cases; the tests beside the fixture are the TypeScript SDK's own
    /// (<c>test/record.test.mjs</c>). When the dist moves the fixture, re-vendor it and update the hash below.
    /// </summary>
    public class OWRecordTests
    {
        /// <summary>The fixture's sha256 as dist.102's MANIFEST.json records it.</summary>
        private const string DistSha256 = "eff88749296799cb27643ba3a189b1cb128b39f37395c723c38e774ded20c571";

        private static JObject _fixture;

        private static string FixturePath
        {
            get
            {
                var root = TestPackage.Root();
                return root == null ? null : Path.Combine(root, "Tests", "Editor", "Fixtures", "record", "record.fixture.json");
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
                yield return new TestCaseData(string.Empty).SetName("Record_FixtureNotFound");
                yield break;
            }

            foreach (var c in fixture["cases"].Cast<JObject>())
            {
                var name = (string)c["name"];
                yield return new TestCaseData(name).SetName("Record_" + name.Replace('-', '_'));
            }
        }

        [Test]
        public void TheFixture_IsTheDistsBytes()
        {
            var path = FixturePath;
            Assert.IsTrue(path != null && File.Exists(path), "the vendored record fixture was not found: " + path);
            using var sha = SHA256.Create();
            var hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
            Assert.AreEqual(DistSha256, hash,
                "the vendored fixture is not dist.102's: re-vendor it from schema-dist (a checkout's line endings change the bytes)");
        }

        [Test]
        public void TheFixture_HoldsDist102sCases_AndItsMarkIsTheReaders()
        {
            Assert.IsNotNull(Fixture, "the vendored record fixture was not found");
            Assert.AreEqual("record", (string)Fixture["profile"]);
            Assert.AreEqual((string)Fixture["mark"]["field"], OWRecord.MarkField);
            CollectionAssert.AreEqual(Fixture["mark"]["members"].Select(m => (string)m).ToList(), OWRecord.Members.ToList());
            Assert.AreEqual(32, Fixture["cases"].Count(), "dist.102's fixture holds 32 cases");
        }

        [TestCaseSource(nameof(Cases))]
        public void EveryCase_IsAnsweredAsTheProfileSays(string name)
        {
            Assert.IsNotNull(Fixture, "the vendored record fixture was not found");
            var c = Fixture["cases"].Cast<JObject>().Single(x => (string)x["name"] == name);
            var elements = (JArray)Fixture["worlds"][(string)c["world"]]["elements"];
            var want = c["want"];
            JToken got;
            switch ((string)c["kind"])
            {
                case "mark":
                    var el = elements.Cast<JObject>().SingleOrDefault(e => (string)e["id"] == (string)c["element"]);
                    Assert.IsNotNull(el, $"{name}: no element {c["element"]}");
                    got = OWRecord.MarkOf(el)?.ToJson() ?? (JToken)JValue.CreateNull();
                    break;
                case "records":
                    got = new JArray(OWRecord.Of((string)c["character"], elements).Select(r => (object)r.ToJson()));
                    break;
                default:
                    Assert.Fail($"case {name}: a case of unknown kind {c["kind"]}");
                    return;
            }
            Assert.IsTrue(JToken.DeepEquals(want, got),
                $"{name}:\n got  {got.ToString(Formatting.None)}\n want {want.ToString(Formatting.None)}");
        }

        // -- Beside the fixture: the TypeScript SDK's own vectors ---------------------------

        private static JObject Run(string id, string playedAt, params string[] characters) => new JObject
        {
            ["id"] = id,
            ["type"] = "event",
            ["name"] = id,
            ["characters"] = new JArray(characters.Length == 0 ? new object[] { "c" } : characters.Cast<object>().ToArray()),
            ["x_record"] = new JObject { ["tool"] = "subs", ["played_at"] = playedAt },
        };

        /// <summary>The reader is pure: the elements are not moved, and what it returns is the caller's own.</summary>
        [Test]
        public void Of_IsPure()
        {
            var elements = (JArray)Fixture["worlds"]["harbour"]["elements"];
            var before = elements.ToString(Formatting.None);
            var who = (string)Fixture["cases"].Cast<JObject>().First(c => (string)c["kind"] == "records" && ((JArray)c["want"]).Count > 0)["character"];
            var got = OWRecord.Of(who, elements);
            var json = got[0].ToJson();
            json["event"]["name"] = "planted";
            ((JArray)json["stories"]).Add(new JObject { ["id"] = "x" });
            OWRecord.MarkOf(elements.First(e => OWRecord.MarkOf(e) != null)).ToJson()["seats"]["planted"] = "planted";
            Assert.AreEqual(before, elements.ToString(Formatting.None));
            Assert.AreNotEqual("planted", OWRecord.Of(who, elements)[0].EventName);
        }

        /// <summary>No id or no list is refused; an empty world reads no records; a name like an object's own machinery
        /// reads no seat.</summary>
        [Test]
        public void Of_RefusesNoIdOrNoList_AndReadsNoSeatFromMachinery()
        {
            Assert.Throws<ArgumentNullException>(() => OWRecord.Of(null, new JArray()));
            Assert.Throws<ArgumentNullException>(() => OWRecord.Of("a", null));
            Assert.AreEqual(0, OWRecord.Of("a", new JArray()).Count);
            var ev = Run("e", "2026-11-01T00:00:00Z", "toString", "constructor");
            foreach (var who in new[] { "toString", "constructor" }) Assert.IsNull(OWRecord.Of(who, new[] { ev })[0].Seat);
        }

        /// <summary>An Event or a story whose id is not a string is never read (the page is silent; keel and the Python
        /// package raise, npm emits an entry with no id; the Godot and Unreal SDKs skip it, as here).</summary>
        [Test]
        public void WhatCannotBeNamed_IsNeverRead()
        {
            var nameless = Run("x", "2026-11-01T00:00Z");
            nameless.Remove("id");
            var numbered = Run("x", "2026-11-01T00:00Z");
            numbered["id"] = 7;
            var ev = Run("e", "2026-11-01T00:00Z");
            var story = new JObject { ["type"] = "narrative", ["events"] = new JArray("e"), ["narrator"] = "c", ["name"] = "S" };
            var got = OWRecord.Of("c", new JToken[] { nameless, numbered, ev, story });
            CollectionAssert.AreEqual(new[] { "e" }, got.Select(r => r.EventId).ToArray());
            Assert.AreEqual(0, got[0].Stories.Count, "a story with no id is not one");
        }

        /// <summary>Ties order by code point, never UTF-16: an id above the BMP sorts after U+FF00 (npm 4.124.2's vector;
        /// string.CompareOrdinal would put the surrogate pair first).</summary>
        [Test]
        public void Ties_OrderByCodePoint_NeverUtf16()
        {
            const string hi = "e\U0001F600", bmp = "e＀";
            var story = new Func<string, string, JObject>((id, name) => new JObject
            {
                ["id"] = id, ["type"] = "narrative", ["name"] = name, ["protagonist"] = "c", ["events"] = new JArray(hi),
            });
            var got = OWRecord.Of("c", new JToken[]
            {
                Run(hi, "2026-11-01T00:00:00Z"), Run(bmp, "2026-11-01T00:00:00Z"),
                story("n\U0001F600", "a"), story("n＀", "b"),
            });
            CollectionAssert.AreEqual(new[] { bmp, hi }, got.Select(r => r.EventId).ToArray());
            CollectionAssert.AreEqual(new[] { "n＀", "n\U0001F600" }, got[1].Stories.Select(s => s.Id).ToArray());
        }

        /// <summary>A Narrative naming an Event twice is one story of it, other values in events name none (npm 4.124.1's
        /// vector; npm's third clause, that no two records share a mutable story, holds here by a story being immutable).</summary>
        [Test]
        public void ANarrativeNamingAnEventTwice_IsOneStory_AndOtherValuesNameNone()
        {
            const string c = "019a8000-0000-7000-8000-000000000101";
            var events = new JArray(7, JValue.CreateNull(), new JObject(), new JArray("e1"), "e1");
            var got = OWRecord.Of(c, new JToken[]
            {
                Run("e1", "2026-11-02T00:00:00Z", c), Run("e2", "2026-11-01T00:00:00Z", c),
                new JObject { ["id"] = "n1", ["type"] = "narrative", ["name"] = "One", ["protagonist"] = c, ["events"] = new JArray("e1", "e1", "e2") },
                new JObject { ["id"] = "n2", ["type"] = "narrative", ["name"] = "Two", ["narrator"] = c, ["events"] = events },
            });
            CollectionAssert.AreEqual(new[] { "e1", "e2" }, got.Select(r => r.EventId).ToArray());
            CollectionAssert.AreEqual(new[] { "n1:protagonist", "n2:narrator" }, got[0].Stories.Select(s => s.Id + ":" + s.As).ToArray());
            CollectionAssert.AreEqual(new[] { "n1:protagonist" }, got[1].Stories.Select(s => s.Id + ":" + s.As).ToArray());
        }

        [Test]
        public void PlayedAt_WithSecondsAndAFraction_OrdersAsTheInstantItIs()
        {
            var got = OWRecord.Of("c", new[]
            {
                Run("a", "2026-11-01T00:30Z"), Run("b", "2026-11-01T00:30:00.500Z"), Run("c", "2026-11-01T00:29:59Z"),
            });
            CollectionAssert.AreEqual(new[] { "b", "a", "c" }, got.Select(r => r.EventId).ToArray());
        }

        [Test]
        public void PlayedAt_OrdersToTheMillisecond_AFinerFractionTruncated()
        {
            var got = OWRecord.Of("c", new[] { Run("b", "2026-11-01T00:30:00.1239Z"), Run("a", "2026-11-01T00:30:00.1231Z") });
            CollectionAssert.AreEqual(new[] { "a", "b" }, got.Select(r => r.EventId).ToArray());
        }

        [Test]
        public void PlayedAt_ThatIsNoRealCalendarInstant_IsNoMark()
        {
            foreach (var at in new[]
                     {
                         "2026-13-01T00:00Z", "2026-02-30T00:00Z", "2026-02-29T00:00Z", "2026-11-01T24:00Z", "2026-11-01T00:60Z",
                         "2026-11-01T00:00:60Z", "0000-01-01T00:00Z", "2026-00-01T00:00Z", "2026-11-00T00:00Z",
                         // Beside npm's: a fraction with no seconds, an empty fraction, and digits that are not ASCII.
                         "2026-11-01T00:00.5Z", "2026-11-01T00:00:00.Z", "２026-11-01T00:00Z",
                     })
                Assert.IsNull(OWRecord.MarkOf(Run("e", at)), at);
            foreach (var at in new[] { "2028-02-29T00:00Z", "2000-02-29T23:59:59.999Z", "0001-01-01T00:00Z" })
                Assert.IsNotNull(OWRecord.MarkOf(Run("e", at)), at);
        }
    }
}
