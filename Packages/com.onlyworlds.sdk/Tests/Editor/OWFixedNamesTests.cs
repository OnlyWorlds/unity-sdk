using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The three fixed names, <c>world.json</c>, <c>elements/</c> and <c>spatial/</c>, are read as written at every door
    /// (the folder format's §4.8, ruled 2026-11-25): a folder or a world file that holds one of them in another case, and
    /// not the name itself, is refused with the cause <c>name_case</c>, never read as an empty world. The reader, the
    /// in-memory read, the world file, the pack and the writer all ask the same question of one listing.
    /// </summary>
    /// <remarks>
    /// On NTFS and APFS the file system folds a name's case, so a reader that asks it for <c>elements</c> is served
    /// <c>Elements/</c>; on ext4 it is served nothing. These tests build the folder on disk and pass on both: the refusal
    /// comes from the listing, never from the lookup. <see cref="DiskFoldsCase"/> says which this disk is, in the output.
    /// </remarks>
    public class OWFixedNamesTests
    {
        private const string World = "{\"id\": \"w\", \"name\": \"W\"}";

        /// <summary>The cause, by its word in the shared list (written as the word, so the test reads today's code too).</summary>
        private const string NameCase = "name_case";

        private string _root;
        private string _scratch;

        [SetUp]
        public void SetUp()
        {
            var at = Path.Combine(Path.GetTempPath(), "ow-fixed-names-" + System.Guid.NewGuid().ToString("N"));
            _root = Path.Combine(at, "world");
            _scratch = Path.Combine(at, "out");
            Directory.CreateDirectory(_root);
            Directory.CreateDirectory(_scratch);
        }

        [TearDown]
        public void TearDown()
        {
            var at = Path.GetDirectoryName(_root);
            if (Directory.Exists(at)) Directory.Delete(at, true);
        }

        private void Write(string relative, string json)
        {
            var path = Path.Combine(_root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json);
        }

        /// <summary>Whether this disk serves <c>Elements/</c> to a lookup of <c>elements</c>, said in the test's output.</summary>
        private bool DiskFoldsCase()
        {
            var folds = Directory.Exists(Path.Combine(_root, "elements"));
            TestContext.WriteLine(folds
                ? "this disk folds case: the lookup of elements finds Elements/, so the refusal is the listing's"
                : "this disk keeps case: the lookup of elements finds nothing, so the refusal is the listing's");
            return folds;
        }

        private static void AssertNameCase(TestDelegate act, params string[] named)
        {
            var e = Assert.Throws<OWFolderFormatException>(act);
            Assert.AreEqual(NameCase, e.Reason, e.Message);
            foreach (var name in named) StringAssert.Contains(name, e.Message, "the refusal names both spellings");
        }

        /// <summary>An archive of one top folder holding these entries, built here byte for byte.</summary>
        private string Archive(string name, IDictionary<string, string> entries)
        {
            var path = Path.Combine(_scratch, name);
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                foreach (var entry in entries)
                {
                    using (var stream = zip.CreateEntry(entry.Key).Open())
                    {
                        var bytes = Encoding.UTF8.GetBytes(entry.Value);
                        stream.Write(bytes, 0, bytes.Length);
                    }
                }
            }

            return path;
        }

        // -- The folder reader ----------------------------------------------------------------------------------------

        [Test]
        public void Folder_ElementsInAnotherCase_IsRefusedNameCase_NeverReadAsEmpty()
        {
            Write("world.json", World);
            Write("Elements/character/a.json", "{\"id\": \"c-1\", \"name\": \"A\"}");
            DiskFoldsCase();

            AssertNameCase(() => OWFolderReader.Read(_root), "'Elements'", "elements");
        }

        [Test]
        public void Folder_WorldJsonInAnotherCase_IsRefusedNameCase()
        {
            Write("World.JSON", World);
            Write("elements/character/a.json", "{\"id\": \"c-1\", \"name\": \"A\"}");

            AssertNameCase(() => OWFolderReader.Read(_root), "'World.JSON'", "world.json");
        }

        [Test]
        public void Folder_SpatialInAnotherCase_IsRefused_EvenWhenTheLegacyLayoutIsNotRead()
        {
            Write("world.json", World);
            Write("Spatial/pin/p.json", "{\"id\": \"p-1\", \"name\": \"P\"}");

            AssertNameCase(() => OWFolderReader.Read(_root, includeLegacySpatial: false), "'Spatial'", "spatial");
            AssertNameCase(() => OWFolderReader.Read(_root), "'Spatial'");
        }

        [Test]
        public void Folder_TheFoldIsTheStandardsOne_ALongSIsAnS()
        {
            // U+017F folds to s under simple case folding (OW-18's fold), so this is spatial in another case.
            Write("world.json", World);
            Write("\u017Fpatial/pin/p.json", "{\"id\": \"p-1\", \"name\": \"P\"}");

            AssertNameCase(() => OWFolderReader.Read(_root), "\u017Fpatial");
        }

        [Test]
        public void Folder_TheFixedNamesAsWritten_ReadAsBefore()
        {
            Write("world.json", World);
            Write("elements/character/a.json", "{\"id\": \"c-1\", \"name\": \"A\"}");
            Write("spatial/pin/p.json", "{\"id\": \"p-1\", \"name\": \"P\"}");
            Write("Media/a.png", "not an image");

            var read = OWFolderReader.Read(_root);

            CollectionAssert.AreEqual(new[] { "c-1" }, read.Elements.Select(e => e.Id).ToArray());
            CollectionAssert.AreEqual(new[] { "p-1" }, read.LegacyElements.Select(e => e.Id).ToArray());
        }

        // -- The in-memory read -----------------------------------------------------------------------------------------

        [Test]
        public void Entries_ElementsInAnotherCase_IsRefusedNameCase()
        {
            AssertNameCase(() => OWFolderReader.ReadEntries(new Dictionary<string, string>
            {
                ["world.json"] = World,
                ["Elements/character/a.json"] = "{\"id\": \"c-1\"}",
            }), "'Elements'", "elements");
        }

        [Test]
        public void Entries_WorldJsonInAnotherCase_IsRefusedNameCase_NotNoWorld()
        {
            AssertNameCase(() => OWFolderReader.ReadEntries(new Dictionary<string, string>
            {
                ["World.JSON"] = World,
                ["elements/character/a.json"] = "{\"id\": \"c-1\"}",
            }), "'World.JSON'", "world.json");
        }

        [Test]
        public void Entries_BothSpellings_ReadTheNameAsWritten_AndLeaveTheOtherUnread()
        {
            // Only a case-sensitive disk holds the two side by side; the in-memory form stands in for it.
            var read = OWFolderReader.ReadEntries(new Dictionary<string, string>
            {
                ["world.json"] = World,
                ["elements/character/a.json"] = "{\"id\": \"c-1\"}",
                ["Elements/character/b.json"] = "{\"id\": \"c-2\"}",
            });

            CollectionAssert.AreEqual(new[] { "c-1" }, read.Elements.Select(e => e.Id).ToArray());
        }

        // -- The world file -------------------------------------------------------------------------------------------

        [Test]
        public void Archive_ElementsInAnotherCase_IsRefusedNameCase()
        {
            var archive = Archive("elements-case.owworld", new Dictionary<string, string>
            {
                ["abbey/world.json"] = World,
                ["abbey/Elements/character/a.json"] = "{\"id\": \"c-1\"}",
            });

            AssertNameCase(() => OWWorldArchive.Read(archive), "Elements");
        }

        [Test]
        public void Archive_WorldJsonInAnotherCase_IsRefusedNameCase_NotNotAWorld()
        {
            var archive = Archive("world-case.owworld", new Dictionary<string, string>
            {
                ["abbey/World.JSON"] = World,
                ["abbey/elements/character/a.json"] = "{\"id\": \"c-1\"}",
            });

            AssertNameCase(() => OWWorldArchive.Read(archive), "World.JSON");
        }

        [Test]
        public void Archive_WorldJsonInAnotherCaseAtTheRoot_IsRefusedNameCase()
        {
            var archive = Archive("root-case.owworld", new Dictionary<string, string>
            {
                ["WORLD.json"] = World,
                ["elements/character/a.json"] = "{\"id\": \"c-1\"}",
            });

            AssertNameCase(() => OWWorldArchive.Read(archive), "WORLD.json");
        }

        [Test]
        public void Archive_TheCheckIsFindWorldRootsToo()
        {
            var e = Assert.Throws<OWFolderFormatException>(() => OWWorldArchive.FindWorldRoot(new[] { "abbey/World.json", "abbey/elements/x/y.json" }));
            Assert.AreEqual(NameCase, e.Reason, e.Message);
            Assert.Throws<OWFolderFormatException>(() => OWWorldArchive.FindWorldRoot(new[] { "abbey/notes.txt" }), "no world at all is still no world");
            Assert.AreEqual(OWWorldArchiveReason.NotAWorld,
                Assert.Throws<OWFolderFormatException>(() => OWWorldArchive.FindWorldRoot(new[] { "abbey/notes.txt" })).Reason);
        }

        // -- The pack ---------------------------------------------------------------------------------------------------

        [Test]
        public void Pack_AFolderHoldingElementsInAnotherCase_IsRefused_NeverLaunderedOrEmptied()
        {
            Write("world.json", World);
            Write("Elements/character/a.json", "{\"id\": \"c-1\", \"name\": \"A\"}");
            var archive = Path.Combine(_scratch, "w.owworld");

            AssertNameCase(() => OWWorldArchive.Pack(_root, archive), "'Elements'", "elements");
            Assert.IsFalse(File.Exists(archive), "nothing is written");
        }

        [Test]
        public void Pack_SpatialInAnotherCaseBesideElements_IsRefused_InACopyAndInAShare_NothingWritten()
        {
            // The TypeScript SDK 4.212.3's vectors: Spatial/ (or a long s) beside elements/ was dropped from npm's copy and
            // emptied its share; a pack that lists the folder refuses both, and the names as written pack as before.
            var at = Path.GetDirectoryName(_root);
            var n = 0;
            string Folder(string spatial, string elements = "elements")
            {
                var root = Path.Combine(at, "case" + n++, "w");
                foreach (var (rel, json) in new[]
                         {
                             ("world.json", World),
                             (elements + "/character/ada--c1.json", "{\"id\": \"c-1\", \"name\": \"Ada\"}"),
                             (spatial == null ? null : spatial + "/pin/p--p1.json", "{\"id\": \"p-1\", \"name\": \"P\"}"),
                         })
                {
                    if (rel == null) continue;
                    var path = Path.Combine(root, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, json);
                }

                return root;
            }

            foreach (var includeHidden in new[] { true, false })
            {
                var options = new OWWorldArchivePackOptions { IncludeHidden = includeHidden };
                var mode = includeHidden ? "copy" : "share";
                foreach (var spatial in new[] { "Spatial", "ſpatial" })
                {
                    var archive = Path.Combine(_scratch, $"{mode}-{n}.owworld");
                    AssertNameCase(() => OWWorldArchive.Pack(Folder(spatial), archive, options), "'" + spatial + "'", "spatial");
                    Assert.IsFalse(File.Exists(archive), $"{mode}, {spatial}: nothing is written");
                }

                var alone = Path.Combine(_scratch, $"{mode}-elements.owworld");
                AssertNameCase(() => OWWorldArchive.Pack(Folder(null, "Elements"), alone, options), "'Elements'", "elements");
                Assert.IsFalse(File.Exists(alone), $"{mode}, Elements/ alone: nothing is written");

                // The control: the names as written, a share carrying the element and the legacy pin both.
                var control = Path.Combine(_scratch, $"{mode}-control.owworld");
                CollectionAssert.AreEqual(
                    new[] { "mimetype", "w/elements/character/ada--c1.json", "w/spatial/pin/p--p1.json", "w/world.json" },
                    OWWorldArchive.Pack(Folder("spatial"), control, options), mode);
                var back = OWWorldArchive.Read(control);
                Assert.AreEqual(1, back.Elements.Count, mode);
                Assert.AreEqual(1, back.LegacyElements.Count, mode);
            }
        }

        [Test]
        public void Pack_AFolderHoldingWorldJsonInAnotherCase_IsRefused()
        {
            Write("World.json", World);
            var archive = Path.Combine(_scratch, "w.owworld");

            AssertNameCase(() => OWWorldArchive.Pack(_root, archive), "'World.json'", "world.json");
            AssertNameCase(() => OWWorldArchive.DefaultArchiveName(_root), "'World.json'");
            Assert.IsFalse(File.Exists(archive), "nothing is written");
        }

        // -- The writer -------------------------------------------------------------------------------------------------

        [Test]
        public void Writer_IntoAFolderHoldingElementsInAnotherCase_IsRefused_NothingWritten()
        {
            Write("world.json", World);
            Write("Elements/character/a.json", "{\"id\": \"c-1\", \"name\": \"A\"}");
            var body = JObject.Parse("{\"id\": \"c-2\", \"type\": \"character\", \"name\": \"B\"}");

            AssertNameCase(() => OWFolderWriter.WriteElement(_root, body), "'Elements'");
            AssertNameCase(() => OWFolderWriter.Write(_root, new[] { body }), "'Elements'");
            CollectionAssert.AreEqual(new[] { "a.json" },
                Directory.GetFiles(Path.Combine(_root, "Elements", "character")).Select(Path.GetFileName).ToArray(),
                "nothing landed in Elements/ (on a disk that folds case, elements/ would)");
        }

        [Test]
        public void Writer_WorldJsonOverOneInAnotherCase_IsRefused_ItsBytesKept()
        {
            Write("World.JSON", World);
            var before = File.ReadAllBytes(Path.Combine(_root, "World.JSON"));

            AssertNameCase(() => OWFolderWriter.WriteWorld(_root, JObject.Parse("{\"id\": \"w\", \"name\": \"Renamed\"}")), "'World.JSON'");
            CollectionAssert.AreEqual(before, File.ReadAllBytes(Path.Combine(_root, "World.JSON")));
        }

        [Test]
        public void Writer_DeleteInAFolderHoldingElementsInAnotherCase_IsRefused_TheFileKept()
        {
            Write("world.json", World);
            Write("Elements/character/a.json", "{\"id\": \"c-1\", \"name\": \"A\"}");

            AssertNameCase(() => OWFolderWriter.DeleteElement(_root, "c-1"), "'Elements'");
            Assert.IsTrue(File.Exists(Path.Combine(_root, "Elements", "character", "a.json")));
        }

        // -- A type folder in another case, at the writer (ruled 2026-11-23: a type folder is read as written) --------

        [Test]
        public void Writer_IntoATypeFolderInAnotherCase_IsRefused_TheFolderNamed_NothingWritten()
        {
            Write("world.json", World);
            Write("elements/Character/old.json", "{\"id\": \"c-old\", \"name\": \"Old\"}");
            var body = JObject.Parse("{\"id\": \"c-new\", \"type\": \"character\", \"name\": \"New\"}");
            DiskFoldsCase();

            AssertNameCase(() => OWFolderWriter.WriteElement(_root, body), "Character", "c-new");
            AssertNameCase(() => OWFolderWriter.Write(_root, new[] { body }), "Character");
            var e = Assert.Throws<OWFolderFormatException>(() => OWFolderWriter.WriteElement(_root, body));
            StringAssert.Contains(Path.Combine(_root, "elements", "Character"), e.Message, "the folder is named by its path");
            CollectionAssert.AreEqual(new[] { "old.json" },
                Directory.GetFiles(Path.Combine(_root, "elements", "Character")).Select(Path.GetFileName).ToArray(),
                "nothing landed in Character/ (on a disk that folds case, character/ would)");
        }

        [Test]
        public void Writer_AnotherTypesFolderInAnotherCase_DoesNotStopThisWrite()
        {
            Write("world.json", World);
            Write("elements/Location/old.json", "{\"id\": \"l-old\", \"name\": \"Old\"}");

            OWFolderWriter.WriteElement(_root, JObject.Parse("{\"id\": \"c-new\", \"type\": \"character\", \"name\": \"New\"}"));

            CollectionAssert.AreEqual(new[] { "c-new" }, OWFolderReader.Read(_root).Elements.Select(x => x.Id).ToArray());
        }

        [Test]
        public void Writer_TheLegacyLayoutInAnotherCase_IsNotWhereTheElementLives()
        {
            // spatial/Pin/ is no pin folder, so the id it holds is no element the reader serves: the write goes to
            // elements/pin/, never into spatial/Pin/ (which a disk that folds case would give a lookup of spatial/pin).
            Write("world.json", World);
            Write("spatial/Pin/well.json", "{\"id\": \"p-1\", \"name\": \"Old\"}");
            var before = File.ReadAllBytes(Path.Combine(_root, "spatial", "Pin", "well.json"));

            var path = OWFolderWriter.WriteElement(_root, JObject.Parse("{\"id\": \"p-1\", \"type\": \"pin\", \"name\": \"Well\"}"));

            StringAssert.Contains(Path.Combine(_root, "elements", "pin"), path);
            var read = OWFolderReader.Read(_root);
            CollectionAssert.AreEqual(new[] { "p-1" }, read.Elements.Select(x => x.Id).ToArray());
            CollectionAssert.AreEqual(before, File.ReadAllBytes(Path.Combine(_root, "spatial", "Pin", "well.json")),
                "the unread file keeps its bytes");
        }

        [Test]
        public void Writer_IntoANewFolder_WritesAsBefore()
        {
            var fresh = Path.Combine(_root, "fresh");
            OWFolderWriter.WriteWorld(fresh, JObject.Parse(World));
            OWFolderWriter.WriteElement(fresh, JObject.Parse("{\"id\": \"c-1\", \"type\": \"character\", \"name\": \"A\"}"));

            CollectionAssert.AreEqual(new[] { "c-1" }, OWFolderReader.Read(fresh).Elements.Select(e => e.Id).ToArray());
        }

        // -- spatial/ holds the four, as written (OW-24) ----------------------------------------------------------------

        [Test]
        public void Spatial_AFolderOutsideTheFour_ServesNothing_AtEveryDoor()
        {
            Write("world.json", World);
            Write("elements/character/a.json", "{\"id\": \"c-1\", \"name\": \"A\"}");
            Write("spatial/character/cleric.json", "{\"id\": \"c-2\", \"name\": \"Cleric\"}");
            Write("spatial/Pin/well.json", "{\"id\": \"p-1\", \"name\": \"Well\"}");

            foreach (var read in new[] { OWFolderReader.Read(_root), PackedAndRead() })
            {
                CollectionAssert.AreEqual(new[] { "c-1" }, read.Elements.Select(e => e.Id).ToArray());
                Assert.AreEqual(0, read.LegacyElements.Count, "spatial/character/ holds no Character, spatial/Pin/ no Pin");
            }
        }

        private OWFolderReadResult PackedAndRead()
        {
            var archive = Path.Combine(_scratch, "spatial.owworld");
            OWWorldArchive.Pack(_root, archive);
            return OWWorldArchive.Read(archive);
        }
    }
}
