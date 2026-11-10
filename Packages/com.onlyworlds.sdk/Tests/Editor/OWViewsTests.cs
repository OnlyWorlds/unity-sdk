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
    /// The views table and its fixture (schema-dist <c>views.json</c> and <c>views.fixture.json</c>, at the dist's root
    /// since serial 113, vendored byte for byte in <c>Tests/Editor/Fixtures/views/</c>): the baked table is the file,
    /// every case gives its row, its sentence and its banners through <see cref="OWViews.ViewOf"/>, and the fixture's
    /// cites name fields their rows name and their types have. The cites' quotes are checked at their lines by
    /// <c>codegen/generate_views.py</c>, which has the profile pages; the package does not carry them. The tests beside
    /// the fixture are the TypeScript SDK's (test/views.test.mjs, 4.147.0), then this SDK's own for the two readings
    /// the file leaves open and the one where it reads narrower than npm.
    /// </summary>
    public class OWViewsTests
    {
        /// <summary>The fixture's sha256 as dist.113's MANIFEST.json records it.</summary>
        private const string FixtureSha256 = "61543b9d39a0dcfc300da40b07ed593ec5e2b815dc3cff1e874f2556c8aa5472";

        private const string World = "0199c3a1-7b2e-7c3d-8e4f-5a6b7c8d9e0f";

        private static JObject _fixture;
        private static JObject _file;

        private static string Dir
        {
            get
            {
                var root = TestPackage.Root();
                return root == null ? null : Path.Combine(root, "Tests", "Editor", "Fixtures", "views");
            }
        }

        private static JObject Load(string name)
        {
            var dir = Dir;
            var path = dir == null ? null : Path.Combine(dir, name);
            return path == null || !File.Exists(path) ? null : OWJson.ParseObject(File.ReadAllText(path));
        }

        private static JObject Fixture => _fixture ??= Load("views.fixture.json");

        private static JObject TheFile => _file ??= Load("views.json");

        private static string Sha(string name)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Path.Combine(Dir, name)))).Replace("-", "")
                .ToLowerInvariant();
        }

        /// <summary>One case per fixture case, named after it.</summary>
        public static IEnumerable<TestCaseData> Cases()
        {
            var fixture = Fixture;
            if (fixture == null)
            {
                yield return new TestCaseData(string.Empty).SetName("Views_FixtureNotFound");
                yield break;
            }

            foreach (var c in fixture["cases"].Cast<JObject>())
            {
                var name = (string)c["name"];
                yield return new TestCaseData(name).SetName("Views_" + name.Replace('-', '_'));
            }
        }

        private static JObject Answer(OWView v) => new JObject
        {
            ["row"] = v.Row == null ? JValue.CreateNull() : new JValue(v.Row.Id),
            ["sentence"] = v.Sentence == null ? JValue.CreateNull() : new JValue(v.Sentence),
            ["banners"] = new JArray(v.Banners),
        };

        private static List<string> Strings(JToken token) =>
            token == null ? new List<string>() : token.Select(t => (string)t).ToList();

        [Test]
        public void TheFiles_AreTheDistsBytes()
        {
            Assert.IsNotNull(Fixture, "the vendored views fixture was not found");
            Assert.IsNotNull(TheFile, "the vendored views.json was not found");
            Assert.AreEqual(FixtureSha256, Sha("views.fixture.json"),
                "the vendored fixture is not dist.113's: re-vendor it from schema-dist (a checkout's line endings change the bytes)");
            Assert.AreEqual(OWViews.Sha256, Sha("views.json"),
                "the vendored views.json is not the one OWViews.Table.cs was baked from: regenerate with codegen/generate_views.py");
        }

        [Test]
        public void TheTable_IsTheFile_TheBannersTheLayoutsAndEveryRow()
        {
            var file = TheFile;
            Assert.IsNotNull(file, "the vendored views.json was not found");
            Assert.AreEqual(1, (int)file["_meta"]["version"]);
            Assert.AreEqual(1, OWViews.Version);

            CollectionAssert.AreEqual(file["banners"].Select(b => ((string)b["mark"], (string)b["defined_by"])).ToList(),
                OWViews.Banners.Select(b => (b.Mark, b.DefinedBy)).ToList());
            CollectionAssert.AreEquivalent(((JObject)file["layouts"]).Properties().ToDictionary(p => p.Name, p => (string)p.Value),
                OWViews.Layouts);

            var rows = (JArray)file["rows"];
            Assert.Greater(OWViews.Rows.Count, 0, "a table with no rows would pass every case that wants no row");
            Assert.AreEqual(rows.Count, OWViews.Rows.Count);
            for (var i = 0; i < rows.Count; i++)
            {
                var want = (JObject)rows[i];
                var got = OWViews.Rows[i];
                var id = (string)want["id"];
                Assert.AreEqual(id, got.Id);
                var kind = (JObject)want["kind"];
                Assert.AreEqual((string)kind["profile"], got.Kind.Profile, id);
                Assert.AreEqual((string)kind["pair"], got.Kind.Pair, id);
                if (kind["profile"] == null)
                {
                    Assert.AreEqual((string)kind["type"], got.Kind.Type, id);
                    Assert.AreEqual((string)kind["supertype"], got.Kind.Supertype, id);
                }
                else
                {
                    Assert.AreEqual((string)kind["pair"], got.Kind.Type + "/" + got.Kind.Supertype, id);
                }

                Assert.AreEqual((string)want["from"], got.From, id);
                CollectionAssert.AreEqual(Strings(want["sentence"]), got.Sentence, id);
                CollectionAssert.AreEqual(Strings(want["lead"]), got.Lead, id);
                CollectionAssert.AreEqual(
                    (want["fold"] ?? new JArray()).Select(f => ((string)f["field"], (string)f["label"])).ToList(),
                    got.Fold.Select(f => (f.Field, f.Label)).ToList(), id);
                Assert.AreEqual((string)want["layout"]["name"], got.Layout.Name, id);
                Assert.AreEqual((string)want["layout"]["by"], got.Layout.By, id);
                CollectionAssert.AreEqual(Strings(want["banner"]), got.Banner, id);
                var keys = new[] { "id", "kind", "from", "sentence", "lead", "fold", "layout", "banner" };
                CollectionAssert.IsSubsetOf(want.Properties().Select(p => p.Name).ToList(), keys,
                    $"row {id} carries a column OWViews does not read");
            }
        }

        [Test]
        public void TheFixture_HoldsDist113sCases_AndEveryRowIsReached()
        {
            Assert.IsNotNull(Fixture, "the vendored views fixture was not found");
            Assert.AreEqual("views.json", (string)Fixture["views"]);
            var cases = Fixture["cases"].Cast<JObject>().ToList();
            Assert.GreaterOrEqual(cases.Count, 28, "dist.113's fixture holds 28 cases");
            var seen = new HashSet<string>(cases.Select(c => OWViews.ViewOf(c["element"]).Row?.Id).Where(id => id != null));
            foreach (var r in OWViews.Rows) Assert.IsTrue(seen.Contains(r.Id), "no case reaches the row " + r.Id);
        }

        [TestCaseSource(nameof(Cases))]
        public void EveryCase_GivesItsRowItsSentenceAndItsBanners(string name)
        {
            Assert.IsNotNull(Fixture, "the vendored views fixture was not found");
            var c = Fixture["cases"].Cast<JObject>().Single(x => (string)x["name"] == name);
            var got = OWViews.ViewOf(c["element"]);
            var want = (JObject)c["expect"];
            Assert.IsTrue(JToken.DeepEquals(want, Answer(got)), $"case {name}: want {want}, got {Answer(got)}");
            if (got.Row != null)
                Assert.AreSame(OWViews.Rows.Single(r => r.Id == got.Row.Id), got.Row, "the row is the table's own row");
        }

        /// <summary>
        /// The fixture's third note: each cite names a field its row names, each field a row names has a cite, and each
        /// is a field of the row's type in the standard (a base field included). The quotes are checked at their lines
        /// by the codegen.
        /// </summary>
        [Test]
        public void TheCites_NameFieldsTheirRowsNameAndTheirTypesHave()
        {
            Assert.IsNotNull(Fixture, "the vendored views fixture was not found");
            var cites = Fixture["cites"].Cast<JObject>().ToList();
            Assert.AreEqual(10, cites.Count, "dist.113's fixture holds 10 cites");
            var named = new HashSet<(string, string)>();
            foreach (var r in OWViews.Rows)
            {
                foreach (var f in r.Sentence.Concat(r.Lead).Concat(r.Fold.Select(x => x.Field))) named.Add((r.Id, f));
                if (r.Layout.By != null) named.Add((r.Id, r.Layout.By));
            }

            var cited = new HashSet<(string, string)>();
            foreach (var c in cites)
            {
                var key = ((string)c["row"], (string)c["field"]);
                Assert.IsTrue(named.Contains(key), $"the cite of {key} names a field its row does not name");
                var row = OWViews.Rows.Single(r => r.Id == key.Item1);
                var fields = OWElementTypes.FieldNames[row.Kind.Type].Concat(OWElementTypes.BaseFieldNames);
                CollectionAssert.Contains(fields.ToList(), key.Item2, $"{key}: not a field of {row.Kind.Type}");
                Assert.IsTrue(((string)c["page"]).Length > 0 && (int)c["line"] > 0 && ((string)c["quote"]).Length > 0);
                cited.Add(key);
            }

            CollectionAssert.IsEmpty(named.Except(cited).ToList(), "fields a row names with no cite");
        }

        // -- Beside the fixture (the TypeScript SDK's own tests) -------------------

        [Test]
        public void AnythingThatIsNotAnElement_HasNoRowNoSentenceAndNoBanners()
        {
            foreach (var bad in new JToken[] { null, JValue.CreateNull(), new JValue(1), new JValue("narrative"), new JArray(), new JValue(true), new JObject() })
            {
                var v = OWViews.ViewOf(bad);
                Assert.IsNull(v.Row, bad?.ToString() ?? "null");
                Assert.IsNull(v.Sentence);
                CollectionAssert.IsEmpty(v.Banners);
            }
        }

        /// <summary>
        /// The fixture's carriage-return case is CRLF, which a cut at the line feed alone also passes (the trim takes the
        /// CR left behind); a lone CR tells the two apart.
        /// </summary>
        [Test]
        public void TheSentence_IsCutAtALoneCarriageReturnAsAtALineFeed()
        {
            string Msg(string story) => OWViews.ViewOf(new JObject
            {
                ["type"] = "narrative", ["supertype"] = "Message", ["story"] = story,
            }).Sentence;

            Assert.AreEqual("One.", Msg("One.\rTwo."));
            Assert.AreEqual("One.", Msg("One.\nTwo."));
            Assert.AreEqual("One.", Msg("  \r\n One. \rTwo."));
        }

        [Test]
        public void EachAnswer_IsItsOwn_AndCannotBeChanged()
        {
            var el = new JObject { ["type"] = "narrative", ["supertype"] = "Message", ["story"] = "Hello.", ["hidden"] = true };
            var first = OWViews.ViewOf(el);
            CollectionAssert.AreEqual(new[] { "hidden" }, first.Banners);
            Assert.IsFalse(first.Banners is string[], "the banners are a read-only list, not an array a caller could write");
            Assert.Throws<NotSupportedException>(() => ((IList<string>)first.Banners).Add("changed"));
            Assert.AreNotSame(first.Banners, OWViews.ViewOf(el).Banners);
        }

        // -- Where the file is silent or narrower than npm -------------------------

        private static OWViewRow Row(string id, OWViewKind kind, params string[] banner) =>
            new OWViewRow(id, kind, "a test", new[] { "story" }, Array.Empty<string>(), Array.Empty<OWViewFold>(),
                new OWViewLayout("document", null), banner);

        /// <summary>
        /// Undefined by the file, npm's choice: the file says a row's own <c>banner</c> "adds to" the table's, and not
        /// how its mark is read as present. The TypeScript SDK 4.147.0 (<c>src/views.ts</c>, <c>carries</c>) reads it
        /// by presence as the room profile reads its mark: present and not null, a string only when something is left
        /// after trimming White_Space, any other value a mark.
        /// </summary>
        [TestCase("\"" + World + "\"", true)]
        [TestCase("\"  \"", false)]
        [TestCase("\"\"", false)]
        [TestCase("7", true)]
        [TestCase("false", true)]
        [TestCase("{}", true)]
        [TestCase("null", false)]
        public void ARowsOwnBannerMark_IsReadByPresence_UndefinedByTheFile_NpmsChoice(string json, bool shown)
        {
            var rows = new[] { Row("noted", OWViewKind.OfType("character", null), "x_note") };
            var el = new JObject { ["type"] = "character", ["x_note"] = JToken.Parse(json) };
            var v = OWViews.ViewOf(el, OWViews.Banners, rows);
            Assert.AreEqual("noted", v.Row?.Id);
            CollectionAssert.AreEqual(shown ? new[] { "x_note" } : Array.Empty<string>(), v.Banners, json);
            Assert.AreEqual(OWViews.ViewOf(new JObject { ["type"] = "character" }, OWViews.Banners, rows).Banners.Count, 0,
                "an absent mark is no banner");
        }

        /// <summary>
        /// Undefined by the file, npm's choice: the file ranks the rungs and says no two rows hold one kind, but not
        /// which wins when two rows tie on one rung. Under the file's own rows no element can match two (a kind is
        /// its folded type and supertype, or a profile's pair, and the codegen refuses two rows of one kind), so the
        /// tie is built here past that check. The TypeScript SDK 4.147.0 keeps the first row in the table's order
        /// (<c>viewOf</c>: a later row must score higher to win), and so does this.
        /// </summary>
        [Test]
        public void ATieOnOneRung_TheFirstRowWins_UndefinedByTheFile_NpmsChoice()
        {
            var el = new JObject { ["type"] = "narrative", ["supertype"] = "Message", ["story"] = "Hi." };
            var a = Row("first", OWViewKind.OfType("narrative", "Message"));
            var b = Row("second", OWViewKind.OfType("Narrative", " message "));
            Assert.AreEqual("first", OWViews.ViewOf(el, OWViews.Banners, new[] { a, b }).Row.Id);
            Assert.AreEqual("second", OWViews.ViewOf(el, OWViews.Banners, new[] { b, a }).Row.Id);

            var plain = Row("plain", OWViewKind.OfType("narrative", null));
            Assert.AreEqual("first", OWViews.ViewOf(el, OWViews.Banners, new[] { plain, a }).Row.Id,
                "a higher rung still wins from later in the table");
        }

        /// <summary>
        /// The file's words, narrower than npm: <c>x_served_trimmed</c> shows "only when it names a field". The
        /// TypeScript SDK 4.147.0 shows it for any non-empty list; this shows it for a list holding a non-empty string,
        /// so a list of only <c>""</c>, a number or null names nothing. The fixture's empty list is no banner in both.
        /// </summary>
        [TestCase("[\"characters\"]", true)]
        [TestCase("[\"narrator\", \"characters\"]", true)]
        [TestCase("[]", false)]
        [TestCase("[\"\"]", false)]
        [TestCase("[42]", false)]
        [TestCase("[null]", false)]
        [TestCase("\"characters\"", false)]
        public void ServedTrimmed_IsABannerOnlyWhenItNamesAField_TheFilesWords(string json, bool shown)
        {
            var el = new JObject { ["type"] = "narrative", ["supertype"] = "Message", ["story"] = "Hi.", ["x_served_trimmed"] = JToken.Parse(json) };
            CollectionAssert.AreEqual(shown ? new[] { "x_served_trimmed" } : Array.Empty<string>(), OWViews.ViewOf(el).Banners, json);
        }
    }
}
