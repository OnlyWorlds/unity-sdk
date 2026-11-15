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
    /// The report profile's lines fixture (schema-dist <c>profiles/report-lines.fixture.json</c>, rule 10, vendored byte
    /// for byte in <c>Tests/Editor/Fixtures/report/</c> from dist.129), replayed row by row through
    /// <see cref="OWReport.ReadLine"/> and <see cref="OWReport.ReadLines"/>, read in the world its <c>world</c> names:
    /// each case one line as it stands, its address (<c>ow://&lt;type&gt;/&lt;id&gt;</c> or none) and its shown words;
    /// each text the lines it splits into, in order, each with its line, address and words. Every one is compared
    /// exactly, character for character; <c>kind</c>, <c>status</c> and <c>about</c> are notes, never compared. The
    /// TypeScript SDK's <c>readReportLine</c> and <c>readReportLines</c> and the Python package's twins replay the same
    /// rows. When the dist moves the fixture, re-vendor it and update the hash and the counts below.
    /// </summary>
    public class OWReportLinesTests
    {
        /// <summary>The fixture's sha256 as dist.129's MANIFEST.json records it.</summary>
        private const string DistSha256 = "2face7cd693235d806e34d3884164b6934a78a299fb77f5ac283b89374e1b47a";

        /// <summary>The fixture's line cases and texts, as dist.129 holds them.</summary>
        private const int CaseCount = 101;
        private const int TextCount = 16;

        private const string Id = "01a1d4ef-0000-7000-8000-000000000084";

        private static JObject _fixture;

        private static string FixturePath
        {
            get
            {
                var root = TestPackage.Root();
                return root == null ? null : Path.Combine(root, "Tests", "Editor", "Fixtures", "report", "report-lines.fixture.json");
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

        private static string World => (string)Fixture["world"];

        /// <summary>One test per line case, by its place in the fixture (a kind is a note, and not every kind is a name).</summary>
        public static IEnumerable<TestCaseData> LineCases()
        {
            var fixture = Fixture;
            if (fixture == null)
            {
                yield return new TestCaseData(-1).SetName("ReportLine_FixtureNotFound");
                yield break;
            }

            var cases = (JArray)fixture["cases"];
            for (var i = 0; i < cases.Count; i++) yield return new TestCaseData(i).SetName($"ReportLine_{i:000}");
        }

        /// <summary>One test per text, by its place in the fixture.</summary>
        public static IEnumerable<TestCaseData> TextCases()
        {
            var fixture = Fixture;
            if (fixture == null)
            {
                yield return new TestCaseData(-1).SetName("ReportLines_FixtureNotFound");
                yield break;
            }

            var texts = (JArray)fixture["texts"];
            for (var i = 0; i < texts.Count; i++) yield return new TestCaseData(i).SetName($"ReportLines_{i:00}");
        }

        [Test]
        public void TheFixture_IsTheDistsBytes()
        {
            var path = FixturePath;
            Assert.IsTrue(path != null && File.Exists(path), "the vendored lines fixture was not found: " + path);
            using var sha = SHA256.Create();
            var hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
            Assert.AreEqual(DistSha256, hash,
                "the vendored fixture is not dist.129's: re-vendor it from schema-dist (a checkout's line endings change the bytes)");
        }

        [Test]
        public void TheFixture_HoldsDist129sRows_OfEachKind()
        {
            Assert.IsNotNull(Fixture, "the vendored lines fixture was not found");
            Assert.AreEqual("report", (string)Fixture["profile"]);
            Assert.AreEqual("lines", (string)Fixture["table"]);
            var cases = Fixture["cases"].Cast<JObject>().ToList();
            Assert.AreEqual(CaseCount, cases.Count, "dist.129's fixture holds 101 line cases");
            Assert.AreEqual(TextCount, Fixture["texts"].Count(), "dist.129's fixture holds 16 texts");
            Assert.IsTrue(cases.Any(c => c["address"].Type == JTokenType.Null), "a line with no address");
            Assert.IsTrue(cases.Any(c => c["address"].Type != JTokenType.Null && (string)c["words"] == (string)c["line"]),
                "a line shown whole, still linked");
            Assert.IsTrue(cases.Any(c => c["address"].Type != JTokenType.Null && (string)c["words"] != (string)c["line"]),
                "a line shown with its address taken out");
        }

        [TestCaseSource(nameof(LineCases))]
        public void EveryLine_IsReadAsTheProfileSays(int index)
        {
            Assert.IsNotNull(Fixture, "the vendored lines fixture was not found");
            var c = (JObject)Fixture["cases"][index];
            var line = (string)c["line"];
            var want = (string)c["address"];
            var about = $"case {index} ({(string)c["kind"]})";
            var got = OWReport.ReadLine(line, World);
            Assert.AreEqual(line, got.Line, about + ": the line as read");
            Assert.AreEqual(want, got.Address, about + ": the address");
            Assert.AreEqual((string)c["words"], got.Words, about + ": the words");
            if (got.Address == null)
            {
                Assert.IsNull(got.Reading, about + ": no reading without an address");
                Assert.IsNull(got.Start, about + ": no start without an address");
                Assert.IsNull(got.End, about + ": no end without an address");
            }
            else
            {
                // The address as written stands at Start..End and reads as the element named.
                Assert.IsNotNull(got.Reading, about + ": a reading");
                var slice = line.Substring(got.Start.Value, got.End.Value - got.Start.Value);
                var reading = OWAddress.ParseLink(slice);
                Assert.IsNotNull(reading, about + ": the slice " + slice + " reads as an address");
                Assert.AreEqual(reading.Type, got.Reading.Type, about);
                Assert.AreEqual(reading.Id, got.Reading.Id, about);
                Assert.AreEqual(reading.World, got.Reading.World, about);
                Assert.AreEqual(reading.Canonical, got.Reading.Canonical, about);
                Assert.AreEqual(want, "ow://" + got.Reading.Type + "/" + got.Reading.Id, about);
            }
        }

        [TestCaseSource(nameof(TextCases))]
        public void EveryText_SplitsIntoTheProfilesLines(int index)
        {
            Assert.IsNotNull(Fixture, "the vendored lines fixture was not found");
            var t = (JObject)Fixture["texts"][index];
            var want = t["lines"].Cast<JObject>().Select(l => Row((string)l["line"], (string)l["address"], (string)l["words"])).ToList();
            var got = OWReport.ReadLines((string)t["text"], World).Select(l => Row(l.Line, l.Address, l.Words)).ToList();
            CollectionAssert.AreEqual(want, got, $"text {index} ({(string)t["kind"]})");
        }

        /// <summary>A row as one string, each part escaped, so a mismatch shows where it is.</summary>
        private static string Row(string line, string address, string words) =>
            $"[{Escape(line)}] [{(address == null ? "null" : Escape(address))}] [{Escape(words)}]";

        private static string Escape(string s) =>
            string.Concat(s.Select(ch => ch < 0x20 || ch > 0x7E ? $"\\u{(int)ch:x4}" : ch.ToString()));

        // -- This SDK's own, beside the fixture: the TypeScript SDK's tests of the world and the refusals --

        /// <summary>Without a world, only an address with no world names an element here.</summary>
        [Test]
        public void WithoutAWorld_OnlyAnAddressWithNoWorldIsTheLines()
        {
            Assert.IsNotNull(Fixture, "the vendored lines fixture was not found");
            Assert.IsNull(OWReport.ReadLine($"Built: ow://{World}/object/{Id}").Address);
            Assert.AreEqual($"ow://object/{Id}", OWReport.ReadLine($"Built: ow://object/{Id}").Address);
            Assert.AreEqual("Built", OWReport.ReadLine($"Built: ow://object/{Id}", null).Words);
            Assert.AreEqual($"ow://object/{Id}", OWReport.ReadLine($"Built: ow://{World}/object/{Id}", World).Address);
        }

        /// <summary>The world is compared as given: an upper-case world id names no world here (the reading's is lowercase).</summary>
        [Test]
        public void TheWorld_IsComparedAsGiven()
        {
            Assert.IsNotNull(Fixture, "the vendored lines fixture was not found");
            Assert.IsNull(OWReport.ReadLine($"Built: ow://{World}/object/{Id}", World.ToUpperInvariant()).Address);
        }

        /// <summary>A text splits at CR and LF only, and every line of it is read in the world given.</summary>
        [Test]
        public void ReadLines_ReadsEachLineInTheWorldGiven()
        {
            var got = OWReport.ReadLines($"Built: ow://object/{Id}\r\n\r\n  nothing changed\u2028still\n\u00a0\n");
            Assert.AreEqual(2, got.Count);
            Assert.AreEqual("Built", got[0].Words);
            Assert.AreEqual("nothing changed\u2028still", got[1].Line);
            Assert.IsNull(got[1].Address);
            Assert.AreEqual(0, OWReport.ReadLines(string.Empty).Count);
        }

        [Test]
        public void ANullLineOrText_IsRefused()
        {
            Assert.Throws<ArgumentNullException>(() => OWReport.ReadLine(null));
            Assert.Throws<ArgumentNullException>(() => OWReport.ReadLines(null));
        }
    }
}
