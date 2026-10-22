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
    /// <c>v0.33.0-dist.48</c>), vendored byte for byte in <c>Tests/Editor/Fixtures/names/</c>, read case by case through
    /// <see cref="OWNames.TypeWord"/>. Each case is a names map, a type slug, an optional supertype and whether a plural
    /// is printed; its word is what every reader prints, or null for the standard's name. The TypeScript SDK and the
    /// Python package run the same cases. When the dist moves the fixture, re-vendor it and update the hash below.
    /// </summary>
    public class OWNamesTests
    {
        /// <summary>The fixture's sha256 as dist.48's MANIFEST.json records it.</summary>
        private const string DistSha256 = "a3224ef265506869ca7aa0d4848ea6f62ce04b93d4b18588394be0d7982927af";

        private static JObject _fixture;

        /// <summary>
        /// The package's own resolved path; outside the Editor (no package manager to ask), the working directory's
        /// <c>Packages/com.onlyworlds.sdk</c>, as <c>OWWireContractTests</c> finds its fixture.
        /// </summary>
        private static string PackageRoot()
        {
            try
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(OWNamesTests).Assembly);
                if (package != null) return package.resolvedPath;
            }
            catch (Exception)
            {
                // No Editor behind the call; try the working directory below.
            }

            var local = Path.Combine(Directory.GetCurrentDirectory(), "Packages", "com.onlyworlds.sdk");
            return Directory.Exists(local) ? local : null;
        }

        private static string FixturePath
        {
            get
            {
                var root = PackageRoot();
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
                "the vendored fixture is not dist.48's: re-vendor it from schema-dist (a checkout's line endings change the bytes)");
        }

        [Test]
        public void TheFixture_HoldsTheProfilesCases()
        {
            Assert.IsNotNull(Fixture, "the vendored names fixture was not found");
            Assert.AreEqual("x_names", (string)Fixture["profile"]);
            Assert.AreEqual(23, ((JArray)Fixture["cases"]).Count, "dist.48's fixture holds 23 cases");
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
