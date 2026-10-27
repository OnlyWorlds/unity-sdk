using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// <see cref="OWFolderExport.ExportFileAsync"/>: the portal's one-file export written as a world folder. The whole-file
    /// case reads keel's own fixture (dist.25, see <see cref="OWEnvelopeReaderTests"/>); the refusals are inline.
    /// </summary>
    public class OWFolderExportFileTests
    {
        private string _parent;
        private string _folder;

        [SetUp]
        public void SetUp()
        {
            _parent = Path.Combine(Path.GetTempPath(), "ow-file-export-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_parent);
            _folder = Path.Combine(_parent, "world");
        }

        [TearDown]
        public void TearDown()
        {
            if (_parent != null && Directory.Exists(_parent)) Directory.Delete(_parent, recursive: true);
        }

        private static string Fixture
        {
            get
            {
                var root = TestPackage.Root();
                Assert.IsNotNull(root, "the package's own path is how the fixture is found");
                return Path.Combine(root, "Tests", "Editor", "Fixtures", "envelope", "world-export.json");
            }
        }

        private static OWFolderExportOptions NoMedia => new OWFolderExportOptions { IncludeMedia = false };

        /// <summary>An export file in the test's folder, its world and elements given as JSON text.</summary>
        private string ExportFile(string elements, string world = @"{""id"":""w1"",""name"":""W""}")
        {
            var path = Path.Combine(_parent, "export-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".json");
            File.WriteAllText(path,
                @"{""format"":""onlyworlds-world-export"",""world"":" + world + @",""elements"":" + elements + "}",
                new UTF8Encoding(false));
            return path;
        }

        /// <summary>Nothing was left behind: no folder, and no build directory beside it.</summary>
        private void AssertNothingWritten()
        {
            Assert.IsFalse(Directory.Exists(_folder), "a refused file leaves no folder");
            Assert.IsEmpty(Directory.GetDirectories(_parent), "and no build directory beside it");
        }

        // -- The whole file ----------------------------------------------------------------------------

        [Test]
        public async Task KeelsFixture_ReadsBackAsTheFile()
        {
            var report = await OWFolderExport.ExportFileAsync(Fixture, _folder, NoMedia);
            var file = OWEnvelopeReader.Read(Fixture);
            var folder = OWFolderReader.Read(_folder);

            Assert.AreEqual(file.WorldId, report.WorldId);
            Assert.AreEqual("Fixture World", report.WorldName);
            Assert.AreEqual(5, report.Total);
            Assert.AreEqual(file.WorldId, folder.WorldId);
            Assert.IsEmpty(folder.Skipped);
            Assert.AreEqual(file.Elements.Count, folder.Elements.Count);
            foreach (var element in file.Elements)
            {
                var written = folder.Elements.Single(e => e.Id == element.Id);
                Assert.AreEqual(element.Type, written.Type);
                Assert.IsTrue(JToken.DeepEquals(element.Body, written.Body), $"{element.Id}'s body is the file's, key for key");
            }

            Assert.AreEqual(OWFolderExport.FormatVersion, (string)folder.World["format_version"]);
        }

        [Test]
        public async Task TheFilesWorldKeys_RideAlong_ProvenanceIncluded()
        {
            var world = @"{""id"":""w1"",""name"":""W"",""created_at"":""2026-10-19T12:00:00+00:00"",
                ""source"":{""server"":""https://www.onlyworlds.com"",""world"":""w1"",""at"":""2026-10-19T21:00:00+00:00""},
                ""exported_as"":{""kind"":""character"",""role"":null,""character"":{""id"":""c1"",""name"":""Pip""}}}";
            await OWFolderExport.ExportFileAsync(ExportFile(@"{""character"":[{""id"":""c1"",""name"":""Pip""}]}", world), _folder, NoMedia);

            var written = OWFolderReader.Read(_folder).World;
            Assert.AreEqual("https://www.onlyworlds.com", (string)written["source"]["server"]);
            Assert.AreEqual("Pip", (string)written["exported_as"]["character"]["name"]);
            Assert.AreEqual(JTokenType.Null, written["exported_as"]["role"].Type, "a null is kept, never dropped");
            Assert.AreEqual("2026-10-19T12:00:00+00:00", (string)written["created_at"]);
        }

        [Test]
        public async Task TheFilesProducer_IsCarriedVerbatim_ItsMakerIsTheFoldersMaker()
        {
            // Folder spec §4.2 (v0.3.14): a tool writing a folder from keel's file keeps keel's line as it found it, key
            // order and an input word this SDK does not know included; it never writes its own over it.
            var world = @"{""id"":""w1"",""name"":""W"",
                ""source"":{""server"":""https://www.onlyworlds.com"",""world"":""w1"",""at"":""2026-10-22T05:00:00+00:00""},
                ""producer"":{""version"":""2026.10.22"",""tool"":""keel"",""input"":""papyrus-scroll""}}";
            await OWFolderExport.ExportFileAsync(ExportFile(@"{""character"":[{""id"":""c1"",""name"":""Pip""}]}", world), _folder, NoMedia);

            var text = File.ReadAllText(Path.Combine(_folder, "world.json"));
            StringAssert.Contains(
                "\"producer\": {\n    \"version\": \"2026.10.22\",\n    \"tool\": \"keel\",\n    \"input\": \"papyrus-scroll\"\n  }", text);
            var written = OWFolderReader.Read(_folder).World;
            CollectionAssert.AreEqual(new[] { "id", "name", "source", "producer", "format_version" }, written.Properties().Select(p => p.Name));
        }

        [Test]
        public async Task TheFilesOrigin_AndExtensionStore_AreCarriedVerbatim()
        {
            // Folder spec §4.2 (v0.3.16): keel's download writes origin, the world's first import, beside its own lines,
            // and the World's extension keys at the top level. The folder keeps both as the file has them.
            var world = @"{""id"":""w1"",""name"":""W"",
                ""origin"":{""source"":{""server"":""https://www.onlyworlds.com"",""world"":""w0"",""at"":""2026-10-18T09:00:00+00:00""},
                    ""producer"":{""tool"":""atlas"",""input"":""server"",""version"":""1.19.0""}},
                ""x_names"":{""types"":{""character"":{""one"":""Villager"",""many"":""Villagers""}},""version"":1},
                ""source"":{""server"":""https://www.onlyworlds.com"",""world"":""w1"",""at"":""2026-10-22T05:00:00+00:00""}}";
            await OWFolderExport.ExportFileAsync(ExportFile(@"{""character"":[{""id"":""c1"",""name"":""Pip""}]}", world), _folder, NoMedia);

            var written = OWFolderReader.Read(_folder).World;
            var given = OWFolderWriter.Parse(world);
            Assert.IsTrue(JToken.DeepEquals(given["origin"], written["origin"]), "the first import, as the file has it");
            CollectionAssert.AreEqual(new[] { "tool", "input", "version" }, ((JObject)written["origin"]["producer"]).Properties().Select(p => p.Name),
                "its key order too");
            Assert.IsTrue(JToken.DeepEquals(given["x_names"], written["x_names"]));
            CollectionAssert.AreEqual(new[] { "id", "name", "origin", "x_names", "source", "format_version" }, written.Properties().Select(p => p.Name));
        }

        [Test]
        public async Task AFileWithNoOrigin_GivesAFolderWithNone_WhateverItsOtherLines()
        {
            // A writer never derives one: keel's own source and producer describe the download, not the world's first import.
            var world = @"{""id"":""w1"",""name"":""W"",
                ""source"":{""server"":""https://www.onlyworlds.com"",""world"":""w1"",""at"":""2026-10-22T05:00:00+00:00""},
                ""producer"":{""tool"":""keel"",""version"":""2026.10.23"",""input"":""server""}}";
            await OWFolderExport.ExportFileAsync(ExportFile(@"{""character"":[{""id"":""c1"",""name"":""Pip""}]}", world), _folder, NoMedia);

            Assert.IsFalse(OWFolderReader.Read(_folder).World.ContainsKey("origin"));
        }

        [Test]
        public async Task AFileWithNoProducer_GivesAFolderWithNone()
        {
            // Absent is the honest value: this SDK copied the file, it did not make the world.
            await OWFolderExport.ExportFileAsync(ExportFile(@"{""character"":[{""id"":""c1"",""name"":""Pip""}]}"), _folder, NoMedia);

            Assert.IsFalse(OWFolderReader.Read(_folder).World.ContainsKey("producer"));
        }

        [Test]
        public async Task AMapsImage_IsFetchedFromItsUrl_AndKept()
        {
            var fetched = 0;
            var options = new OWFolderExportOptions
            {
                FetchMedia = (url, ct) => { fetched++; return Task.FromResult(new byte[] { 1, 2, 3 }); },
            };
            var report = await OWFolderExport.ExportFileAsync(
                ExportFile(@"{""map"":[{""id"":""m1"",""name"":""The Isle"",""image_url"":""https://files.example.test/isle.png""}]}"),
                _folder, options);

            Assert.AreEqual(1, fetched);
            Assert.IsEmpty(report.MediaProblems);
            Assert.IsTrue(File.Exists(Path.Combine(_folder, "media", "m1", "base-image", "isle.png")));
        }

        [Test]
        public async Task AnEmptyWorld_IsAFolderWithOnlyItsWorld()
        {
            var report = await OWFolderExport.ExportFileAsync(ExportFile("{}"), _folder, NoMedia);

            Assert.AreEqual(0, report.Total);
            Assert.IsTrue(File.Exists(Path.Combine(_folder, "world.json")));
            Assert.IsFalse(Directory.Exists(Path.Combine(_folder, "elements")));
        }

        // -- What refuses, and leaves nothing --------------------------------------------------------

        [Test]
        public void AnElementWithNoId_IsRefused()
        {
            var path = ExportFile(@"{""character"":[{""id"":""c1"",""name"":""A""},{""name"":""Nobody""}]}");

            var ex = Assert.ThrowsAsync<OWFolderFormatException>(() => OWFolderExport.ExportFileAsync(path, _folder, NoMedia));
            StringAssert.Contains("#elements/character/1", ex.Message);
            AssertNothingWritten();
        }

        [Test]
        public void ATypeThatIsNotTheStandards_IsRefused()
        {
            var path = ExportFile(@"{""spaceship"":[{""id"":""s1"",""name"":""Hyperion""}]}");

            var ex = Assert.ThrowsAsync<OWFolderFormatException>(() => OWFolderExport.ExportFileAsync(path, _folder, NoMedia));
            StringAssert.Contains("'spaceship'", ex.Message);
            AssertNothingWritten();
        }

        [Test]
        public void OneIdUnderTwoTypes_IsRefused()
        {
            var path = ExportFile(@"{""character"":[{""id"":""x1"",""name"":""A""}],""creature"":[{""id"":""x1"",""name"":""B""}]}");

            Assert.ThrowsAsync<OWFolderFormatException>(() => OWFolderExport.ExportFileAsync(path, _folder, NoMedia));
            AssertNothingWritten();
        }

        [Test]
        public void AFileThatIsNoExport_IsRefused()
        {
            var path = Path.Combine(_parent, "world.json");
            File.WriteAllText(path, @"{""id"":""w1"",""name"":""W""}");

            var ex = Assert.ThrowsAsync<OWFolderFormatException>(() => OWFolderExport.ExportFileAsync(path, _folder, NoMedia));
            StringAssert.Contains("not an OnlyWorlds world export", ex.Message);
            Assert.IsFalse(Directory.Exists(_folder));
        }

        [Test]
        public void NoFile_IsFileNotFound()
        {
            Assert.ThrowsAsync<FileNotFoundException>(() =>
                OWFolderExport.ExportFileAsync(Path.Combine(_parent, "no-such.json"), _folder, NoMedia));
            AssertNothingWritten();
        }

        [Test]
        public void AnOccupiedFolder_IsRefused_BeforeTheFileIsRead()
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllText(Path.Combine(_folder, "keep.txt"), "mine");

            // The file does not exist: the folder's refusal comes first, so the IOException is the folder's.
            var ex = Assert.ThrowsAsync<IOException>(() =>
                OWFolderExport.ExportFileAsync(Path.Combine(_parent, "no-such.json"), _folder, NoMedia));
            Assert.IsNotInstanceOf<FileNotFoundException>(ex);
            Assert.AreEqual("mine", File.ReadAllText(Path.Combine(_folder, "keep.txt")));
        }
    }
}
