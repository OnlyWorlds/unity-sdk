using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// Three names a reader reads as written, at every door (the folder format's rulings of 2026-11-23): an element file's
    /// extension is <c>.json</c> in lower case, a type folder's name is the type word in lower case and one of the 22, and
    /// an id is compared as written, never as a UUID.
    /// </summary>
    /// <remarks>
    /// Each door is held: the folder on disk (<see cref="OWFolderReader.Read(string, bool)"/>), the same files in memory
    /// (<see cref="OWFolderReader.ReadEntries"/>), an <c>.owworld</c> (<see cref="OWWorldArchive.Read"/>), the cache a folder
    /// fills, the share, and the writer, which touches only what the reader serves. On Windows and macOS the file
    /// system folds a name's case; the reader must not, so a test that writes <c>X.JSON</c> fails there on a reader that
    /// lists by the file system's pattern.
    /// </remarks>
    public class OWFolderNamesTests
    {
        private const string World = "{\"id\": \"w\", \"name\": \"W\"}";
        private const string Upper = "0195D1A0-0000-7000-8000-00000000C0DE";
        private const string Lower = "0195d1a0-0000-7000-8000-00000000c0de";

        private string _root;
        private string _scratch;

        [SetUp]
        public void SetUp()
        {
            var at = Path.Combine(Path.GetTempPath(), "ow-names-" + System.Guid.NewGuid().ToString("N"));
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

        private static string Reason(OWFolderReadResult read, string pathEnd)
        {
            var hit = read.Skipped.Where(s => s.Path.Replace('\\', '/').EndsWith(pathEnd, System.StringComparison.Ordinal)).ToList();
            Assert.AreEqual(1, hit.Count, $"'{pathEnd}' is listed once as skipped; skipped: "
                                          + string.Join(", ", read.Skipped.Select(s => s.Path + " (" + s.Reason + ")")));
            return hit[0].Reason;
        }

        private OWFolderReadResult PackedAndRead()
        {
            var archive = Path.Combine(_scratch, "w.owworld");
            OWWorldArchive.Pack(_root, archive);
            return OWWorldArchive.Read(archive);
        }

        // -- 1. M.JSON is not an element file ---------------------------------------------------------------------

        private void ExtensionWorld()
        {
            Write("world.json", World);
            Write("elements/character/ok.json", "{\"id\": \"c-ok\", \"name\": \"Ok\"}");
            Write("elements/character/X.JSON", "{\"id\": \"c-upper\", \"name\": \"Upper\"}");
            Write("elements/character/y.Json", "{\"id\": \"c-mixed\", \"name\": \"Mixed\"}");
        }

        [Test]
        public void Extension_Folder_AnElementFileSpelledOtherwise_IsSkippedWithAReason()
        {
            ExtensionWorld();

            var read = OWFolderReader.Read(_root);

            CollectionAssert.AreEqual(new[] { "c-ok" }, read.Elements.Select(e => e.Id).ToArray(),
                "only the .json file is an element, whatever the file system folds");
            StringAssert.Contains("extension", Reason(read, "elements/character/X.JSON"));
            StringAssert.Contains("extension", Reason(read, "elements/character/y.Json"));
        }

        [Test]
        public void Extension_Entries_AnElementFileSpelledOtherwise_IsSkippedWithAReason()
        {
            var read = OWFolderReader.ReadEntries(new Dictionary<string, string>
            {
                ["world.json"] = World,
                ["elements/character/ok.json"] = "{\"id\": \"c-ok\", \"name\": \"Ok\"}",
                ["elements/character/X.JSON"] = "{\"id\": \"c-upper\", \"name\": \"Upper\"}",
                ["spatial/pin/P.JSON"] = "{\"id\": \"p-upper\", \"name\": \"Upper\"}",
            });

            CollectionAssert.AreEqual(new[] { "c-ok" }, read.Elements.Select(e => e.Id).ToArray());
            Assert.AreEqual(0, read.LegacyElements.Count);
            StringAssert.Contains("extension", Reason(read, "elements/character/X.JSON"));
            StringAssert.Contains("extension", Reason(read, "spatial/pin/P.JSON"));
        }

        [Test]
        public void Extension_Folder_TheLegacyLayout_ByTheSameRule()
        {
            Write("world.json", World);
            Write("spatial/pin/P.JSON", "{\"id\": \"p-upper\", \"name\": \"Upper\"}");

            var read = OWFolderReader.Read(_root);

            Assert.AreEqual(0, read.LegacyElements.Count);
            StringAssert.Contains("extension", Reason(read, "spatial/pin/P.JSON"));
        }

        [Test]
        public void Extension_Archive_AnElementFileSpelledOtherwise_IsSkippedWithAReason()
        {
            ExtensionWorld();

            var read = PackedAndRead();

            CollectionAssert.AreEqual(new[] { "c-ok" }, read.Elements.Select(e => e.Id).ToArray());
            StringAssert.Contains("extension", Reason(read, "elements/character/X.JSON"));
        }

        [Test]
        public void Extension_AFileOfAnotherExtension_IsStillNoElementFile_AndNotListed()
        {
            Write("world.json", World);
            Write("elements/character/notes.txt", "{\"id\": \"c-notes\"}");

            var read = OWFolderReader.Read(_root);

            Assert.AreEqual(0, read.Elements.Count);
            Assert.AreEqual(0, read.Skipped.Count, "a notes.txt is not an element file in any spelling, as before");
        }

        // -- 2. elements/Character/ is not a type folder ----------------------------------------------------------

        private void FolderWorld()
        {
            Write("world.json", World);
            Write("elements/location/l.json", "{\"id\": \"l-ok\", \"name\": \"Ok\"}");
            Write("elements/Character/c.json", "{\"id\": \"c-upper\", \"type\": \"character\", \"name\": \"Upper\"}");
            Write("elements/widget/w.json", "{\"id\": \"w-1\", \"name\": \"Widget\"}");
        }

        [Test]
        public void TypeFolder_Folder_AFolderThatIsNoType_IsNotRead_ItsFilesListedWithAReason()
        {
            FolderWorld();

            var read = OWFolderReader.Read(_root);

            CollectionAssert.AreEqual(new[] { "l-ok" }, read.Elements.Select(e => e.Id).ToArray(),
                "an upper-case folder and an unknown word are no type folders, whatever the body declares");
            StringAssert.Contains("no type", Reason(read, "elements/Character/c.json"));
            StringAssert.Contains("no type", Reason(read, "elements/widget/w.json"));
        }

        [Test]
        public void TypeFolder_Entries_AFolderThatIsNoType_IsNotRead_ItsFilesListedWithAReason()
        {
            var read = OWFolderReader.ReadEntries(new Dictionary<string, string>
            {
                ["world.json"] = World,
                ["elements/location/l.json"] = "{\"id\": \"l-ok\", \"name\": \"Ok\"}",
                ["elements/Character/c.json"] = "{\"id\": \"c-upper\", \"type\": \"character\", \"name\": \"Upper\"}",
                ["elements/widget/w.json"] = "{\"id\": \"w-1\", \"name\": \"Widget\"}",
                ["spatial/Pin/p.json"] = "{\"id\": \"p-upper\", \"name\": \"Upper\"}",
            });

            CollectionAssert.AreEqual(new[] { "l-ok" }, read.Elements.Select(e => e.Id).ToArray());
            Assert.AreEqual(0, read.LegacyElements.Count);
            StringAssert.Contains("no type", Reason(read, "elements/Character/c.json"));
            StringAssert.Contains("no type", Reason(read, "elements/widget/w.json"));
            StringAssert.Contains("no type", Reason(read, "spatial/Pin/p.json"));
        }

        [Test]
        public void TypeFolder_Folder_TheLegacyLayout_ByTheSameRule()
        {
            Write("world.json", World);
            Write("spatial/Pin/p.json", "{\"id\": \"p-upper\", \"name\": \"Upper\"}");

            var read = OWFolderReader.Read(_root);

            Assert.AreEqual(0, read.LegacyElements.Count, "spatial/Pin/ is not spatial/pin/, though NTFS opens one as the other");
            StringAssert.Contains("no type", Reason(read, "spatial/Pin/p.json"));
        }

        [Test]
        public void TypeFolder_Archive_AFolderThatIsNoType_IsNotRead_ItsFilesListedWithAReason()
        {
            FolderWorld();

            var read = PackedAndRead();

            CollectionAssert.AreEqual(new[] { "l-ok" }, read.Elements.Select(e => e.Id).ToArray());
            StringAssert.Contains("no type", Reason(read, "elements/Character/c.json"));
            StringAssert.Contains("no type", Reason(read, "elements/widget/w.json"));
        }

        [Test]
        public void TypeFolder_TheTwoFormsAgree_FileForFile()
        {
            FolderWorld();
            Write("elements/location/X.JSON", "{\"id\": \"l-x\"}");
            Write("spatial/Pin/p.json", "{\"id\": \"p-upper\"}");

            var folder = OWFolderReader.Read(_root);
            var entries = OWFolderReader.ReadEntries(Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
                .Select(f => new KeyValuePair<string, string>(f.Substring(_root.Length + 1).Replace('\\', '/'), File.ReadAllText(f))));

            Assert.AreEqual(4, folder.Skipped.Count, "Character/c.json, location/X.JSON, widget/w.json, spatial/Pin/p.json");
            string Rel(string p) => p.Replace('\\', '/').Substring(p.Replace('\\', '/').IndexOf("/world/", System.StringComparison.Ordinal) + 7);
            CollectionAssert.AreEqual(
                entries.Skipped.Select(s => s.Path + " | " + s.Reason).ToArray(),
                folder.Skipped.Select(s => Rel(s.Path) + " | " + s.Reason).ToArray(),
                "the folder and the in-memory form skip the same files, in the same order, for the same reasons");
        }

        // -- 3. An id is compared as written ----------------------------------------------------------------------

        private void TwoSpellingsWorld()
        {
            Write("world.json", World);
            Write("elements/character/a.json", $"{{\"id\": \"{Upper}\", \"name\": \"Upper\"}}");
            Write("elements/character/b.json", $"{{\"id\": \"{Lower}\", \"name\": \"Lower\"}}");
        }

        [Test]
        public void Id_Folder_OneUuidInTwoCases_IsTwoElements()
        {
            TwoSpellingsWorld();

            var read = OWFolderReader.Read(_root);

            CollectionAssert.AreEquivalent(new[] { Upper, Lower }, read.Elements.Select(e => e.Id).ToArray());
            Assert.AreEqual(0, read.Skipped.Count, "neither is a repeat of the other");
        }

        [Test]
        public void Id_Entries_AndArchive_OneUuidInTwoCases_IsTwoElements()
        {
            TwoSpellingsWorld();

            foreach (var read in new[] { OWFolderReader.ReadEntries(new Dictionary<string, string>
                     {
                         ["world.json"] = World,
                         ["elements/character/a.json"] = $"{{\"id\": \"{Upper}\"}}",
                         ["elements/character/b.json"] = $"{{\"id\": \"{Lower}\"}}",
                     }), PackedAndRead() })
            {
                CollectionAssert.AreEquivalent(new[] { Upper, Lower }, read.Elements.Select(e => e.Id).ToArray());
                Assert.AreEqual(0, read.Skipped.Count);
            }
        }

        [Test]
        public void Id_AcrossTheLayouts_OneUuidInTwoCases_IsTwoElements()
        {
            var read = OWFolderReader.ReadEntries(new Dictionary<string, string>
            {
                ["world.json"] = World,
                ["elements/pin/a.json"] = $"{{\"id\": \"{Upper}\"}}",
                ["spatial/pin/b.json"] = $"{{\"id\": \"{Lower}\"}}",
            });

            CollectionAssert.AreEqual(new[] { Upper }, read.Elements.Select(e => e.Id).ToArray());
            CollectionAssert.AreEqual(new[] { Lower }, read.LegacyElements.Select(e => e.Id).ToArray(),
                "the old layout's copy is another id, so it is not the one elements/ serves");
        }

        [Test]
        public void Id_TheCache_HoldsBothSpellings()
        {
            TwoSpellingsWorld();
            var cache = UnityEngine.ScriptableObject.CreateInstance<OWWorldCache>();
            try
            {
                OWFolderLoader.LoadInto(cache, _root);

                Assert.AreEqual(2, cache.Count);
                Assert.IsTrue(cache.Contains(Upper));
                Assert.IsTrue(cache.Contains(Lower));
                Assert.AreEqual("Upper", cache.Get<OWCharacter>(Upper).Name);
                Assert.AreEqual("Lower", cache.Get<OWCharacter>(Lower).Name);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cache);
            }
        }

        [Test]
        public void Id_AShare_CarriesBothSpellings_NeitherIsATwiceHeldId()
        {
            TwoSpellingsWorld();
            var archive = Path.Combine(_scratch, "share.owworld");
            var unread = new List<string>();
            var hidden = new List<string>();

            var written = OWWorldArchive.Pack(_root, archive, false, false, includeHidden: false, hidden, unread);

            CollectionAssert.IsEmpty(unread);
            CollectionAssert.IsEmpty(hidden);
            foreach (var file in new[] { "/elements/character/a.json", "/elements/character/b.json" })
                Assert.IsTrue(written.Any(w => w.Replace('\\', '/').EndsWith(file, System.StringComparison.Ordinal)), file + " travels");
            CollectionAssert.AreEquivalent(new[] { Upper, Lower }, OWWorldArchive.Read(archive).Elements.Select(e => e.Id).ToArray());
        }

        // -- The share and the writer follow the reader ------------------------------------------------------------

        /// <summary>
        /// The share already left these out (folder spec §4.8, 2026-11-23): held here so that it keeps following the
        /// reader. One folder per spelling, since on NTFS <c>Character/</c> and <c>character/</c> are one folder.
        /// </summary>
        [Test]
        public void Share_LeavesOutWhatTheReaderSkipsForItsName()
        {
            Write("world.json", World);
            Write("elements/location/ok.json", "{\"id\": \"l-ok\"}");
            Write("elements/location/X.JSON", "{\"id\": \"l-upper\"}");
            Write("elements/location/y.Json", "{\"id\": \"l-mixed\"}");
            Write("elements/Character/c.json", "{\"id\": \"c-upper\", \"type\": \"character\"}");
            Write("elements/widget/w.json", "{\"id\": \"w-1\"}");
            var archive = Path.Combine(_scratch, "share.owworld");
            var unread = new List<string>();

            OWWorldArchive.Pack(_root, archive, false, false, includeHidden: false, null, unread);

            CollectionAssert.AreEquivalent(
                new[] { "elements/Character/c.json", "elements/location/X.JSON", "elements/location/y.Json", "elements/widget/w.json" },
                unread);
        }

        [Test]
        public void Writer_NeverWritesIntoAFileTheReaderSkipsForItsExtension()
        {
            Write("world.json", World);
            var body = Newtonsoft.Json.Linq.JObject.Parse($"{{\"id\": \"{Lower}\", \"type\": \"character\", \"name\": \"Kira\"}}");
            var shortName = OWFolderWriter.ElementFilename(Lower, "Kira");
            var misspelt = Path.ChangeExtension(shortName, ".JSON");
            Write("elements/character/" + misspelt, $"{{\"id\": \"{Lower}\", \"name\": \"Old\"}}");
            var before = File.ReadAllBytes(Path.Combine(_root, "elements/character", misspelt));

            var path = OWFolderWriter.WriteElement(_root, body);

            StringAssert.EndsWith(".json", path);
            var read = OWFolderReader.Read(_root);
            CollectionAssert.AreEqual(new[] { Lower }, read.Elements.Select(e => e.Id).ToArray(), "the element written is served");
            Assert.AreEqual("Kira", (string)read.Elements[0].Body["name"]);
            var kept = Directory.GetFiles(Path.Combine(_root, "elements/character"))
                .Single(f => Path.GetFileName(f) == misspelt);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(kept), "the file the reader skips keeps its bytes and its name");
        }

        [Test]
        public void Writer_DeleteRemovesOnlyFilesTheReaderServes()
        {
            Write("world.json", World);
            Write("elements/character/kira.json", $"{{\"id\": \"{Lower}\", \"name\": \"Kira\"}}");
            Write("elements/character/kira-old.JSON", $"{{\"id\": \"{Lower}\", \"name\": \"Unread\"}}");
            Write("elements/widget/kira.json", $"{{\"id\": \"{Lower}\", \"name\": \"Unread\"}}");
            Write("elements/.atlas/kira.json", $"{{\"id\": \"{Lower}\", \"name\": \"Tool\"}}");

            Assert.IsTrue(OWFolderWriter.DeleteElement(_root, Lower));

            Assert.IsFalse(File.Exists(Path.Combine(_root, "elements/character/kira.json")), "the served file is removed");
            Assert.IsTrue(File.Exists(Path.Combine(_root, "elements/character/kira-old.JSON")), "a file of another spelling is not the element's");
            Assert.IsTrue(File.Exists(Path.Combine(_root, "elements/widget/kira.json")), "a folder that is no type is not the world's");
            Assert.IsTrue(File.Exists(Path.Combine(_root, "elements/.atlas/kira.json")), "a tool's folder is not the world's");
        }
    }
}
