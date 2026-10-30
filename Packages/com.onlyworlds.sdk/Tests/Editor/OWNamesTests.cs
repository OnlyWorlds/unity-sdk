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
    /// The names profile's conformance fixture (schema-dist <c>profiles/names.fixture.json</c>, tag
    /// <c>v0.33.0-dist.94</c>), vendored byte for byte in <c>Tests/Editor/Fixtures/names/</c>, read case by case through
    /// <see cref="OWNames.TypeWord"/>. Each case is a names map, a type slug, an optional supertype and whether a plural
    /// is printed; its word is what every reader prints, or null for the standard's name. The TypeScript SDK and the
    /// Python package run the same cases. When the dist moves the fixture, re-vendor it and update the hash below.
    /// </summary>
    public class OWNamesTests
    {
        /// <summary>The fixture's sha256 as dist.94's MANIFEST.json records it.</summary>
        private const string DistSha256 = "7d012081b5df7bab3e2223333f87a347417c3061c62b38947fc952e8c4d2377a";

        private static JObject _fixture;

        private static string FixturePath
        {
            get
            {
                var root = TestPackage.Root();
                return root == null ? null : Path.Combine(root, "Tests", "Editor", "Fixtures", "names", "names.fixture.json");
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
                yield return new TestCaseData(string.Empty).SetName("Names_FixtureNotFound");
                yield break;
            }

            foreach (var c in fixture["cases"].Cast<JObject>())
            {
                var name = (string)c["name"];
                yield return new TestCaseData(name).SetName("Names_" + name.Replace('-', '_'));
            }
        }

        [Test]
        public void TheFixture_IsTheDistsBytes()
        {
            var path = FixturePath;
            Assert.IsTrue(path != null && File.Exists(path), "the vendored names fixture was not found: " + path);
            using var sha = SHA256.Create();
            var hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
            Assert.AreEqual(DistSha256, hash,
                "the vendored fixture is not dist.94's: re-vendor it from schema-dist (a checkout's line endings change the bytes)");
        }

        [Test]
        public void TheFixture_HoldsTheProfilesCases()
        {
            Assert.IsNotNull(Fixture, "the vendored names fixture was not found");
            Assert.AreEqual("x_names", (string)Fixture["profile"]);
            Assert.AreEqual(29, ((JArray)Fixture["cases"]).Count, "dist.94's fixture holds 29 cases");
        }

        [TestCaseSource(nameof(Cases))]
        public void EveryCase_PrintsTheProfilesWord(string name)
        {
            Assert.IsNotNull(Fixture, "the vendored names fixture was not found");
            var c = Fixture["cases"].Cast<JObject>().Single(x => (string)x["name"] == name);
            var world = new JObject { [OWNames.NamesField] = c["map"].DeepClone() };
            var supertype = c["supertype"] is JValue s && s.Type == JTokenType.String ? (string)s : null;

            var word = OWNames.TypeWord(world, (string)c["type"], supertype, (bool)c["many"]);

            Assert.AreEqual((string)c["word"], word, $"case {name}");
        }

        // -- Beside the fixture ------------------------------------------------

        private static JObject World(string names) => OWJson.ParseObject(@"{""name"":""W"",""x_names"":" + names + "}");

        /// <summary>One code point as a string, so an invisible one is named in the source.</summary>
        private static string U(int codePoint) => char.ConvertFromUtf32(codePoint);

        private const string Agent = @"{""version"":1,""supertypes"":{""character"":{""Agent"":{""one"":""AI agent""}}}}";

        [Test]
        public void NoWorld_NoMap_OrAMapThatIsAString_IsTheStandardName()
        {
            Assert.IsNull(OWNames.TypeWord(null, "character"));
            Assert.IsNull(OWNames.TypeWord(OWJson.ParseObject(@"{""name"":""W""}"), "character"));
            Assert.IsNull(OWNames.TypeWord(World(@"""Employee"""), "character"));
            Assert.IsNull(OWNames.TypeWord(World(@"null"), "character"));
        }

        [Test]
        public void TheSlug_IsMatchedAsTheSchemaSpellsIt()
        {
            var world = World(@"{""version"":1,""types"":{""character"":{""one"":""Employee""}}}");

            Assert.AreEqual("Employee", OWNames.TypeWord(world, "character"));
            Assert.IsNull(OWNames.TypeWord(world, "Character"), "a type slug is lowercase; the fold is the supertype's");
            Assert.IsNull(OWNames.TypeWord(world, null));
        }

        [Test]
        public void VersionOne_AsAFloat_IsANamesMap_AsJavaScriptReadsIt()
        {
            Assert.AreEqual("Employee", OWNames.TypeWord(World(@"{""version"":1.0,""types"":{""character"":{""one"":""Employee""}}}"), "character"));
            Assert.IsNull(OWNames.TypeWord(World(@"{""version"":""1"",""types"":{""character"":{""one"":""Employee""}}}"), "character"));
        }

        /// <summary>A key that is not one of the 22 slugs is ignored, and nothing else names it: asked for, it reads null.</summary>
        [Test]
        public void AnUnknownSlug_AskedForByType_ReadsNull()
        {
            var world = World(@"{""version"":1,""types"":{""employee"":{""one"":""Staff""},""character"":{""one"":""Employee""}}}");

            Assert.IsNull(OWNames.TypeWord(world, "employee"));
            Assert.AreEqual("Employee", OWNames.TypeWord(world, "character"), "the map's other entries stand");
        }

        /// <summary>A <c>version</c> of <c>1.0</c> is the number 1 and accepted; a fraction is refused.</summary>
        [Test]
        public void AVersionWithAFraction_IsRefused()
        {
            Assert.AreEqual("Employee", OWNames.TypeWord(World(@"{""version"":1e0,""types"":{""character"":{""one"":""Employee""}}}"), "character"));
            Assert.IsNull(OWNames.TypeWord(World(@"{""version"":1.5,""types"":{""character"":{""one"":""Employee""}}}"), "character"));
        }

        /// <summary>
        /// When two keys of <c>supertypes[type]</c> fold to the element's supertype, the first in the map's order that is
        /// an entry (a string <c>one</c>) gives the word; one with no word is passed over.
        /// </summary>
        [Test]
        public void TwoKeysThatFoldAlike_TheFirstEntryInTheMapsOrderGivesTheWord()
        {
            var world = World(@"{""version"":1,""supertypes"":{""character"":{""agent"":{""one"":""  ""},""AGENT"":{""one"":""Operative""},""Agent"":{""one"":""AI agent""}}}}");

            Assert.AreEqual("Operative", OWNames.TypeWord(world, "character", "Agent"));
        }

        [Test]
        public void ANullTypesOrSupertypes_IsPresent_AndRefusesTheMap()
        {
            // "Present and not an object" refuses the map, and a JSON null is present (ruled with npm's reading).
            Assert.IsNull(OWNames.TypeWord(World(@"{""version"":1,""types"":null,""supertypes"":{""character"":{""Agent"":{""one"":""AI agent""}}}}"),
                "character", "Agent"), "types null");
            Assert.IsNull(OWNames.TypeWord(World(@"{""version"":1,""types"":{""character"":{""one"":""Employee""}},""supertypes"":null}"),
                "character"), "supertypes null");
            Assert.AreEqual("Employee", OWNames.TypeWord(World(@"{""version"":1,""types"":{""character"":{""one"":""Employee""}}}"),
                "character"), "supertypes absent");
        }

        [Test]
        public void TheSupertype_IsTrimmedByUnicodeWhiteSpace_NotTheRuntimesTrim()
        {
            var world = World(Agent);

            Assert.AreEqual("AI agent", OWNames.TypeWord(world, "character", U(0x3000) + "Agent" + U(0x00A0)), "ideographic and no-break spaces trim");
            Assert.IsNull(OWNames.TypeWord(world, "character", U(0xFEFF) + "Agent"), "a byte order mark is not White_Space and stays");
            Assert.IsNull(OWNames.TypeWord(world, "character", U(0x200B) + "Agent"), "a zero width space is not White_Space and stays");
        }

        [Test]
        public void TheSupertype_IsFoldedBySimpleCaseFolding()
        {
            var world = World(@"{""version"":1,""supertypes"":{""character"":{""Σοφός"":{""one"":""Sage""}}}}");

            Assert.AreEqual("Sage", OWNames.TypeWord(world, "character", "ΣΟΦΌΣ"), "capital sigma folds with final sigma");
        }

        [Test]
        public void ABlankSupertype_GoesStraightToTheTypesWord()
        {
            var world = World(@"{""version"":1,""types"":{""character"":{""one"":""Employee""}},""supertypes"":{""character"":{"""":{""one"":""Nobody""}}}}");

            Assert.AreEqual("Employee", OWNames.TypeWord(world, "character", "  "));
            Assert.AreEqual("Employee", OWNames.TypeWord(world, "character", ""));
        }

        [Test]
        public void TheWord_IsPrintedAsWritten()
        {
            var world = World(@"{""version"":1,""types"":{""character"":{""one"":""eMPLOYEE"",""many"":""ai Agents""}}}");

            Assert.AreEqual("eMPLOYEE", OWNames.TypeWord(world, "character"));
            Assert.AreEqual("ai Agents", OWNames.TypeWord(world, "character", many: true));
        }
    }
}
