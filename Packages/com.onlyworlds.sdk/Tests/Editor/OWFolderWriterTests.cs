using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The folder writer, measured in bytes rather than in parsed values.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Almost every assertion here compares BYTES. That is the point of the suite: the failure this
    /// writer exists to avoid is not a world that reads back wrong -- it is a world that reads back
    /// right and looks, to version control, like every file changed. A test that compares parsed
    /// JSON passes happily while the repository fills with noise.
    /// </para>
    /// <para>
    /// The fixture is eight real files from a real world (Sikelia), copied in unmodified, covering
    /// seven element types. Real files carry what invented ones do not: microsecond timestamps,
    /// empty-string names, explicit nulls, empty arrays beside populated ones, and a bare
    /// <c>map</c> link.
    /// </para>
    /// </remarks>
    public class OWFolderWriterTests
    {
        private string _temp;

        /// <summary>The committed fixture -- eight real files, byte-for-byte as the world holds them.</summary>
        /// <remarks>
        /// Found by walking up from the assets folder, so the suite does not care whether the
        /// package sits in <c>Packages/</c>, in <c>Library/PackageCache/</c>, or under a project
        /// name nobody here chose. A hardcoded relative path works in exactly one layout.
        /// </remarks>
        private static string FixtureRoot
        {
            get
            {
                if (_fixtureRoot != null) return _fixtureRoot.Length == 0 ? null : _fixtureRoot;

                var projectRoot = Directory.GetParent(UnityEngine.Application.dataPath)?.FullName;
                _fixtureRoot = string.Empty;

                foreach (var root in new[] { "Packages", "Library/PackageCache" })
                {
                    if (projectRoot == null) break;

                    var search = Path.Combine(projectRoot, root.Replace('/', Path.DirectorySeparatorChar));
                    if (!Directory.Exists(search)) continue;

                    var hit = Directory
                        .GetDirectories(search, "folder-write", SearchOption.AllDirectories)
                        .FirstOrDefault(d => File.Exists(Path.Combine(d, "world.json")));

                    if (hit != null) { _fixtureRoot = hit; break; }
                }

                return _fixtureRoot.Length == 0 ? null : _fixtureRoot;
            }
        }

        private static string _fixtureRoot;

        private static bool FixtureAvailable =>
            FixtureRoot != null && File.Exists(Path.Combine(FixtureRoot, "world.json"));

        private static void RequireFixture()
        {
            if (FixtureAvailable) return;
            Assert.Ignore(
                $"Write fixture not present at {FixtureRoot}. These assertions did not run -- "
                + "treat them as unmeasured, not as passing.");
        }

        [SetUp]
        public void SetUp()
        {
            _temp = Path.Combine(Path.GetTempPath(), "ow-folder-writer-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temp);
            // The reader refuses a folder without world.json (id and name are its only required
            // keys), and several cases read the temp folder back through it.
            File.WriteAllText(Path.Combine(_temp, "world.json"),
                "{\n  \"id\": \"" + Guid.NewGuid() + "\",\n  \"name\": \"writer-temp\"\n}\n");
        }

        [TearDown]
        public void TearDown()
        {
            if (_temp != null && Directory.Exists(_temp)) Directory.Delete(_temp, recursive: true);
        }

        private static byte[] Bytes(string path) => File.ReadAllBytes(path);

        private static JObject Body(string id, string type, string name)
        {
            var body = new JObject { ["type"] = type, ["id"] = id };
            if (name != null) body["name"] = name;
            return body;
        }

        // -- The round trip, which is the whole contract ----------------------

        [Test]
        public void RoundTrip_IsByteIdentical_OnTheFixture()
        {
            RequireFixture();

            var read = OWFolderReader.Read(FixtureRoot, includeLegacySpatial: false);
            Assert.IsNotEmpty(read.Elements, "The fixture must contain elements, or this proves nothing.");

            foreach (var element in read.Elements)
            {
                // Re-read verbatim. The reader's JObject came from JObject.Parse, which coerces
                // timestamps -- see WriterParse_DoesNotRewriteTimestamps below for why that matters.
                var body = OWFolderWriter.ReadJsonFile(element.Path);
                var written = OWFolderWriter.WriteElement(_temp, body);

                CollectionAssert.AreEqual(Bytes(element.Path), Bytes(written),
                    $"{Path.GetFileName(element.Path)} did not survive a round trip byte-for-byte. "
                    + "A world folder lives in version control: bytes are the contract, not values.");

                Assert.AreEqual(Path.GetFileName(element.Path), Path.GetFileName(written),
                    "The writer must independently derive the filename the world already uses.");
            }
        }

        [Test]
        public void RoundTrip_WorldJson_IsByteIdentical()
        {
            RequireFixture();

            var source = Path.Combine(FixtureRoot, "world.json");
            var written = OWFolderWriter.WriteWorld(_temp, OWFolderWriter.ReadJsonFile(source));

            CollectionAssert.AreEqual(Bytes(source), Bytes(written),
                "world.json must round-trip byte-for-byte, including its full time_* block.");
        }

        [Test]
        public void RoundTrip_IsByteIdentical_OnTheRealWorld()
        {
            var world = RealWorldPath();
            if (world == null)
            {
                Assert.Ignore(
                    "The real Sikelia world folder is not on this machine. Set OW_WORLD_FOLDER to a "
                    + "world folder to run this -- these assertions did not run.");
            }

            var files = Directory
                .GetFiles(Path.Combine(world, "elements"), "*.json", SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.Ordinal)
                .Take(50)
                .ToList();

            Assert.IsNotEmpty(files, "A world folder with no element files cannot measure anything.");

            foreach (var file in files)
            {
                var written = OWFolderWriter.WriteElement(_temp, OWFolderWriter.ReadJsonFile(file));

                CollectionAssert.AreEqual(Bytes(file), Bytes(written),
                    $"{Path.GetFileName(file)} from the live world did not round-trip byte-for-byte.");
            }
        }

        /// <summary>The real world folder, if this machine has one.</summary>
        private static string RealWorldPath()
        {
            var candidates = new[]
            {
                Environment.GetEnvironmentVariable("OW_WORLD_FOLDER"),
                @"C:\Users\Titus\Development\OnlyWorlds\sikelia-world",
            };

            return candidates.FirstOrDefault(c =>
                !string.IsNullOrEmpty(c) && Directory.Exists(Path.Combine(c ?? "", "elements")));
        }

        // -- The bytes themselves ---------------------------------------------

        [Test]
        public void Serialize_UsesLf_TwoSpaceIndent_AndOneTrailingNewline()
        {
            var text = OWFolderWriter.Serialize(new JObject
            {
                ["id"] = "x",
                ["tags"] = new JArray("a"),
            });

            StringAssert.DoesNotContain("\r", text,
                "CRLF would differ from every file another implementation wrote, on every line. "
                + "A TextWriter defaults to Environment.NewLine, so this is not free on Windows.");

            StringAssert.Contains("\n  \"id\": \"x\"", text, "Two-space indent.");

            Assert.IsTrue(text.EndsWith("}\n", StringComparison.Ordinal), "Exactly one trailing newline.");
            Assert.IsFalse(text.EndsWith("}\n\n", StringComparison.Ordinal), "Not two.");
        }

        [Test]
        public void WrittenFiles_HaveNoByteOrderMark()
        {
            OWFolderWriter.WriteElement(_temp, Body("11111111-2222-4333-8444-555555555501", "character", "Alice"));

            var file = Directory.GetFiles(Path.Combine(_temp, "elements", "character")).Single();
            var bytes = Bytes(file);

            Assert.IsTrue(bytes.Length >= 3, "The file must have content.");
            Assert.IsFalse(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                "Encoding.UTF8 writes a BOM. The format says none, and three stray bytes make diff "
                + "call the whole file changed.");
            Assert.AreEqual((byte)'{', bytes[0]);
        }

        [Test]
        public void KeyOrder_IsAsReceived_NeverSorted()
        {
            var body = new JObject
            {
                ["type"] = "character",
                ["id"] = "11111111-2222-4333-8444-555555555501",
                ["name"] = "Zoe",
                ["age"] = 30,
            };

            var text = OWFolderWriter.Serialize(body);
            var typeAt = text.IndexOf("\"type\"", StringComparison.Ordinal);
            var idAt = text.IndexOf("\"id\"", StringComparison.Ordinal);
            var nameAt = text.IndexOf("\"name\"", StringComparison.Ordinal);
            var ageAt = text.IndexOf("\"age\"", StringComparison.Ordinal);

            Assert.Less(typeAt, idAt, "Keys keep the order they arrived in.");
            Assert.Less(idAt, nameAt);
            Assert.Less(nameAt, ageAt,
                "Sorting would stabilise the diff and destroy the byte-fidelity of foreign "
                + "extension values, which is the worse trade.");
        }

        [Test]
        public void WriterParse_DoesNotRewriteTimestamps()
        {
            // Measured against the real world: JObject.Parse turned
            // "2026-09-04T20:36:14.605251+00:00" into "...T22:36:14.605251+02:00" -- the same
            // instant, in the writing machine's timezone. Valid JSON, identical semantics, and a
            // whole-repository diff whose content depends on where it was written.
            const string stamp = "2026-09-04T20:36:14.605251+00:00";
            var source = "{\n  \"id\": \"x\",\n  \"created_at\": \"" + stamp + "\"\n}\n";

            var verbatim = OWFolderWriter.Parse(source);

            Assert.AreEqual(JTokenType.String, verbatim["created_at"].Type,
                "The timestamp must stay a string. Parsed to DateTime, it re-serializes in the "
                + "local timezone and the bytes stop being the writer's choice.");
            Assert.AreEqual(stamp, verbatim["created_at"].ToString());
            Assert.AreEqual(source, OWFolderWriter.Serialize(verbatim));
        }

        [Test]
        public void ExtensionFields_AndExplicitNulls_SurviveVerbatim()
        {
            var source =
                "{\n"
                + "  \"type\": \"character\",\n"
                + "  \"id\": \"11111111-2222-4333-8444-555555555501\",\n"
                + "  \"name\": \"\",\n"
                + "  \"description\": null,\n"
                + "  \"traits\": [],\n"
                + "  \"x_probe\": {\n"
                + "    \"eras\": [\n"
                + "      {\n"
                + "        \"name\": \"Old Earth\",\n"
                + "        \"start\": null\n"
                + "      }\n"
                + "    ]\n"
                + "  }\n"
                + "}\n";

            Assert.AreEqual(source, OWFolderWriter.Serialize(OWFolderWriter.Parse(source)),
                "A foreign extension value must not be normalised, reordered or re-indented at any "
                + "depth -- and an explicit null is UNSET, never 0, \"\" or [].");
        }

        // -- Identity is the id, not the filename -----------------------------

        [Test]
        public void Rename_RemovesTheOldFile()
        {
            const string id = "11111111-2222-4333-8444-555555555501";

            var first = OWFolderWriter.WriteElement(_temp, Body(id, "character", "Alice"));
            Assert.IsTrue(File.Exists(first));

            var second = OWFolderWriter.WriteElement(_temp, Body(id, "character", "Beatrice"));

            Assert.AreNotEqual(first, second, "A new name means a new path.");
            Assert.IsTrue(File.Exists(second), "The renamed file must exist...");
            Assert.IsFalse(File.Exists(first),
                "...and the old one must be gone. Leaving it forks one element into two, and the "
                + "reader -- keyed on id -- serves whichever it happens to see last.");

            Assert.AreEqual(1, Directory.GetFiles(Path.Combine(_temp, "elements", "character")).Length);
        }

        [Test]
        public void RewritingUnderTheSameName_IsNotTreatedAsACollision()
        {
            const string id = "11111111-2222-4333-8444-555555555501";

            var first = OWFolderWriter.WriteElement(_temp, Body(id, "character", "Alice"));
            var body = Body(id, "character", "Alice");
            body["description"] = "changed";
            var second = OWFolderWriter.WriteElement(_temp, body);

            Assert.AreEqual(first, second, "Rewriting an element in place keeps its path.");
            Assert.AreEqual(1, Directory.GetFiles(Path.Combine(_temp, "elements", "character")).Length);
            StringAssert.Contains("changed", File.ReadAllText(second));
        }

        [Test]
        public void SameNameSameTail_DifferentIds_DoNotDestroyEachOther()
        {
            // Two ids sharing their last 8 characters, with the same name: the short filename is
            // identical for both, and a writer that does not check destroys the first on the second
            // write -- silent element loss, which is the exact failure the format exists to prevent.
            const string a = "11111111-2222-4333-8444-5555deadbeef";
            const string b = "99999999-8888-4777-8666-5555deadbeef";

            var first = OWFolderWriter.WriteElement(_temp, Body(a, "character", "Twin"));
            var second = OWFolderWriter.WriteElement(_temp, Body(b, "character", "Twin"));

            Assert.AreNotEqual(first, second, "The second element must not land on the first's path.");
            Assert.IsTrue(File.Exists(first), "The first element must still be there.");
            Assert.IsTrue(File.Exists(second));

            StringAssert.Contains(b, Path.GetFileName(second),
                "The collision fallback puts the full id in the filename.");

            var read = OWFolderReader.Read(_temp, includeLegacySpatial: false);
            CollectionAssert.AreEquivalent(new[] { a, b }, read.Elements.Select(e => e.Id).ToList(),
                "Both elements must be readable back.");
        }

        [Test]
        public void Delete_RemovesTheFile_ByIdNotByName()
        {
            const string id = "11111111-2222-4333-8444-555555555501";
            var path = OWFolderWriter.WriteElement(_temp, Body(id, "character", "Alice"));

            Assert.IsTrue(OWFolderWriter.DeleteElement(_temp, id));
            Assert.IsFalse(File.Exists(path));

            Assert.IsFalse(OWFolderWriter.DeleteElement(_temp, id),
                "Deleting what is already gone reports false rather than throwing.");
        }

        [Test]
        public void Delete_FindsTheElement_EvenInADifferentTypeDirectory()
        {
            const string id = "11111111-2222-4333-8444-555555555501";
            OWFolderWriter.WriteElement(_temp, Body(id, "location", "Somewhere"));

            Assert.IsTrue(OWFolderWriter.DeleteElement(_temp, id),
                "An element's type may have changed since it was written. A delete that only looks "
                + "where it expects the file leaves an orphan the reader serves as live.");

            Assert.AreEqual(0, OWFolderReader.Read(_temp, includeLegacySpatial: false).Elements.Count);
        }

        // -- Refusals ---------------------------------------------------------

        [Test]
        public void ABodyWithNoId_Throws()
        {
            var body = new JObject { ["type"] = "character", ["name"] = "Nameless" };

            Assert.Throws<OWFolderFormatException>(() => OWFolderWriter.WriteElement(_temp, body),
                "Identity lives in the body, and a writer must never mint one.");
        }

        [Test]
        public void ABodyWithANullId_Throws()
        {
            var body = new JObject { ["type"] = "character", ["id"] = JValue.CreateNull() };

            Assert.Throws<OWFolderFormatException>(() => OWFolderWriter.WriteElement(_temp, body));
        }

        [Test]
        public void ABodyWithNoType_Throws()
        {
            var body = new JObject { ["id"] = "11111111-2222-4333-8444-555555555501", ["name"] = "Alice" };

            Assert.Throws<OWFolderFormatException>(() => OWFolderWriter.WriteElement(_temp, body),
                "A reader may infer type from the directory; a writer cannot run that backwards.");
        }

        [Test]
        public void AWorldWithNoId_Throws()
        {
            Assert.Throws<OWFolderFormatException>(
                () => OWFolderWriter.WriteWorld(_temp, new JObject { ["name"] = "Nameless" }));
        }

        // -- Layout and filenames ---------------------------------------------

        [Test]
        public void TypeDirectory_ComesFromTheBodysType()
        {
            OWFolderWriter.WriteElement(_temp, Body("11111111-2222-4333-8444-555555555501", "location", "Syracuse"));

            var expected = Path.Combine(_temp, "elements", "location");
            Assert.IsTrue(Directory.Exists(expected),
                "The reader maps elements/<type>/ to a type; the writer must place them the same way.");
            Assert.AreEqual(1, Directory.GetFiles(expected, "*.json").Length);

            var read = OWFolderReader.Read(_temp, includeLegacySpatial: false);
            Assert.AreEqual("location", read.Elements.Single().Type);
        }

        [Test]
        public void Filename_IsSlugAndTheLastEightCharactersOfTheId()
        {
            // The TRAILING characters: uuid7 fronts are timestamps and look alike, so a leading
            // fragment would collide for everything minted in the same second.
            Assert.AreEqual("alice--55555501",
                Path.GetFileNameWithoutExtension(
                    OWFolderWriter.ElementFilename("11111111-2222-4333-8444-555555555501", "Alice")));
        }

        [Test]
        public void Slug_IsCappedAtForty_AndNeverEndsInAHyphen()
        {
            var slug = OWFolderWriter.Slugify("Hippocrates' campaigns against the Chalcidian cities");

            Assert.AreEqual("hippocrates-campaigns-against-the-chalci", slug);
            Assert.AreEqual(OWFolderWriter.SlugMaxLength, slug.Length);
            Assert.IsFalse(slug.EndsWith("-", StringComparison.Ordinal),
                "A trailing hyphen would make the -- separator ambiguous.");
        }

        [Test]
        public void Slug_DropsDiacritics_RatherThanSeparatingOnThem()
        {
            Assert.AreEqual("jose", OWFolderWriter.Slugify("Jos\u00e9"), "Precomposed.");
            Assert.AreEqual("jose", OWFolderWriter.Slugify("Jose\u0301"),
                "Decomposed. Stripping marks AFTER the alphanumeric pass would give 'jose-'.");
        }

        [Test]
        public void AnUnsluggableName_FallsBackToTheFullId()
        {
            const string id = "11111111-2222-4333-8444-555555555501";

            Assert.AreEqual(id + ".json", OWFolderWriter.ElementFilename(id, ""));
            Assert.AreEqual(id + ".json", OWFolderWriter.ElementFilename(id, null));
            Assert.AreEqual(id + ".json", OWFolderWriter.ElementFilename(id, "\u4e16\u754c"),
                "A name with no ASCII alphanumerics yields no slug.");

            var written = OWFolderWriter.WriteElement(_temp, Body(id, "character", ""));
            Assert.AreEqual(id + ".json", Path.GetFileName(written));

            var read = OWFolderReader.Read(_temp, includeLegacySpatial: false);
            Assert.AreEqual("", read.Elements.Single().Body["name"].ToString(),
                "An empty-string name is a legal name, and must not be replaced with a placeholder.");
        }

        // -- What the reader gets back ----------------------------------------

        [Test]
        public void AWrittenFolder_ReadsBackThroughTheReader()
        {
            OWFolderWriter.WriteWorld(_temp, new JObject
            {
                ["id"] = "49501a68-179b-4a3c-8eaf-7f46f73d5fd2",
                ["name"] = "Testland",
                ["time_current"] = -492,
            });

            OWFolderWriter.Write(_temp, new[]
            {
                Body("11111111-2222-4333-8444-555555555501", "character", "Alice"),
                Body("11111111-2222-4333-8444-555555555502", "location", "Syracuse"),
                Body("11111111-2222-4333-8444-555555555503", "pin", "A pin"),
            });

            var read = OWFolderReader.Read(_temp, includeLegacySpatial: false);

            Assert.AreEqual("49501a68-179b-4a3c-8eaf-7f46f73d5fd2", read.WorldId);
            Assert.AreEqual("Testland", read.WorldName);
            Assert.AreEqual(3, read.Elements.Count);
            Assert.IsEmpty(read.Skipped, "Nothing the writer produced may be unreadable.");
            CollectionAssert.AreEquivalent(
                new[] { "character", "location", "pin" },
                read.Elements.Select(e => e.Type).ToList());
        }

        [Test]
        public void WriteWorld_ChangingTimeCurrent_LeavesEveryOtherKeyAlone()
        {
            RequireFixture();

            var world = OWFolderWriter.ReadJsonFile(Path.Combine(FixtureRoot, "world.json"));
            var before = OWFolderWriter.Serialize(world);

            world["time_current"] = -491;
            var after = File.ReadAllText(OWFolderWriter.WriteWorld(_temp, world), new UTF8Encoding(false));

            Assert.AreEqual(
                before.Replace("\"time_current\": -492", "\"time_current\": -491"), after,
                "Advancing the world clock must change exactly one line. A writer must not drop "
                + "keys the source had, nor reorder what it did not touch.");
        }
    }
}
