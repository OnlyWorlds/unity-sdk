using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The folder reader against the Forge's committed conformance fixture.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fixture is a second implementation's answer key -- Atlas's -- and running against it is
    /// the only way to find the places where the spec permits two readings and we each picked a
    /// different one. Its author asked explicitly for the failures rather than the passes, so this
    /// suite is written to make disagreements visible rather than to go green.
    /// </para>
    /// <para>
    /// The fixture lives outside this repository, in a sibling bay. When it is not present these
    /// tests are inconclusive rather than failing: a red suite for a missing external file trains
    /// people to ignore red. <see cref="FixtureAvailable"/> gates them, and the gate reports itself
    /// so an ignored run cannot be mistaken for a passing one.
    /// </para>
    /// </remarks>
    public class OWFolderConformanceTests
    {
        /// <summary>
        /// Where the Forge's conformance fixture lives, if it is on this machine.
        /// </summary>
        /// <remarks>
        /// Set <c>OW_CONFORMANCE_FIXTURE</c> to point at a checkout of it. The default is the
        /// author's local path, which is useless on anyone else's machine -- deliberately, because
        /// the alternative is vendoring a copy of somebody else's answer key into this repo, and a
        /// vendored copy of a fixture is a fixture that silently stops matching the implementation
        /// it was written to check.
        /// </remarks>
        private static string FixtureRoot =>
            System.Environment.GetEnvironmentVariable("OW_CONFORMANCE_FIXTURE")
            ?? @"C:\Users\Titus\Carrier\Forge\tools\atlas\tests\fixtures\folder-conformance";

        private static string WorldPath => Path.Combine(FixtureRoot, "world");
        private static string ExpectedPath => Path.Combine(FixtureRoot, "expected.json");

        private static bool FixtureAvailable => Directory.Exists(WorldPath) && File.Exists(ExpectedPath);

        private static void RequireFixture()
        {
            if (FixtureAvailable) return;

            Assert.Ignore(
                $"Conformance fixture not present at {FixtureRoot}. These assertions did not run -- "
                + "treat this suite as unmeasured, not as passing.");
        }

        private static OWFolderReadResult Read(bool legacy = false)
            => OWFolderReader.Read(WorldPath, includeLegacySpatial: legacy);

        private static JObject Expected() => JObject.Parse(File.ReadAllText(ExpectedPath));

        // -- World ------------------------------------------------------------

        [Test]
        public void World_MatchesTheAnswerKey()
        {
            RequireFixture();
            var result = Read();
            var expected = Expected()["world"];

            Assert.AreEqual(expected["id"].ToString(), result.WorldId);
            Assert.AreEqual(expected["name"].ToString(), result.WorldName);
        }

        [Test]
        public void World_WithNoApiBlock_IsNotSyncable()
        {
            RequireFixture();
            Assert.IsFalse(Read().IsSyncable,
                "No api block means local-only, and no sync machinery may attach.");
        }

        // -- Element count ----------------------------------------------------

        [Test]
        public void NormativeElements_AreAllFound()
        {
            RequireFixture();
            var result = Read();
            var expectedIds = Expected()["elements"].Select(e => e["id"].ToString()).ToList();

            var foundIds = result.Elements.Select(e => e.Id).ToList();

            CollectionAssert.AreEquivalent(expectedIds, foundIds,
                "Every element under elements/<type>/ must be found, and nothing invented.");
        }

        [Test]
        public void LegacySpatial_IsOptOut_AndSeparate()
        {
            RequireFixture();

            Assert.AreEqual(0, Read(legacy: false).LegacyElements.Count,
                "A reader covering only elements/ is conforming.");

            var withLegacy = Read(legacy: true);
            var expectedLegacy = Expected()["legacy_elements"].Select(e => e["id"].ToString()).ToList();

            CollectionAssert.AreEquivalent(expectedLegacy,
                withLegacy.LegacyElements.Select(e => e.Id).ToList());
            Assert.AreEqual(0, withLegacy.LegacyElements.Count(e => withLegacy.Elements.Any(n => n.Id == e.Id)),
                "Legacy elements must not also appear in the normative list.");
        }

        // -- The malformed cases, which matter more than the well-formed ones --

        [Test]
        public void MalformedFiles_AreSkipped_AndLeftOnDisk()
        {
            RequireFixture();

            var before = Directory.GetFiles(WorldPath, "*.json", SearchOption.AllDirectories).ToList();
            var result = Read(legacy: true);
            var after = Directory.GetFiles(WorldPath, "*.json", SearchOption.AllDirectories).ToList();

            CollectionAssert.AreEquivalent(before, after,
                "READING IS NOT EDITING. Every file must still be exactly where it was.");

            foreach (var skip in Expected()["skipped_files"])
            {
                var name = Path.GetFileName(skip["path"].ToString());
                Assert.IsTrue(result.Skipped.Any(s => Path.GetFileName(s.Path) == name),
                    $"{name} must be reported as skipped, not silently dropped.");
                Assert.IsTrue(File.Exists(Path.Combine(WorldPath, skip["path"].ToString().Replace('/', Path.DirectorySeparatorChar))),
                    $"{name} must remain on disk.");
            }
        }

        [Test]
        public void OneBadFile_DoesNotTakeOutTheWorld()
        {
            RequireFixture();
            var result = Read();

            Assert.GreaterOrEqual(result.Elements.Count, 6,
                "A truncated file must not cost us the other elements -- Atlas once had a single "
                + "file with no type blank an entire sidebar, console-only.");
            Assert.AreEqual(2, result.Skipped.Count, "Exactly the id-less and the truncated file.");
        }

        [Test]
        public void BodyTypeWins_OverTheDirectory()
        {
            RequireFixture();
            var element = Read().Elements.Single(e => e.Id.EndsWith("502"));

            Assert.AreEqual("location", element.Type,
                "It sits in elements/character/ and declares location. The declaration is the "
                + "stronger claim.");
            Assert.IsFalse(element.TypeInferred);
        }

        [Test]
        public void MissingType_IsInferredFromTheDirectory_AndNothingIsInvented()
        {
            RequireFixture();
            var element = Read().Elements.Single(e => e.Id.EndsWith("501"));

            Assert.AreEqual("character", element.Type);
            Assert.IsTrue(element.TypeInferred, "Inference must be visible, not silent.");

            foreach (var forbidden in Expected()["elements"]
                         .Single(e => e["id"].ToString().EndsWith("501"))["must_not_have"])
            {
                Assert.IsNull(element.Body[forbidden.ToString()],
                    $"A reader must not synthesize {forbidden}.");
            }
        }

        [Test]
        public void EmptyStringName_IsLegal_AndNotAPlaceholder()
        {
            RequireFixture();
            var element = Read().Elements.Single(e => e.Id.EndsWith("509"));

            Assert.IsNotNull(element.Body["name"], "The key is present...");
            Assert.AreEqual(JTokenType.String, element.Body["name"].Type);
            Assert.AreEqual("", element.Body["name"].ToString(),
                "...and its value is the empty string, which is a different claim from unnamed. "
                + "A reader may display a fallback; it must never write one back.");
        }

        [Test]
        public void ExplicitNulls_AreNeverCollapsed()
        {
            RequireFixture();
            var element = Read().Elements.Single(e => e.Id.EndsWith("50a"));

            foreach (var field in new[] { "description", "birth_date", "species", "traits" })
            {
                Assert.IsNotNull(element.Body[field], $"{field} must still be present...");
                Assert.AreEqual(JTokenType.Null, element.Body[field].Type,
                    $"...and still explicitly null. null means UNSET and is never 0, \"\" or [].");
            }
        }

        [Test]
        public void MaplessPin_IsServedAsItIs()
        {
            RequireFixture();
            var pin = Read().Elements.Single(e => e.Id.EndsWith("505"));

            Assert.AreEqual("pin", pin.Type);
            Assert.IsTrue(pin.Body["map"] == null || pin.Body["map"].Type == JTokenType.Null,
                "A mapless pin is a legitimate app pattern -- it must not crash, and must not be "
                + "given a map it does not have.");
        }

        [Test]
        public void ExtensionFields_SurviveVerbatim()
        {
            RequireFixture();
            var element = Read().Elements.Single(e => e.Id.EndsWith("507"));

            Assert.IsNotNull(element.Body["x_probe_note"],
                "Another tool's extension field must round-trip untouched -- normalising or "
                + "reserializing one at any depth is a byte-fidelity violation.");
        }

        // -- Into the cache ---------------------------------------------------

        [Test]
        public void LoadsIntoACache_KeyedBySourceAndPath()
        {
            RequireFixture();
            var cache = ScriptableObject.CreateInstance<OWWorldCache>();
            try
            {
                var read = OWFolderLoader.LoadInto(cache, WorldPath, includeLegacySpatial: false);

                Assert.AreEqual(read.Elements.Count, cache.Count);
                Assert.AreEqual(OWSourceKind.Folder, cache.Key.Source);
                Assert.AreEqual(read.WorldId, cache.Key.WorldId);

                // The collision the key exists to prevent: same world id, different folders.
                var other = OWWorldKey.FromFolder(read.WorldId, WorldPath + "-chapter-12");
                Assert.AreNotEqual(cache.Key, other,
                    "Two snapshots of one world share a world id. Keyed by id alone they collide "
                    + "and the wrong one opens with a correct-looking UI.");
            }
            finally
            {
                Object.DestroyImmediate(cache);
            }
        }

        [Test]
        public void TypeContradictingItsDirectory_LandsInTheRightBucket()
        {
            RequireFixture();
            var cache = ScriptableObject.CreateInstance<OWWorldCache>();
            try
            {
                OWFolderLoader.LoadInto(cache, WorldPath, includeLegacySpatial: false);

                Assert.AreEqual(1, cache.AllRaw("location").Count,
                    "The element declaring location must be queryable as a location, even though "
                    + "it sits in elements/character/.");
            }
            finally
            {
                Object.DestroyImmediate(cache);
            }
        }
    }
}
