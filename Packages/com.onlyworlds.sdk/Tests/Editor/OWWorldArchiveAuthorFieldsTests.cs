using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// A world file leaves out the five author fields (<c>created_by</c>, <c>changed_by</c>, <c>changed_via</c>,
    /// <c>changed_client</c>, <c>doors</c>) of every file under <c>elements/</c> and <c>spatial/</c> that is one JSON
    /// object, unless <see cref="OWWorldArchivePackOptions.IncludeAuthorFields"/> asks for a byte copy.
    /// </summary>
    /// <remarks>
    /// Held to the packages' own fixture (<c>Fixtures/author-fields/author-fields.fixture.json</c>, the same bytes as the
    /// TypeScript SDK's <c>test/fixtures/author-fields.fixture.json</c> and the Python package's copy, its sha256 pinned
    /// here), every case replayed whole: each entry compared byte for byte, and the count. Then the edges the fixture
    /// does not hold: the TypeScript SDK's verdict on each (4.203.1, measured; the integer edges again on 4.205.0), and
    /// the place this package keeps a file's bytes where the TypeScript SDK rewrites it (a file nested past Newtonsoft's
    /// depth). Ported from @onlyworlds/sdk 4.203.1 (6794d27), test/world-file.test.mjs, "author fields"; the fixture
    /// at 4.203.2 (55aad63), whose case 8 holds an integer no double holds.
    /// </remarks>
    public class OWWorldArchiveAuthorFieldsTests
    {
        /// <summary>The fixture's sha256, as the TypeScript and Python suites hold it.</summary>
        private const string Sha256 = "33835ede8b407868da8fef2857699c53e763879ad4bef7909376c75b31c48645";

        private const int CaseCount = 8;

        private static readonly string[] Fields = { "created_by", "changed_by", "changed_via", "changed_client", "doors" };

        private const string WorldJson = "{\n  \"id\": \"0192a000-aaaa-7bbb-8ccc-000000000001\",\n  \"name\": \"W\"\n}\n";

        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        private static JObject _fixture;

        private string _temp;

        [SetUp]
        public void SetUp()
        {
            _temp = Path.Combine(Path.GetTempPath(), "ow-author-fields-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temp);
        }

        [TearDown]
        public void TearDown()
        {
            if (_temp != null && Directory.Exists(_temp)) Directory.Delete(_temp, recursive: true);
        }

        private static string FixturePath
        {
            get
            {
                var root = TestPackage.Root();
                return root == null ? null : Path.Combine(root, "Tests", "Editor", "Fixtures", "author-fields", "author-fields.fixture.json");
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
                yield return new TestCaseData(string.Empty).SetName("AuthorFields_FixtureNotFound");
                yield break;
            }

            var i = 0;
            foreach (var c in fixture["cases"].Cast<JObject>())
                yield return new TestCaseData((string)c["name"]).SetName($"AuthorFields_{++i:D2}");
        }

        [Test]
        public void TheFixture_IsTheSharedBytes()
        {
            var path = FixturePath;
            Assert.IsTrue(path != null && File.Exists(path), "the vendored author-fields fixture was not found: " + path);
            using var sha = SHA256.Create();
            var hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
            Assert.AreEqual(Sha256, hash,
                "the vendored fixture is not the packages' bytes: re-vendor it (a checkout's line endings change the bytes)");
            CollectionAssert.AreEqual(Fields, Fixture["fields"].Select(f => (string)f).ToArray());
            var names = Fixture["cases"].Select(c => (string)c["name"]).ToList();
            Assert.AreEqual(CaseCount, names.Count, $"the fixture holds {CaseCount} cases");
            Assert.AreEqual(names.Count, names.Distinct().Count(), "a case is found by its name, so each name is once");
        }

        [TestCaseSource(nameof(Cases))]
        public void EveryCase_LeftOutByDefault_AndCarriedWhenAsked(string name)
        {
            Assert.IsNotNull(Fixture, "the vendored author-fields fixture was not found");
            var c = Fixture["cases"].Cast<JObject>().Single(x => (string)x["name"] == name);
            var files = ((JObject)c["files"]).Properties().ToDictionary(p => p.Name, p => Utf8.GetBytes((string)p.Value));
            var root = WriteWorld("w", files);

            var left = new List<string>();
            var byDefault = Pack(root, "default.owworld", new OWWorldArchivePackOptions { AuthorFieldsLeftOut = left });
            var want = ((JObject)c["default"]).Properties().ToDictionary(p => p.Name, p => Utf8.GetBytes((string)p.Value));
            AssertEntries(want, EntriesOf(byDefault, "w"), name + ", the default");
            Assert.AreEqual((int)c["stripped"], left.Count, name + ": the files that lost a field, counted");
            CollectionAssert.AreEqual(left.OrderBy(p => p, StringComparer.Ordinal), left, name + ": the paths in order");

            // No options at all is the default, byte for byte.
            var plain = Path.Combine(_temp, "plain.owworld");
            OWWorldArchive.Pack(root, plain);
            CollectionAssert.AreEqual(File.ReadAllBytes(byDefault), File.ReadAllBytes(plain), name + ": no options is the default");

            var asked = new List<string>();
            var copy = Pack(root, "asked.owworld",
                new OWWorldArchivePackOptions { IncludeAuthorFields = true, AuthorFieldsLeftOut = asked });
            AssertEntries(files, EntriesOf(copy, "w"), name + ", asked: byte for byte");
            Assert.AreEqual(0, asked.Count, name + ": asked, nothing left out");
        }

        /// <summary>
        /// The edges the fixture does not hold, one file each beside <c>world.json</c>: its text, and what the pack writes
        /// for it (null: its bytes, the author field included). Every verdict is the TypeScript SDK 4.203.1's (measured on
        /// its build) except where the label says this package keeps the bytes.
        /// </summary>
        public static IEnumerable<TestCaseData> Edges()
        {
            string Nested(int depth) => new string('[', depth - 1) + new string(']', depth - 1);
            TestCaseData Edge(string label, string text, string want) => new TestCaseData(text, want).SetName("AuthorFields_Edge_" + label);

            // Numbers: a file is rewritten only when every number in it is written back as JSON.stringify writes it.
            yield return Edge("SafeIntegersAndMinusZero", "{\"id\":\"d\",\"n\":9007199254740991,\"m\":-0,\"created_by\":\"u\"}",
                "{\n  \"id\": \"d\",\n  \"n\": 9007199254740991,\n  \"m\": 0\n}\n");
            yield return Edge("AnIntegerPast2To53_JavaScriptWritesTheSame", "{\"id\":\"c\",\"n\":18014398509481984,\"created_by\":\"u\"}",
                "{\n  \"id\": \"c\",\n  \"n\": 18014398509481984\n}\n");
            // The file keeps its bytes, as in the TypeScript SDK since 4.203.2 (measured on 4.205.0): a rewrite would
            // write a value the file does not hold.
            yield return Edge("AnIntegerJavaScriptWouldRound_KeepsItsBytes", "{\"id\":\"a\",\"n\":12345678901234567891,\"created_by\":\"u\"}", null);
            yield return Edge("AnIntegerOnePast2To53_KeepsItsBytes", "{\"id\":\"b\",\"n\":9007199254740993,\"created_by\":\"u\"}", null);
            yield return Edge("AnIntegerNoDoubleHolds_KeepsItsBytes", "{\"id\":\"e\",\"n\":" + new string('9', 400) + ",\"created_by\":\"u\"}", null);
            yield return Edge("Doubles_Exact", "{\"id\":\"t\",\"x\":263.2953629956518,\"y\":0.1,\"z\":1.5e300,\"w\":5e-324,\"created_by\":\"u\"}",
                "{\n  \"id\": \"t\",\n  \"x\": 263.2953629956518,\n  \"y\": 0.1,\n  \"z\": 1.5e+300,\n  \"w\": 5e-324\n}\n");

            // JSON.parse's grammar: what Newtonsoft also reads is no JSON here, and keeps its bytes.
            yield return Edge("AComment_KeepsItsBytes", "{\"id\":\"f\",/* note */\"created_by\":\"u\"}", null);
            yield return Edge("SingleQuotes_KeepItsBytes", "{'id':'g','created_by':'u'}", null);
            yield return Edge("ABareName_KeepsItsBytes", "{id:\"h\",\"created_by\":\"u\"}", null);
            yield return Edge("ATrailingComma_KeepsItsBytes", "{\"id\":\"i\",\"created_by\":\"u\",}", null);
            yield return Edge("NaN_KeepsItsBytes", "{\"id\":\"j\",\"n\":NaN,\"created_by\":\"u\"}", null);
            yield return Edge("ARawTabInAString_KeepsItsBytes", "{\"id\":\"k\",\"name\":\"a\tb\",\"created_by\":\"u\"}", null);
            yield return Edge("ALeadingZero_KeepsItsBytes", "{\"id\":\"l\",\"n\":01,\"created_by\":\"u\"}", null);
            yield return Edge("ABarePoint_KeepsItsBytes", "{\"id\":\"m\",\"n\":.5,\"created_by\":\"u\"}", null);
            yield return Edge("TwoValues_KeepItsBytes", "{\"id\":\"c-9\",\"created_by\":\"u\"} {\"hidden\": true}", null);

            // Strings and keys.
            // The TypeScript SDK rewrites a lone surrogate's escape as it was ("\ud800x"); Newtonsoft reads it as U+FFFD,
            // so here the file keeps its bytes. An escaped pair is one character, written raw as JSON.stringify writes it.
            yield return Edge("ALoneHighSurrogate_KeepsItsBytes", "{\"id\":\"s\",\"name\":\"\\ud800x\",\"created_by\":\"u\"}", null);
            yield return Edge("ALoneLowSurrogate_KeepsItsBytes", "{\"id\":\"s\",\"name\":\"x\\udc00\",\"created_by\":\"u\"}", null);
            yield return Edge("AHighSurrogateAtTheEnd_KeepsItsBytes", "{\"id\":\"s\",\"name\":\"x\\ud800\",\"created_by\":\"u\"}", null);
            yield return Edge("AnEscapedPair_WrittenRaw", "{\"id\":\"s\",\"name\":\"\\\"\\ud83d\\ude00\\\\\",\"created_by\":\"u\"}",
                "{\n  \"id\": \"s\",\n  \"name\": \"\\\"\U0001F600\\\\\"\n}\n");
            yield return Edge("RawAboveTheControls", "{\"id\":\"v\",\"name\":\"a\\u007f\\u0085\\u2029b\",\"created_by\":\"u\"}",
                "{\n  \"id\": \"v\",\n  \"name\": \"a\u007f\u0085\u2029b\"\n}\n");
            yield return Edge("ADuplicateKey_FirstPlaceLastValue", "{\"id\":\"r\",\"name\":\"A\",\"x\":1,\"created_by\":\"u\",\"name\":\"B\"}",
                "{\n  \"id\": \"r\",\n  \"name\": \"B\",\n  \"x\": 1\n}\n");
            // JSON.stringify puts integer-like keys first ("0", "12", "id", "b"); here every key keeps its place.
            yield return Edge("IntegerLikeKeys_KeepTheirOrder", "{\"id\":\"q\",\"b\":1,\"12\":2,\"0\":3,\"created_by\":\"u\"}",
                "{\n  \"id\": \"q\",\n  \"b\": 1,\n  \"12\": 2,\n  \"0\": 3\n}\n");

            // Depth: every reader reads to 200 deep (an object at the top is 1; the folder format's OW-25), Newtonsoft's
            // own line of 64 raised to it, and a file past it keeps its bytes, as the TypeScript SDK keeps them.
            yield return Edge("Nested64_Rewritten", "{\"id\":\"u\",\"x_d\":" + Nested(64) + ",\"created_by\":\"u\"}", "rewritten");
            yield return Edge("Nested65_Rewritten", "{\"id\":\"n\",\"x_d\":" + Nested(65) + ",\"created_by\":\"u\"}", "rewritten");
            yield return Edge("Nested200_Rewritten", "{\"id\":\"o\",\"x_d\":" + Nested(200) + ",\"created_by\":\"u\"}", "rewritten");
            yield return Edge("Nested201_KeepsItsBytes", "{\"id\":\"p\",\"x_d\":" + Nested(201) + ",\"created_by\":\"u\"}", null);
        }

        [TestCaseSource(nameof(Edges))]
        public void AnEdge_IsRewrittenOrKept(string text, string want)
        {
            const string rel = "elements/object/edge.json";
            var bytes = Utf8.GetBytes(text);
            var root = WriteWorld("w", new Dictionary<string, byte[]> { ["world.json"] = Utf8.GetBytes(WorldJson), [rel] = bytes });
            var left = new List<string>();
            var got = EntriesOf(Pack(root, "edge.owworld", new OWWorldArchivePackOptions { AuthorFieldsLeftOut = left }), "w")[rel];

            if (want == null)
            {
                CollectionAssert.AreEqual(bytes, got, "kept byte for byte, its author field included");
                Assert.AreEqual(0, left.Count, "nothing counted");
                return;
            }

            CollectionAssert.AreEqual(new[] { rel }, left, "counted");
            if (want == "rewritten")
            {
                var source = OWJson.ParseObject(text);
                source.Remove("created_by");
                Assert.AreEqual(OWFolderWriter.Serialize(source), Utf8.GetString(got), "the folder's own form");
                return;
            }

            Assert.AreEqual(want, Utf8.GetString(got));
        }

        [Test]
        public void AFileThatIsNotUtf8_KeepsItsBytes()
        {
            const string rel = "elements/character/latin.json";
            var latin = new byte[] { 0x7b, 0x22, 0x69, 0x64, 0x22, 0x3a, 0x22, 0xe9, 0x22, 0x2c, 0x22, 0x63, 0x72, 0x65, 0x61, 0x74,
                0x65, 0x64, 0x5f, 0x62, 0x79, 0x22, 0x3a, 0x22, 0x75, 0x22, 0x7d }; // {"id":"\xe9","created_by":"u"}
            var root = WriteWorld("w", new Dictionary<string, byte[]> { ["world.json"] = Utf8.GetBytes(WorldJson), [rel] = latin });
            var left = new List<string>();
            var got = EntriesOf(Pack(root, "latin.owworld", new OWWorldArchivePackOptions { AuthorFieldsLeftOut = left }), "w");
            CollectionAssert.AreEqual(latin, got[rel]);
            Assert.AreEqual(0, left.Count);
        }

        [Test]
        public void AShare_CountsOnlyTheFilesItKeeps()
        {
            var root = WriteWorld("w", new Dictionary<string, byte[]>
            {
                ["world.json"] = Utf8.GetBytes(WorldJson),
                ["elements/character/ada.json"] = Utf8.GetBytes("{\"id\":\"a\",\"hidden\":true,\"created_by\":\"u\"}"),
                ["elements/character/bo.json"] = Utf8.GetBytes("{\"id\":\"b\",\"changed_by\":\"u\"}"),
            });
            var hidden = new List<string>();
            var left = new List<string>();
            var got = EntriesOf(Pack(root, "share.owworld",
                new OWWorldArchivePackOptions { IncludeHidden = false, HiddenLeftOut = hidden, AuthorFieldsLeftOut = left }), "w");
            CollectionAssert.AreEqual(new[] { "elements/character/ada.json" }, hidden);
            CollectionAssert.AreEqual(new[] { "elements/character/bo.json" }, left);
            CollectionAssert.AreEquivalent(new[] { "world.json", "elements/character/bo.json" }, got.Keys);
            Assert.AreEqual("{\n  \"id\": \"b\"\n}\n", Utf8.GetString(got["elements/character/bo.json"]));
        }

        [Test]
        public void TheBoolOverloads_LeaveThemOutToo()
        {
            const string rel = "elements/character/bo.json";
            var root = WriteWorld("w", new Dictionary<string, byte[]>
            {
                ["world.json"] = Utf8.GetBytes(WorldJson),
                [rel] = Utf8.GetBytes("{\"id\":\"b\",\"changed_by\":\"u\"}"),
            });
            var archive = Path.Combine(_temp, "overload.owworld");
            OWWorldArchive.Pack(root, archive, false, false, includeHidden: true);
            Assert.AreEqual("{\n  \"id\": \"b\"\n}\n", Utf8.GetString(EntriesOf(archive, "w")[rel]));
        }

        // -- Helpers ----------------------------------------------------------

        private string WriteWorld(string name, IDictionary<string, byte[]> files)
        {
            var root = Path.Combine(_temp, name);
            foreach (var pair in files)
            {
                var path = Path.Combine(root, pair.Key.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, pair.Value);
            }

            return root;
        }

        private string Pack(string root, string file, OWWorldArchivePackOptions options)
        {
            var archive = Path.Combine(_temp, file);
            OWWorldArchive.Pack(root, archive, options);
            return archive;
        }

        /// <summary>The archive's entries under its top folder, by path inside the world; the marker left aside.</summary>
        private static Dictionary<string, byte[]> EntriesOf(string archive, string top)
        {
            var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            using (var zip = ZipFile.OpenRead(archive))
            {
                foreach (var entry in zip.Entries)
                {
                    if (entry.FullName == "mimetype") continue;
                    Assert.IsTrue(entry.FullName.StartsWith(top + "/", StringComparison.Ordinal), entry.FullName);
                    using (var s = entry.Open())
                    using (var m = new MemoryStream())
                    {
                        s.CopyTo(m);
                        entries[entry.FullName.Substring(top.Length + 1)] = m.ToArray();
                    }
                }
            }

            return entries;
        }

        private static void AssertEntries(IDictionary<string, byte[]> want, IDictionary<string, byte[]> got, string label)
        {
            CollectionAssert.AreEquivalent(want.Keys, got.Keys, label + ": the entries");
            foreach (var pair in want)
            {
                Assert.AreEqual(Encoding.UTF8.GetString(pair.Value), Encoding.UTF8.GetString(got[pair.Key]), $"{label}: {pair.Key}");
                CollectionAssert.AreEqual(pair.Value, got[pair.Key], $"{label}: {pair.Key}, byte for byte");
            }
        }
    }
}
