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
    /// The report profile's threads fixture (schema-dist <c>profiles/report.fixture.json</c>, rule 9, vendored byte for
    /// byte in <c>Tests/Editor/Fixtures/report/</c> from dist.120), replayed case by case by name through
    /// <see cref="OWReport.ThreadsOf"/>: each case's narratives as one reader is given them, and its <c>want</c> of three
    /// parts, <c>parents</c> exactly (in the order given), <c>groups</c> and <c>lints</c> each as a set, the order inside
    /// a group exactly. The TypeScript SDK's <c>reportThreadsOf</c> and the Python package's <c>report_threads_of</c>
    /// replay the same cases. Then this SDK's own tests of the corners the fixture holds no case for. When the dist moves
    /// the fixture, re-vendor it and update the hash and the count below.
    /// </summary>
    public class OWReportTests
    {
        /// <summary>The fixture's sha256 as dist.120's MANIFEST.json records it.</summary>
        private const string DistSha256 = "5826bc58609ebc35862b4b9a364cad92822c2a6a50279dc5418dc674f5afd185";

        /// <summary>The fixture's thread cases, as dist.120 holds them.</summary>
        private const int CaseCount = 20;

        private const string Mark = "019a7120-0000-7000-8000-000000000000";

        private static JObject _fixture;

        private static string FixturePath
        {
            get
            {
                var root = TestPackage.Root();
                return root == null ? null : Path.Combine(root, "Tests", "Editor", "Fixtures", "report", "report.fixture.json");
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
                yield return new TestCaseData(string.Empty).SetName("Report_FixtureNotFound");
                yield break;
            }

            foreach (var c in fixture["thread_cases"].Cast<JObject>())
            {
                var name = (string)c["name"];
                yield return new TestCaseData(name).SetName("Report_" + name.Replace('-', '_'));
            }
        }

        [Test]
        public void TheFixture_IsTheDistsBytes()
        {
            var path = FixturePath;
            Assert.IsTrue(path != null && File.Exists(path), "the vendored report fixture was not found: " + path);
            using var sha = SHA256.Create();
            var hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
            Assert.AreEqual(DistSha256, hash,
                "the vendored fixture is not dist.120's: re-vendor it from schema-dist (a checkout's line endings change the bytes)");
        }

        [Test]
        public void TheFixture_HoldsDist120sCases_EachNamedOnce_AndItsPairsAreTheReaders()
        {
            Assert.IsNotNull(Fixture, "the vendored report fixture was not found");
            Assert.AreEqual("report", (string)Fixture["profile"]);
            var names = Fixture["thread_cases"].Select(c => (string)c["name"]).ToList();
            Assert.AreEqual(CaseCount, names.Count, "dist.120's fixture holds 20 thread cases");
            Assert.AreEqual(names.Count, names.Distinct().Count(), "a case is found by its name, so each name is once");

            Assert.AreEqual(OWReport.Pair.type, (string)Fixture["pair"]["type"]);
            Assert.AreEqual(OWReport.Pair.supertype, (string)Fixture["pair"]["supertype"]);
            Assert.AreEqual(OWReport.BoardPair.type, (string)Fixture["board_pair"]["type"]);
            Assert.AreEqual(OWReport.BoardPair.supertype, (string)Fixture["board_pair"]["supertype"]);
            CollectionAssert.Contains(OWRoom.Pairs.ToList(), OWReport.Pair, "a report is a room pair");
            CollectionAssert.Contains(OWRoom.Pairs.ToList(), OWReport.BoardPair, "a board entry is a room pair");
            Assert.AreEqual(OWRoom.MarkField, (string)Fixture["mark"]["field"]);
            Assert.AreEqual(Mark, (string)Fixture["mark"]["value"]);
        }

        [TestCaseSource(nameof(Cases))]
        public void EveryCase_IsAnsweredAsTheProfileSays(string name)
        {
            Assert.IsNotNull(Fixture, "the vendored report fixture was not found");
            var c = Fixture["thread_cases"].Cast<JObject>().Single(x => (string)x["name"] == name);
            var got = OWReport.ThreadsOf((JArray)c["narratives"]).ToJson();
            var want = (JObject)c["want"];
            CollectionAssert.AreEqual(new[] { "parents", "groups", "lints" }, got.Properties().Select(p => p.Name).ToList(),
                name + ": the three parts, in order");
            Assert.AreEqual(want["parents"].ToString(Formatting.None), got["parents"].ToString(Formatting.None), name + ": parents");
            CollectionAssert.AreEquivalent(Set(want["groups"]), Set(got["groups"]), name + ": groups, as a set");
            CollectionAssert.AreEquivalent(Set(want["lints"]), Set(got["lints"]), name + ": lints, as a set");
        }

        /// <summary>The same narratives give the same answer, byte for byte, and are not changed.</summary>
        [Test]
        public void TheReading_IsPure()
        {
            Assert.IsNotNull(Fixture, "the vendored report fixture was not found");
            foreach (var c in Fixture["thread_cases"].Cast<JObject>())
            {
                var narratives = (JArray)c["narratives"];
                var before = narratives.ToString(Formatting.None);
                var once = OWReport.ThreadsOf(narratives).ToJson().ToString(Formatting.None);
                var twice = OWReport.ThreadsOf(narratives).ToJson().ToString(Formatting.None);
                Assert.AreEqual(once, twice, (string)c["name"]);
                Assert.AreEqual(before, narratives.ToString(Formatting.None), (string)c["name"] + ": the narratives moved");
            }
        }

        /// <summary>The typed answer says what its JSON says.</summary>
        [Test]
        public void TheTypedAnswer_MatchesItsJson()
        {
            var got = OWReport.ThreadsOf(new JArray(
                Board("b1"), Report("r1", "b1", "2026-10-26T09:00:00Z"), Report("r2", "r1", "2026-10-26T09:10:00Z"),
                Report("r3", "r2", "2026-10-26T09:20:00Z"), Report("r4", "r4", null)));
            CollectionAssert.AreEqual(new[] { "r1", "r2", "r3", "r4" }, got.Reports.ToList());
            Assert.AreEqual(OWReportParent.Board, got.Parents["r1"]);
            Assert.AreEqual(OWReportParent.Report, got.Parents["r2"]);
            Assert.AreEqual(OWReportParent.Self, got.Parents["r4"]);
            Assert.AreEqual(3, got.Groups.Count);
            Assert.AreEqual("b1", got.Groups[0].Parent);
            Assert.AreEqual(OWReportParent.Board, got.Groups[0].Kind);
            CollectionAssert.AreEqual(new[] { "r1" }, got.Groups[0].Reports.ToList());
            CollectionAssert.AreEqual(new[] { "r3" }, got.Groups.Single(g => g.Parent == "r2").Reports.ToList());
            CollectionAssert.AreEqual(new[] { "self:r4" }, got.Lints.Where(l => l.Kind == OWReportLintKind.Self).Select(l => "self:" + l.Report).ToList());
            CollectionAssert.AreEqual(new[] { "r3" }, got.Lints.Where(l => l.Kind == OWReportLintKind.Chain).Select(l => l.Report).ToList());
            Assert.IsTrue(OWReport.IsReport(Report("x", null, null)));
            Assert.IsTrue(OWReport.IsBoardEntry(Board("y")));
            Assert.IsFalse(OWReport.IsReport(Board("y")));
        }

        // -- The corners the fixture holds no case for (this SDK's own) --------------

        /// <summary>
        /// A <c>created_at</c> with no offset is read as UTC, by the message profile's instant reader: 09:30 with no
        /// offset comes after 09:15Z (written as 11:15+02:00), which comes after 09:00Z. Compared as text the one with no
        /// offset would come second, and read in a zone east of UTC first. The fixture's times all carry an offset.
        /// </summary>
        [Test]
        public void ACreatedAtWithNoOffset_IsReadAsUtc()
        {
            var got = OWReport.ThreadsOf(new JArray(
                Report("r0", null, "2026-10-26T08:00:00Z"),
                Report("a", "r0", "2026-10-26T09:30:00"),
                Report("b", "r0", "2026-10-26T09:00:00Z"),
                Report("c", "r0", "2026-10-26T11:15:00+02:00")));
            CollectionAssert.AreEqual(new[] { "b", "c", "a" }, got.Groups.Single().Reports.ToList());
        }

        /// <summary>
        /// Of two elements with one id the first is read, and the later copy is passed over whole: it is neither a report
        /// nor a parent. The fixture holds no id twice.
        /// </summary>
        [Test]
        public void OfTwoWithOneId_TheFirstIsRead_AndTheLaterCopyIsPassedOverWhole()
        {
            var got = OWReport.ThreadsOf(new JArray(
                Board("p"), Report("p", null, null), Report("r1", "p", null), Report("r1", "x", null)));
            Assert.AreEqual(@"{""parents"":{""r1"":""board""},""groups"":[{""parent"":""p"",""kind"":""board"",""reports"":[""r1""]}],""lints"":[]}",
                got.ToJson().ToString(Formatting.None));

            var again = OWReport.ThreadsOf(new JArray(Report("r1", null, null), Report("r1", "r1", null)));
            Assert.AreEqual(@"{""parents"":{""r1"":""none""},""groups"":[],""lints"":[]}", again.ToJson().ToString(Formatting.None),
                "a later copy that names the first is not read: no self lint, no group");
        }

        /// <summary>
        /// A <c>parent_narrative</c> that is no string reads none and is never matched by its text: a number (beside an
        /// element whose id is that number's text), a boolean, a list, and an expanded object holding the parent's id.
        /// The fixture's non-blank parents are all strings.
        /// </summary>
        [Test]
        public void AParentThatIsNoString_ReadsNone_NeverMatchedAsText()
        {
            var values = new JToken[]
            {
                new JValue(7), new JValue(true), new JArray("r0"), new JObject { ["id"] = "r0", ["type"] = "narrative" },
            };
            var list = new JArray(Report("r0", null, null), Report("7", null, null));
            for (var i = 0; i < values.Length; i++)
            {
                var r = Report("r" + (i + 1), null, null);
                r["parent_narrative"] = values[i];
                list.Add(r);
            }

            var got = OWReport.ThreadsOf(list);
            CollectionAssert.AreEqual(new[] { "r0", "7", "r1", "r2", "r3", "r4" }, got.Reports.ToList());
            Assert.IsTrue(got.Parents.Values.All(v => v == OWReportParent.None), got.ToJson().ToString(Formatting.None));
            CollectionAssert.IsEmpty(got.Groups);
            CollectionAssert.IsEmpty(got.Lints);
        }

        /// <summary>
        /// A parent with something left once trimmed is read as written, never trimmed before the lookup: <c>" r1 "</c>
        /// and <c>"r1\n"</c> name no <c>r1</c> and read missing. Only a parent that is White_Space alone (a tab, a
        /// no-break space, an ideographic space) reads none. The fixture's padded parent is spaces alone.
        /// </summary>
        [Test]
        public void AParentWithPadding_IsReadAsWritten_AndReadsMissing()
        {
            var got = OWReport.ThreadsOf(new JArray(
                Report("r1", null, null), Report("r2", " r1 ", null), Report("r3", "r1\n", null),
                Report("r4", "\t 　", null)));
            Assert.AreEqual(@"{""parents"":{""r1"":""none"",""r2"":""missing"",""r3"":""missing"",""r4"":""none""},""groups"":[],""lints"":[]}",
                got.ToJson().ToString(Formatting.None));
        }

        /// <summary>
        /// An entry that is no object, or has no id that is a non-empty string, is passed over: it is no report, and a
        /// report that names it reads missing. Null gives the empty answer.
        /// </summary>
        [Test]
        public void AnEntryThatIsNoElement_IsPassedOver()
        {
            var noId = Report("x", null, null);
            noId.Remove("id");
            var number = Report("x", null, null);
            number["id"] = 42;
            var got = OWReport.ThreadsOf(new JArray(
                JValue.CreateNull(), new JValue("r1"), new JArray("r1"), noId, number, Report("", null, null),
                Report("r1", "", null), Report("r2", "42", null)));
            Assert.AreEqual(@"{""parents"":{""r1"":""none"",""r2"":""missing""},""groups"":[],""lints"":[]}",
                got.ToJson().ToString(Formatting.None));
            Assert.AreEqual(@"{""parents"":{},""groups"":[],""lints"":[]}", OWReport.ThreadsOf(null).ToJson().ToString(Formatting.None));
        }

        /// <summary>
        /// The pair is the room profile's, trimmed and folded, the whole word, and the mark is read by presence: a
        /// padded or upper-case pair is a report, <c>Reports</c> and a blank mark are not, a mark of <c>0</c> is one.
        /// </summary>
        [Test]
        public void ThePair_IsTrimmedAndFolded_AndTheMarkReadByPresence()
        {
            var r1 = Report("r1", "r0", "2026-10-26T09:00:00Z");
            r1["type"] = " Narrative ";
            r1["supertype"] = "REPORT";
            var r2 = Report("r2", "r0", null);
            r2["supertype"] = "Reports";
            var r3 = Report("r3", "r0", null);
            r3[OWRoom.MarkField] = "  ";
            var r4 = Report("r4", "r0", "2026-10-26T09:10:00Z");
            r4[OWRoom.MarkField] = 0;
            var got = OWReport.ThreadsOf(new JArray(Report("r0", null, null), r1, r2, r3, r4));
            Assert.AreEqual(@"{""parents"":{""r0"":""none"",""r1"":""report"",""r4"":""report""},""groups"":[{""parent"":""r0"",""kind"":""report"",""reports"":[""r1"",""r4""]}],""lints"":[]}",
                got.ToJson().ToString(Formatting.None));
        }

        private static JObject Report(string id, string parent, string createdAt)
        {
            var r = new JObject
            {
                ["id"] = id,
                ["type"] = "narrative",
                ["supertype"] = "Report",
                [OWRoom.MarkField] = Mark,
                ["parent_narrative"] = parent == null ? JValue.CreateNull() : new JValue(parent),
                ["relations"] = new JArray(),
            };
            if (createdAt != null) r["created_at"] = createdAt;
            return r;
        }

        private static JObject Board(string id) => new JObject
        {
            ["id"] = id,
            ["type"] = "narrative",
            ["supertype"] = "Board",
            [OWRoom.MarkField] = Mark,
            ["parent_narrative"] = JValue.CreateNull(),
        };

        private static List<string> Set(JToken list) => list.Select(x => x.ToString(Formatting.None)).ToList();
    }
}
