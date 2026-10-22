using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The <c>.owworld</c> archive: packing a world folder into one file, and reading it back as the folder it was.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The contract is two-sided. Pack must never touch the folder it packs (another tool owns it) and must give
    /// the same bytes for the same world (an archive that changes on every pack is noise in version control and a
    /// needless re-import). Read must give exactly what a folder read gives, refuse what is not one world, and key
    /// the world by a path every clone shares, never by this machine's.
    /// </para>
    /// <para>
    /// Two worlds are used: the package's own <c>folder-write</c> fixture (eight real Sikelia files, with the Unity
    /// <c>.meta</c> files a package carries beside them) and an Abbey-shaped world generated per test, whose
    /// elements live mostly in the dated <c>spatial/</c> layout and which carries three files the reader skips.
    /// </para>
    /// </remarks>
    public class OWWorldArchiveTests
    {
        private string _temp;

        [SetUp]
        public void SetUp()
        {
            _temp = Path.Combine(Path.GetTempPath(), "ow-archive-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temp);
        }

        [TearDown]
        public void TearDown()
        {
            if (_temp != null && Directory.Exists(_temp)) Directory.Delete(_temp, recursive: true);
        }

        // -- Fixtures -----------------------------------------------------------

        /// <summary>The package's committed folder fixture, found as <c>OWFolderWriterTests</c> finds it.</summary>
        /// <remarks>
        /// The package lookup crosses into the Editor's native side, so it is tried and dropped when it fails; the
        /// walk of the host's <c>Packages/</c> then finds it (and outside the Editor, from the project's path).
        /// </remarks>
        private static string FixtureRoot
        {
            get
            {
                if (_fixtureRoot != null) return _fixtureRoot.Length == 0 ? null : _fixtureRoot;
                _fixtureRoot = string.Empty;

                try
                {
                    var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(OWWorldArchiveTests).Assembly);
                    if (package != null)
                    {
                        var own = Path.Combine(package.resolvedPath, "Tests", "Editor", "Fixtures", "folder-write");
                        if (File.Exists(Path.Combine(own, "world.json"))) return _fixtureRoot = own;
                    }
                }
                catch (Exception) { }

                string projectRoot = null;
                try { projectRoot = Directory.GetParent(UnityEngine.Application.dataPath)?.FullName; }
                catch (Exception) { }
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

        private static string RequireFixture()
        {
            var root = FixtureRoot;
            if (root != null) return root;

            Assert.Ignore("Folder fixture not found. These assertions did not run -- treat them as unmeasured, not as passing.");
            return null;
        }

        private const string AbbeyWorldId = "0695db52-0000-7000-8000-00000000abbe";

        /// <summary>
        /// An Abbey-shaped world: one element under <c>elements/</c>, the rest only under <c>spatial/</c>, a pin
        /// that spells its link <c>map_id</c>, and three files the reader skips (an id in both layouts, a file with
        /// no id, a truncated file).
        /// </summary>
        private string MakeAbbey(string name = "abbey")
        {
            var root = Path.Combine(_temp, name);
            Write(root, "world.json", "{\n  \"id\": \"" + AbbeyWorldId + "\",\n  \"name\": \"Watabou's Abbey\"\n}\n");
            Write(root, "elements/location/abbey--0000ab01.json",
                "{\n  \"id\": \"0695db83-0000-7000-8000-00000000ab01\",\n  \"name\": \"Abbey\"\n}\n");
            Write(root, "spatial/map/abbey-plan--0000ab02.json",
                "{\n  \"id\": \"0695db83-0000-7000-8000-00000000ab02\",\n  \"name\": \"Abbey plan\",\n  \"width\": 1200\n}\n");
            Write(root, "spatial/pin/abbey-pin--0000ab03.json",
                "{\n  \"id\": \"0695db83-0000-7000-8000-00000000ab03\",\n  \"name\": \"Abbey pin\",\n"
                + "  \"map_id\": \"0695db83-0000-7000-8000-00000000ab02\",\n  \"x\": 10,\n  \"y\": 20\n}\n");
            Write(root, "spatial/zone/cloister--0000ab04.json",
                "{\n  \"id\": \"0695db83-0000-7000-8000-00000000ab04\",\n  \"name\": \"Cloister\",\n"
                + "  \"map_id\": \"0695db83-0000-7000-8000-00000000ab02\"\n}\n");
            Write(root, "spatial/marker/well--0000ab05.json",
                "{\n  \"id\": \"0695db83-0000-7000-8000-00000000ab05\",\n  \"name\": \"Well\",\n"
                + "  \"zone_id\": \"0695db83-0000-7000-8000-00000000ab04\"\n}\n");
            // Skipped: the same id as elements/ holds, no id, truncated.
            Write(root, "spatial/map/abbey-dup--0000ab01.json",
                "{\n  \"id\": \"0695db83-0000-7000-8000-00000000ab01\",\n  \"name\": \"Abbey (old copy)\"\n}\n");
            Write(root, "spatial/pin/no-id.json", "{\n  \"name\": \"orphan\"\n}\n");
            Write(root, "spatial/zone/torn.json", "{\n  \"id\": \"0695db83-0000-7000-8000-00000000ab");
            return root;
        }

        private static void Write(string root, string relative, string text)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        /// <summary>A zip with exactly these entries, for the refusals Pack would never write.</summary>
        private string MakeZip(string fileName, params (string name, string text)[] entries)
        {
            var path = Path.Combine(_temp, fileName);
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                foreach (var (name, text) in entries)
                {
                    var entry = zip.CreateEntry(name);
                    using (var w = new StreamWriter(entry.Open(), new UTF8Encoding(false))) w.Write(text);
                }
            }

            return path;
        }

        private static string WorldJson(string id, string name) => "{\n  \"id\": \"" + id + "\",\n  \"name\": \"" + name + "\"\n}\n";

        private const string ElementBody = "{\n  \"id\": \"0695db83-0000-7000-8000-0000000000e1\",\n  \"name\": \"One\"\n}\n";

        /// <summary>A <c>.meta</c> as Unity writes one beside a file it imports: the version, then the GUID.</summary>
        private const string UnityMeta =
            "fileFormatVersion: 2\nguid: 3f9d2c4b8a1e4f6d9b0c7a5e2d1f8c3b\nDefaultImporter:\n  externalObjects: {}\n"
            + "  userData: \n  assetBundleName: \n  assetBundleVariant: \n";

        private static List<string> EntryNames(string archive)
        {
            using (var zip = ZipFile.OpenRead(archive)) return zip.Entries.Select(e => e.FullName).ToList();
        }

        /// <summary>Every file and directory under a root, with its size and mtime: what "untouched" means.</summary>
        private static List<string> Snapshot(string root)
        {
            var lines = new List<string>
            {
                "<root> " + Directory.GetLastWriteTimeUtc(root).Ticks,
            };

            foreach (var dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories))
                lines.Add("d " + dir.Substring(root.Length) + " " + Directory.GetLastWriteTimeUtc(dir).Ticks);

            foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                var info = new FileInfo(file);
                lines.Add("f " + file.Substring(root.Length) + " " + info.Length + " " + info.LastWriteTimeUtc.Ticks);
            }

            lines.Sort(StringComparer.Ordinal);
            return lines;
        }

        private static void CopyDirectory(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
            foreach (var dir in Directory.GetDirectories(from)) CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
        }

        /// <summary>A read, reduced to what must agree: ids, types, bodies, skipped files, all world-relative.</summary>
        private static List<string> Digest(OWFolderReadResult read, string folderRoot)
        {
            var prefix = folderRoot == null ? null : Path.GetFullPath(folderRoot).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            string Rel(string p) => prefix != null && p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? p.Substring(prefix.Length).Replace('\\', '/')
                : p;

            var lines = new List<string> { "world " + read.World.ToString(Formatting.None) };
            lines.AddRange(read.Elements.Select(e =>
                $"element {e.Id} {e.Type} {e.TypeInferred} {Rel(e.Path)} {e.Body.ToString(Formatting.None)}"));
            lines.AddRange(read.LegacyElements.Select(e =>
                $"legacy {e.Id} {e.Type} {e.TypeInferred} {Rel(e.Path)} {e.Body.ToString(Formatting.None)}"));
            lines.AddRange(read.Skipped.Select(s => $"skipped {Rel(s.Path)} {s.Reason}"));
            lines.Sort(StringComparer.Ordinal);
            return lines;
        }

        /// <summary>A cache, reduced to everything a consumer can see of it.</summary>
        private static List<string> Digest(OWWorldCache cache)
        {
            var lines = new List<string>
            {
                $"key {cache.Key.Source} {cache.Key.WorldId} {cache.Key.Origin}",
                $"name {cache.WorldName}",
                $"readOnly {cache.ReadOnly}",
                $"count {cache.Count}",
            };

            foreach (var slug in OWElementTypes.Slugs.Concat(new[] { "map", "pin", "zone", "marker" }).Distinct())
            {
                var i = 0;
                foreach (var raw in cache.AllRaw(slug)) lines.Add($"{slug} {i++} {raw}");
            }

            return lines;
        }

        // -- Pack never writes into the folder --------------------------------------

        [Test]
        public void Pack_NeverWritesIntoTheSourceFolder_TheFixture()
        {
            var root = RequireFixture();
            var before = Snapshot(root);

            OWWorldArchive.Pack(root, Path.Combine(_temp, "fixture.owworld"));

            CollectionAssert.AreEqual(before, Snapshot(root),
                "Pack changed the folder it packed: a file, a .meta, a size or an mtime differs.");
        }

        [Test]
        public void Pack_NeverWritesIntoTheSourceFolder_AnAbbeyWithToolFolders()
        {
            var root = MakeAbbey();
            Write(root, ".atlas/state.json", "{}\n");
            var before = Snapshot(root);

            OWWorldArchive.Pack(root, Path.Combine(_temp, "abbey.owworld"));
            OWWorldArchive.Pack(root, Path.Combine(_temp, "abbey.owworld"));

            CollectionAssert.AreEqual(before, Snapshot(root));
            Assert.IsFalse(Directory.GetFiles(root, "*.meta", SearchOption.AllDirectories).Any(), "No .meta in the world.");
        }

        [Test]
        public void Pack_RefusesAnArchiveInsideTheFolder()
        {
            var root = MakeAbbey();
            var before = Snapshot(root);

            Assert.Throws<ArgumentException>(() => OWWorldArchive.Pack(root, Path.Combine(root, "abbey.owworld")));
            Assert.Throws<ArgumentException>(() => OWWorldArchive.Pack(root, Path.Combine(root, "media", "abbey.owworld")));

            CollectionAssert.AreEqual(before, Snapshot(root), "A refused pack wrote nothing.");
        }

        // -- Pack skips tool state ----------------------------------------------

        [Test]
        public void Pack_SkipsToolStateAndNonWorldContent_ButCarriesRecordsWhole()
        {
            var root = Path.Combine(_temp, "w");
            Write(root, "world.json", WorldJson("0695db52-0000-7000-8000-0000000000aa", "W"));
            Write(root, "elements/character/one--000000e1.json", ElementBody);
            Write(root, "elements/character/one--000000e1.json.meta", "fileFormatVersion: 2\n");
            Write(root, "elements/character/.draft.json", "{}\n");
            Write(root, "elements/.DS_Store", "x");
            Write(root, "elements/.hidden/x.json", "{}\n");
            Write(root, "media/0000e1/base-image/0000e1.meta.json", "{}\n");
            Write(root, "media/0000e1/base-image/0000e1.png", "png");
            Write(root, ".atlas/state.json", "{}\n");
            Write(root, ".git/HEAD", "ref: refs/heads/main\n");
            Write(root, ".ow/cursor", "1\n");
            Write(root, ".gitignore", ".*/\n");
            Write(root, "README.md", "# w\n");
            Write(root, "records/r.json", "{}\n");
            Write(root, "records/r.json.meta", UnityMeta);
            Write(root, "records/lab/.cursor", "7\n");
            Write(root, "records/lab/ticks/1.json", "{\"entries\":[]}\r\n");
            Write(root, "records/lab/plan.meta", "a producer's own file, no sibling\n");
            Write(root, "world.json.meta", "fileFormatVersion: 2\n");

            var entries = OWWorldArchive.Pack(root, Path.Combine(_temp, "w.owworld"));

            // records/ goes in whole (folder spec §4.5 rules 1 and 6), a producer's dot-file and its own .meta
            // included; only Unity's .meta beside a file is left out.
            var expected = new List<string>
            {
                "mimetype",
                "w/elements/character/one--000000e1.json",
                "w/media/0000e1/base-image/0000e1.meta.json",
                "w/media/0000e1/base-image/0000e1.png",
                "w/records/lab/.cursor",
                "w/records/lab/plan.meta",
                "w/records/lab/ticks/1.json",
                "w/records/r.json",
                "w/world.json",
            };
            CollectionAssert.AreEqual(expected, entries);
            CollectionAssert.AreEqual(expected, EntryNames(Path.Combine(_temp, "w.owworld")), "The file holds what Pack said.");
        }

        // -- Pack is deterministic ------------------------------------------------

        [Test]
        public void Pack_IsDeterministic_SameFolderSameBytes_WhateverTheMtimes()
        {
            var root = RequireFixture();
            var copy = Path.Combine(_temp, "copy");
            CopyDirectory(root, copy);

            var a = Path.Combine(_temp, "a.owworld");
            var b = Path.Combine(_temp, "b.owworld");

            foreach (var f in Directory.GetFiles(copy, "*", SearchOption.AllDirectories))
                File.SetLastWriteTimeUtc(f, new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc));
            OWWorldArchive.Pack(copy, a);

            foreach (var f in Directory.GetFiles(copy, "*", SearchOption.AllDirectories))
                File.SetLastWriteTimeUtc(f, new DateTime(2024, 6, 7, 8, 9, 10, DateTimeKind.Utc));
            OWWorldArchive.Pack(copy, b);

            CollectionAssert.AreEqual(File.ReadAllBytes(a), File.ReadAllBytes(b), "The same folder packed to different bytes.");

            // DOS time has a 2-second grain, so two packs of "now" can agree by luck: check the stamp itself.
            using (var zip = ZipFile.OpenRead(a))
            {
                foreach (var entry in zip.Entries)
                    Assert.AreEqual(OWWorldArchive.EntryTime.DateTime, entry.LastWriteTime.DateTime, entry.FullName);
            }

            TestContext.WriteLine($"fixture packs to {new FileInfo(a).Length} bytes, {EntryNames(a).Count} entries");
        }

        [Test]
        public void Pack_WritesEntriesInOrdinalOrder_NotTheFileSystemsOrder()
        {
            var root = Path.Combine(_temp, "w");
            Write(root, "world.json", WorldJson("0695db52-0000-7000-8000-0000000000aa", "W"));
            // NTFS lists case-insensitively (a, B, _c); ordinal is B, _c, a. And world.json, visited first, sorts last.
            foreach (var (name, tail) in new[] { ("a", "0a"), ("B", "0b"), ("_c", "0c") })
                Write(root, $"elements/character/{name}.json", "{\n  \"id\": \"0695db83-0000-7000-8000-0000000000" + tail + "\"\n}\n");

            var entries = OWWorldArchive.Pack(root, Path.Combine(_temp, "w.owworld"));

            var sorted = entries.Skip(1).OrderBy(e => e, StringComparer.Ordinal).ToList();
            CollectionAssert.AreEqual(sorted, entries.Skip(1).ToList(), "After the marker, the entries go in ordinal order.");
            CollectionAssert.AreEqual(new[]
            {
                "mimetype", "w/elements/character/B.json", "w/elements/character/_c.json", "w/elements/character/a.json", "w/world.json",
            }, entries);
        }

        [Test]
        public void Pack_WritesEntriesInUtf8ByteOrder_NotUtf16Order()
        {
            // Folder spec §4.7 (OW-13): the order is the UTF-8 bytes of the name, code point order. UTF-16 order
            // (string.CompareOrdinal, JS <) puts an emoji's surrogates (D83D...) before U+E000 to U+FFFF.
            var root = Path.Combine(_temp, "w");
            Write(root, "world.json", WorldJson("0695db52-0000-7000-8000-0000000000aa", "W"));
            foreach (var name in new[] { "\U0001F600.png", "Ａ.png", ".png", "z.png", "é.png", "\U00010400.png" })
                Write(root, "media/" + name, "png");

            var archive = Path.Combine(_temp, "w.owworld");
            var entries = OWWorldArchive.Pack(root, archive);

            CollectionAssert.AreEqual(new[]
            {
                "mimetype", "w/media/z.png", "w/media/é.png", "w/media/.png", "w/media/Ａ.png",
                "w/media/\U00010400.png", "w/media/\U0001F600.png", "w/world.json",
            }, entries);

            var bytes = entries.Skip(1).Select(e => Encoding.UTF8.GetBytes(e)).ToList();
            for (var i = 1; i < bytes.Count; i++)
                Assert.Less(CompareBytes(bytes[i - 1], bytes[i]), 0, $"{entries[i]} before {entries[i + 1]}");
            Assert.AreNotEqual(entries.Skip(1).OrderBy(e => e, StringComparer.Ordinal).ToList(), entries.Skip(1).ToList(),
                "UTF-16 order would give another order here, so this test tells the two apart.");

            CollectionAssert.AreEqual(entries, EntryNames(archive), "The file holds what Pack said, in that order.");
            Assert.AreEqual(0, OWWorldArchive.Read(archive).Elements.Count, "The names read back (the UTF-8 flag is set).");
        }

        private static int CompareBytes(byte[] a, byte[] b)
        {
            for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
                if (a[i] != b[i]) return a[i] - b[i];
            return a.Length - b.Length;
        }

        // -- The marker, and the zip Pack writes -------------------------------------

        [Test]
        public void Pack_WritesTheMarkerFirst_StoredBare_AndTheReaderOpensIt()
        {
            // Folder spec §4.7 (OW-6): a writer SHOULD place a first entry named mimetype, stored (method 0), no extra
            // field, its bytes exactly the media type (ASCII, no newline): the EPUB shape.
            var archive = PackTo(MakeAbbey(), "abbey.owworld");
            var raw = File.ReadAllBytes(archive);
            var marker = Encoding.ASCII.GetBytes(OWWorldArchive.MediaType);

            Assert.AreEqual(0x04034b50u, BitConverter.ToUInt32(raw, 0), "The file opens on a local header.");
            Assert.AreEqual(0, BitConverter.ToUInt16(raw, 8), "The marker is stored (method 0).");
            Assert.AreEqual(marker.Length, BitConverter.ToUInt32(raw, 18), "Its compressed size is its size.");
            Assert.AreEqual(marker.Length, BitConverter.ToUInt32(raw, 22));
            Assert.AreEqual(0, BitConverter.ToUInt16(raw, 28), "No extra field in its local header.");
            Assert.AreEqual("mimetype", Encoding.ASCII.GetString(raw, 30, BitConverter.ToUInt16(raw, 26)));
            CollectionAssert.AreEqual(marker, raw.Skip(30 + 8).Take(marker.Length).ToArray(),
                "The media type's bytes follow the name, as they are.");

            var first = CentralRecords(raw)[0];
            Assert.AreEqual(("mimetype", 0, 0, 0L), (first.name, first.method, first.extra, first.offset),
                "The central directory's first record is the marker: stored, no extra field, at offset 0.");

            Assert.AreEqual(1, OWWorldArchive.Read(archive).Elements.Count, "The reader opens the file it wrote.");
        }

        [Test]
        public void Pack_DeflatesEveryOtherEntry_StoresAnEmptyOne_AndFlagsOnlyANonAsciiName()
        {
            var root = Path.Combine(_temp, "w");
            Write(root, "world.json", WorldJson("0695db52-0000-7000-8000-0000000000aa", "W"));
            Write(root, "elements/character/one--000000e1.json", ElementBody);
            Write(root, "media/é.png", "png");
            Write(root, "records/empty", "");

            var archive = PackTo(root, "w.owworld");
            var raw = File.ReadAllBytes(archive);
            var records = CentralRecords(raw).ToDictionary(r => r.name);
            Assert.AreNotEqual(0x07064b50u, BitConverter.ToUInt32(raw, raw.Length - 22 - 20), "A small world is a classic zip: no zip64 locator.");

            Assert.AreEqual(8, records["w/elements/character/one--000000e1.json"].method, "A file is deflated.");
            Assert.AreEqual(8, records["w/world.json"].method);
            Assert.AreEqual(0, records["w/records/empty"].method, "An empty file is stored.");
            Assert.AreEqual(1 << 11, records["w/media/é.png"].flags, "A non-ASCII name carries the UTF-8 flag.");
            Assert.AreEqual(0, records["w/world.json"].flags, "An ASCII name carries no flag.");
            Assert.IsTrue(records.Values.All(r => r.extra == 0), "No extra field below zip64's limits.");

            using (var zip = ZipFile.OpenRead(archive))
            {
                Assert.AreEqual(ElementBody, new StreamReader(zip.GetEntry("w/elements/character/one--000000e1.json").Open()).ReadToEnd(),
                    "Another zip reader inflates what Pack deflated.");
                Assert.AreEqual(0, zip.GetEntry("w/records/empty").Length);
            }

            Assert.AreEqual(1, OWWorldArchive.Read(archive).Elements.Count, "The reader checks every CRC-32 and size, and opens it.");
        }

        [Test]
        public void PacksWriter_WritesZip64_From65535Entries()
        {
            // Folder spec §4.7 (OW-15): a writer SHOULD write a classic zip under 65,535 entries and zip64 above. At
            // 65,535 the classic end record's count is full, so the zip64 end record carries it. Pack's own writer,
            // fed in memory: the same entries packed from disk cost 80 s on NTFS, nearly all of it making the files.
            const int entries = 0xFFFF;
            var archive = Path.Combine(_temp, "w.owworld");
            using (var stream = new FileStream(archive, FileMode.Create))
            {
                var writer = new OWWorldArchive.ZipWriter(stream);
                writer.Add("mimetype", Encoding.ASCII.GetBytes(OWWorldArchive.MediaType), deflate: false);
                writer.Add("w/world.json", Encoding.UTF8.GetBytes(WorldJson("0695db52-0000-7000-8000-0000000000aa", "W")), deflate: true);
                for (var i = 0; i < entries - 2; i++) writer.Add("w/records/" + i.ToString("x5"), Array.Empty<byte>(), deflate: true);
                writer.Finish();
            }

            var raw = File.ReadAllBytes(archive);

            var end = raw.Length - 22;
            Assert.AreEqual(0x06054b50u, BitConverter.ToUInt32(raw, end));
            Assert.AreEqual(0xFFFF, BitConverter.ToUInt16(raw, end + 10), "The classic count says: read zip64's.");
            var locator = end - 20;
            Assert.AreEqual(0x07064b50u, BitConverter.ToUInt32(raw, locator), "The zip64 locator sits before the end record.");
            var zip64 = (int)BitConverter.ToInt64(raw, locator + 8);
            Assert.AreEqual(0x06064b50u, BitConverter.ToUInt32(raw, zip64), "The locator points at the zip64 end record.");
            Assert.AreEqual(entries, BitConverter.ToInt64(raw, zip64 + 32), "The zip64 end record holds the count.");

            using (var zip = ZipFile.OpenRead(archive)) Assert.AreEqual(entries, zip.Entries.Count, "Another zip reader counts them all.");
            // The SDK's reader on zip64 is the conformance set's (pass-OW-15-zip64), on ten files, not 65,535.
        }

        /// <summary>The central directory's records, in order, read from a zip with no comment and below zip64.</summary>
        private static List<(string name, int method, int flags, int extra, long offset)> CentralRecords(byte[] raw)
        {
            var end = raw.Length - 22;
            Assert.AreEqual(0x06054b50u, BitConverter.ToUInt32(raw, end), "An end record with no comment.");
            var at = (int)BitConverter.ToUInt32(raw, end + 16);
            var list = new List<(string, int, int, int, long)>();
            for (var i = 0; i < BitConverter.ToUInt16(raw, end + 10); i++)
            {
                Assert.AreEqual(0x02014b50u, BitConverter.ToUInt32(raw, at));
                int nameLength = BitConverter.ToUInt16(raw, at + 28), extra = BitConverter.ToUInt16(raw, at + 30);
                list.Add((Encoding.UTF8.GetString(raw, at + 46, nameLength), BitConverter.ToUInt16(raw, at + 10),
                    BitConverter.ToUInt16(raw, at + 8), extra, BitConverter.ToUInt32(raw, at + 42)));
                at += 46 + nameLength + extra + BitConverter.ToUInt16(raw, at + 32);
            }

            return list;
        }

        // -- The api block ----------------------------------------------------------

        [Test]
        public void Pack_DropsTheApiBlock_KeepsItOnRequest_AndCopiesAPlainWorldJsonByteForByte()
        {
            var root = Path.Combine(_temp, "linked");
            Write(root, "world.json",
                "{\n  \"id\": \"0695db52-0000-7000-8000-0000000000aa\",\n  \"name\": \"Linked\",\n"
                + "  \"api\": {\n    \"world_id\": \"0695db52-0000-7000-8000-0000000000aa\",\n    \"api_key\": \"ow_k_secret\"\n  },\n"
                + "  \"time_current\": 5\n}\n");

            var dropped = OWWorldArchive.Read(PackTo(root, "dropped.owworld"));
            Assert.IsNull(dropped.World["api"], "The key went into the archive.");
            Assert.AreEqual("Linked", dropped.WorldName);
            Assert.AreEqual(5, dropped.World["time_current"].Value<int>(), "Only the api block is dropped.");
            Assert.IsFalse(ArchiveText(Path.Combine(_temp, "dropped.owworld")).Contains("ow_k_secret"));

            var kept = Path.Combine(_temp, "kept.owworld");
            OWWorldArchive.Pack(root, kept, keepApiBlock: true);
            CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(root, "world.json")), EntryBytes(kept, "linked/world.json"));

            var fixture = RequireFixture();
            var plain = Path.Combine(_temp, "plain.owworld");
            OWWorldArchive.Pack(fixture, plain);
            CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(fixture, "world.json")),
                EntryBytes(plain, Path.GetFileName(fixture) + "/world.json"), "No api block: world.json goes in byte for byte.");
        }

        [Test]
        public void Read_StripsAnApiBlock_NeverReturnsIt_AndStillReadsTheFile()
        {
            // Folder spec §4.7, OW-10: a reader strips the block and does not refuse the file (refusing would strand
            // a person's own leaked copy). Before, Read returned it, and the world read as linked.
            var world = "{\n  \"id\": \"" + WId + "\",\n  \"name\": \"Leaked\",\n"
                        + "  \"api\": {\n    \"world_id\": \"0695db52-0000-7000-8000-0000000000bb\",\n    \"api_key\": \"ow_k_secret\"\n  },\n"
                        + "  \"time_current\": 5\n}\n";
            var archive = MakeZip("leaked.owworld", ("w/world.json", world), ("w/elements/character/one.json", ElementBody));

            var read = OWWorldArchive.Read(archive);
            Assert.IsNull(read.World["api"], "The key came back.");
            Assert.IsFalse(read.World.ToString().Contains("ow_k_secret"));
            Assert.IsFalse(read.IsSyncable, "A world read from an archive is never linked.");
            Assert.AreEqual(WId, read.ServerWorldId, "No api.world_id to prefer once it is stripped.");
            Assert.AreEqual("Leaked", read.WorldName);
            Assert.AreEqual(5, read.World["time_current"].Value<int>(), "Only the api block goes.");
            Assert.AreEqual(1, read.Elements.Count);

            // A private copy Pack kept the block in is stripped on read the same way.
            var root = Path.Combine(_temp, "linked");
            Write(root, "world.json", world);
            var kept = Path.Combine(_temp, "kept.owworld");
            OWWorldArchive.Pack(root, kept, keepApiBlock: true);
            StringAssert.Contains("ow_k_secret", ArchiveText(kept), "The private copy holds the key.");
            Assert.IsNull(OWWorldArchive.Read(kept).World["api"]);
        }

        // -- The producer line (folder spec §4.2, v0.3.14) travels as its maker wrote it ----------------

        private const string ProducerLine =
            "  \"producer\": {\n    \"tool\": \"azgaar-converter\",\n    \"version\": \"0.4.0\",\n    \"input\": \"azgaar-map\"\n  },\n";

        [Test]
        public void Pack_KeepsTheProducerLine_ByteForByte_AndWhenItStripsTheApiBlock()
        {
            // An archive is a copy, not a birth: the line says who made the folder, so it goes in as written.
            var plain = Path.Combine(_temp, "made");
            Write(plain, "world.json", "{\n  \"id\": \"" + WId + "\",\n  \"name\": \"Made\",\n" + ProducerLine + "  \"x_tool\": true\n}\n");
            Write(plain, "elements/character/one.json", ElementBody);
            var archive = PackTo(plain, "made.owworld");
            CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(plain, "world.json")), EntryBytes(archive, "made/world.json"));

            // Re-emitted without its api block, the rest of world.json, the producer line among it, is as it was.
            var linked = Path.Combine(_temp, "linked-made");
            Write(linked, "world.json", "{\n  \"id\": \"" + WId + "\",\n  \"name\": \"Made\",\n" + ProducerLine
                                        + "  \"api\": {\n    \"world_id\": \"" + WId + "\",\n    \"api_key\": \"ow_k_secret\"\n  },\n"
                                        + "  \"x_tool\": true\n}\n");
            var stripped = Encoding.UTF8.GetString(EntryBytes(PackTo(linked, "linked-made.owworld"), "linked-made/world.json"));
            Assert.AreEqual("{\n  \"id\": \"" + WId + "\",\n  \"name\": \"Made\",\n" + ProducerLine + "  \"x_tool\": true\n}\n", stripped);
        }

        [Test]
        public void Read_AnInputWordNoReaderKnows_IsData_TheFolderAndTheArchiveReadIt()
        {
            // §4.2: "a reader never refuses a folder on a word it does not know". The package's vector.
            var root = Path.Combine(_temp, "papyrus");
            Write(root, "world.json", "{\n  \"id\": \"" + WId + "\",\n  \"name\": \"Papyrus\",\n"
                                      + "  \"producer\": {\n    \"tool\": \"papyrus\",\n    \"version\": \"9\",\n    \"input\": \"papyrus-scroll\"\n  }\n}\n");
            Write(root, "elements/character/one.json", ElementBody);

            foreach (var read in new[] { OWFolderReader.Read(root), OWWorldArchive.Read(PackTo(root, "papyrus.owworld")) })
            {
                Assert.AreEqual("papyrus-scroll", (string)read.World["producer"]["input"]);
                Assert.AreEqual(1, read.Elements.Count);
                Assert.IsEmpty(read.Skipped);
            }
        }

        private string PackTo(string root, string fileName)
        {
            var path = Path.Combine(_temp, fileName);
            OWWorldArchive.Pack(root, path);
            return path;
        }

        private static byte[] EntryBytes(string archive, string name)
        {
            using (var zip = ZipFile.OpenRead(archive))
            using (var s = zip.GetEntry(name).Open())
            using (var m = new MemoryStream())
            {
                s.CopyTo(m);
                return m.ToArray();
            }
        }

        private static string ArchiveText(string archive)
        {
            var sb = new StringBuilder();
            using (var zip = ZipFile.OpenRead(archive))
            {
                foreach (var e in zip.Entries)
                    using (var r = new StreamReader(e.Open())) sb.Append(r.ReadToEnd());
            }

            return sb.ToString();
        }

        // -- Reading an archive equals reading the folder -------------------------

        [Test]
        public void Read_EqualsTheFolderRead_TheFixture()
        {
            var root = RequireFixture();
            var archive = PackTo(root, "fixture.owworld");

            var folder = OWFolderReader.Read(root, includeLegacySpatial: true);
            var packed = OWWorldArchive.Read(archive, includeLegacySpatial: true);

            Assert.That(folder.Elements.Count, Is.GreaterThan(0), "The fixture has elements to compare.");
            CollectionAssert.AreEqual(Digest(folder, root), Digest(packed, null));
        }

        [Test]
        public void Read_EqualsTheFolderRead_AnAbbeyInTheLegacyLayout()
        {
            var root = MakeAbbey();
            var archive = PackTo(root, "abbey.owworld");

            var folder = OWFolderReader.Read(root, includeLegacySpatial: true);
            var packed = OWWorldArchive.Read(archive, includeLegacySpatial: true);

            Assert.AreEqual(1, folder.Elements.Count);
            Assert.AreEqual(4, folder.LegacyElements.Count, "spatial/ holds the world.");
            Assert.AreEqual(3, folder.Skipped.Count, "Duplicate, no id, truncated.");
            CollectionAssert.AreEqual(Digest(folder, root), Digest(packed, null));

            // And with the legacy layout off, as a conforming reader that covers only elements/.
            CollectionAssert.AreEqual(
                Digest(OWFolderReader.Read(root, includeLegacySpatial: false), root),
                Digest(OWWorldArchive.Read(archive, includeLegacySpatial: false), null));
        }

        [Test]
        public void Read_ReportsPathsInsideTheWorld()
        {
            var packed = OWWorldArchive.Read(PackTo(MakeAbbey(), "abbey.owworld"));

            var paths = packed.Elements.Concat(packed.LegacyElements).Select(e => e.Path)
                .Concat(packed.Skipped.Select(s => s.Path)).ToList();
            Assert.That(paths, Has.Member("spatial/pin/no-id.json"));
            foreach (var p in paths) Assert.IsFalse(Path.IsPathRooted(p), p);
        }

        [Test]
        public void Read_WritesNoTempFolder()
        {
            var archive = PackTo(MakeAbbey(), "abbey.owworld");
            var before = Directory.GetDirectories(Path.GetTempPath(), "ow-owworld-*").Length;

            OWWorldArchive.Read(archive);
            Assert.Throws<OWFolderFormatException>(() => OWWorldArchive.Read(MakeZip("none.owworld", ("a/b.json", "{}"))));

            Assert.AreEqual(before, Directory.GetDirectories(Path.GetTempPath(), "ow-owworld-*").Length);
        }

        // -- The in-memory read is the folder read ----------------------------------------

        /// <summary>A read, in its own order: what a cache is filled in, element by element.</summary>
        private static List<string> OrderedDigest(OWFolderReadResult read, string folderRoot)
        {
            var prefix = folderRoot == null ? null : Path.GetFullPath(folderRoot).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            string Rel(string p) => prefix != null && p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? p.Substring(prefix.Length).Replace('\\', '/')
                : p;

            var lines = new List<string> { "world " + read.World.ToString(Formatting.None) };
            lines.AddRange(read.Elements.Select(e =>
                $"element {e.Id} {e.Type} {e.TypeInferred} {Rel(e.Path)} {e.Body.ToString(Formatting.None)}"));
            lines.AddRange(read.LegacyElements.Select(e =>
                $"legacy {e.Id} {e.Type} {e.TypeInferred} {Rel(e.Path)} {e.Body.ToString(Formatting.None)}"));
            lines.AddRange(read.Skipped.Select(s => $"skipped {Rel(s.Path)} {s.Reason}"));
            return lines;
        }

        /// <summary>Every file under a folder, as (path inside it, text): the folder, held in memory.</summary>
        private static List<KeyValuePair<string, string>> Entries(string root)
        {
            var full = Path.GetFullPath(root).TrimEnd('\\', '/');
            return Directory.GetFiles(full, "*", SearchOption.AllDirectories)
                .Select(f => new KeyValuePair<string, string>(f.Substring(full.Length + 1), File.ReadAllText(f)))
                .ToList();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ReadEntries_EqualsRead_TheFixture_InOrder(bool legacy)
        {
            var root = RequireFixture();
            var folder = OWFolderReader.Read(root, legacy);

            Assert.That(folder.Elements.Count, Is.GreaterThan(0));
            CollectionAssert.AreEqual(OrderedDigest(folder, root),
                OrderedDigest(OWFolderReader.ReadEntries(Entries(root), legacy), null));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ReadEntries_EqualsRead_AnAbbeyInTheLegacyLayout_InOrder(bool legacy)
        {
            var root = MakeAbbey();
            var folder = OWFolderReader.Read(root, legacy);

            Assert.AreEqual(legacy ? 4 : 0, folder.LegacyElements.Count);
            CollectionAssert.AreEqual(OrderedDigest(folder, root),
                OrderedDigest(OWFolderReader.ReadEntries(Entries(root), legacy), null));
        }

        [Test]
        public void ReadEntries_ReadsOnlyWhereTheFolderReads_AndSkipsAnUnreadableText()
        {
            var read = OWFolderReader.ReadEntries(new Dictionary<string, string>
            {
                ["world.json"] = WorldJson("0695db52-0000-7000-8000-0000000000aa", "W"),
                [@"elements\character\one.json"] = ElementBody,
                ["elements/character/nested/deep.json"] = ElementBody.Replace("e1", "e2"),
                // A folder named like a file: the folder form's GetFiles never returns what is inside it.
                ["elements/character/sub.json/deep.json"] = ElementBody.Replace("e1", "e6"),
                ["elements/character/notes.txt"] = "{}",
                ["elements/top.json"] = ElementBody.Replace("e1", "e3"),
                ["spatial/region/r.json"] = ElementBody.Replace("e1", "e4"),
                ["spatial/pin/gone.json"] = null,
                ["media/x/x.meta.json"] = ElementBody.Replace("e1", "e5"),
            });

            Assert.AreEqual(new[] { "0695db83-0000-7000-8000-0000000000e1" }, read.Elements.Select(e => e.Id).ToArray());
            Assert.AreEqual("elements/character/one.json", read.Elements[0].Path);
            Assert.AreEqual(0, read.LegacyElements.Count);
            Assert.AreEqual(1, read.Skipped.Count);
            Assert.AreEqual("spatial/pin/gone.json", read.Skipped[0].Path);
            Assert.AreEqual("unparseable JSON", read.Skipped[0].Reason, "As the folder form says of a file it cannot read.");
        }

        [Test]
        public void ReadEntries_RefusesNoWorldJson_ABadOne_AndARepeatedPath()
        {
            var e = Assert.Throws<OWFolderFormatException>(() => OWFolderReader.ReadEntries(
                new Dictionary<string, string> { ["elements/character/one.json"] = ElementBody }, source: "test.owworld"));
            StringAssert.Contains("test.owworld", e.Message);

            Assert.Throws<OWFolderFormatException>(() => OWFolderReader.ReadEntries(
                new Dictionary<string, string> { ["world.json"] = "{\"name\": \"no id\"}" }));
            Assert.Throws<OWFolderFormatException>(() => OWFolderReader.ReadEntries(
                new Dictionary<string, string> { ["world.json"] = null }));
            Assert.Throws<ArgumentException>(() => OWFolderReader.ReadEntries(new[]
            {
                new KeyValuePair<string, string>("world.json", WorldJson("0695db52-0000-7000-8000-0000000000aa", "W")),
                new KeyValuePair<string, string>("elements/character/one.json", ElementBody),
                new KeyValuePair<string, string>(@"elements\character\one.json", ElementBody),
            }));
        }

        // The spec's world.json requires "id (non-empty string)" (folder spec §4.1); the TypeScript reader refuses the
        // same cases (sdk/test/world-file.test.mjs: a number id, a blank id).
        [TestCase("{\"id\": 7, \"name\": \"number id\"}")]
        [TestCase("{\"id\": \"  \", \"name\": \"blank id\"}")]
        [TestCase("{\"id\": \"\", \"name\": \"empty id\"}")]
        [TestCase("{\"id\": null, \"name\": \"null id\"}")]
        [TestCase("{\"id\": true, \"name\": \"bool id\"}")]
        [TestCase("{\"id\": [\"0695db52-0000-7000-8000-0000000000aa\"], \"name\": \"list id\"}")]
        public void Read_RefusesAWorldIdThatIsNotANonEmptyString_InAnArchive_AFolder_AndAPack(string worldJson)
        {
            var e = Assert.Throws<OWFolderFormatException>(() => OWWorldArchive.Read(MakeZip("id.owworld",
                ("w/world.json", worldJson), ("w/elements/character/one.json", ElementBody))));
            StringAssert.Contains("id", e.Message);

            var root = Path.Combine(_temp, "w");
            Write(root, "world.json", worldJson);
            Write(root, "elements/character/one.json", ElementBody);
            Assert.Throws<OWFolderFormatException>(() => OWFolderReader.Read(root));

            // A writer refuses what a reader would: Pack never makes an archive its own Read refuses.
            Assert.Throws<OWFolderFormatException>(() => OWWorldArchive.Pack(root, Path.Combine(_temp, "id-pack.owworld")));
            Assert.IsFalse(File.Exists(Path.Combine(_temp, "id-pack.owworld")), "A refused pack wrote nothing.");
        }

        // -- Where the world sits in the zip -----------------------------------------

        [Test]
        public void Read_AcceptsWorldJsonAtTheRoot()
        {
            var archive = MakeZip("root.owworld",
                ("world.json", WorldJson("0695db52-0000-7000-8000-0000000000aa", "Root")),
                ("elements/character/one.json", ElementBody),
                ("README.txt", "not the world"));

            var read = OWWorldArchive.Read(archive);
            Assert.AreEqual("Root", read.WorldName);
            Assert.AreEqual(new[] { "0695db83-0000-7000-8000-0000000000e1" }, read.Elements.Select(e => e.Id).ToArray());
        }

        [Test]
        public void Read_ARecordsWorldJson_IsTheWorldsData_NotASecondWorld()
        {
            // A producer may keep a world.json of its own under records/ (folder spec §4.5 rule 2).
            var archive = MakeZip("records.owworld",
                ("world.json", WorldJson("0695db52-0000-7000-8000-0000000000aa", "Root")),
                ("elements/character/one.json", ElementBody),
                ("records/world.json", "{\"producer\": \"its own\"}"));

            var read = OWWorldArchive.Read(archive);
            Assert.AreEqual("Root", read.WorldName);
            Assert.AreEqual(1, read.Elements.Count, "records/ is never read as elements (§4.5 rule 4).");
        }

        [Test]
        public void Pack_CarriesRecordsByteForByte()
        {
            var root = Path.Combine(_temp, "w");
            Write(root, "world.json", WorldJson("0695db52-0000-7000-8000-0000000000aa", "W"));
            var bytes = new byte[] { 0x7B, 0x7D, 0x0D, 0x0A, 0xEF, 0xBB, 0xBF, 0x00, 0xFF };
            Directory.CreateDirectory(Path.Combine(root, "records", "lab"));
            File.WriteAllBytes(Path.Combine(root, "records", "lab", "ledger.bin"), bytes);

            var archive = Path.Combine(_temp, "w.owworld");
            OWWorldArchive.Pack(root, archive);

            using (var zip = ZipFile.OpenRead(archive))
            using (var stream = zip.GetEntry("w/records/lab/ledger.bin").Open())
            using (var copy = new MemoryStream())
            {
                stream.CopyTo(copy);
                CollectionAssert.AreEqual(bytes, copy.ToArray(), "A producer's file travels untouched (§4.5 rule 6).");
            }
        }

        [Test]
        public void Pack_Records_LeavesOutUnitysMeta_ButKeepsAProducersOwnXMetaBesideX()
        {
            var root = Path.Combine(_temp, "w");
            Write(root, "world.json", WorldJson("0695db52-0000-7000-8000-0000000000aa", "W"));
            // The producer's pair: its own file x and its own x.meta, which only shares Unity's suffix.
            var producerMeta = new byte[] { 0x7B, 0x22, 0x6E, 0x22, 0x3A, 0x31, 0x7D, 0x0D, 0x0A, 0x00, 0xFF };
            Write(root, "records/lab/ledger", "1\n");
            Directory.CreateDirectory(Path.Combine(root, "records", "lab"));
            File.WriteAllBytes(Path.Combine(root, "records", "lab", "ledger.meta"), producerMeta);
            // Unity's litter: a .meta beside a file (LF, and CRLF as a git checkout may leave it) and beside a folder.
            Write(root, "records/lab/plan.json", "{}\n");
            Write(root, "records/lab/plan.json.meta", UnityMeta);
            Write(root, "records/lab/tick.json", "{}\n");
            Write(root, "records/lab/tick.json.meta", UnityMeta.Replace("\n", "\r\n"));
            Write(root, "records/lab/days/1.json", "{}\n");
            Write(root, "records/lab/days.meta", "fileFormatVersion: 2\nguid: 0b1c2d3e4f5a6b7c8d9e0f1a2b3c4d5e\n"
                + "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n");

            var archive = Path.Combine(_temp, "w.owworld");
            var entries = OWWorldArchive.Pack(root, archive);

            CollectionAssert.AreEqual(new[]
            {
                "mimetype",
                "w/records/lab/days/1.json",
                "w/records/lab/ledger",
                "w/records/lab/ledger.meta",
                "w/records/lab/plan.json",
                "w/records/lab/tick.json",
                "w/world.json",
            }, entries);
            CollectionAssert.AreEqual(producerMeta, EntryBytes(archive, "w/records/lab/ledger.meta"),
                "A producer's own x.meta travels byte for byte (§4.5 rule 6a).");
        }

        [Test]
        public void Read_AcceptsOneTopLevelFolder_IgnoringWhatSitsBesideIt()
        {
            var archive = MakeZip("one.owworld",
                ("README.txt", "not the world"),
                ("__MACOSX/top/._world.json", "junk"),
                (".atlas/world.json", WorldJson("0695db52-0000-7000-8000-0000000000ff", "A tool's")),
                ("top/world.json", WorldJson("0695db52-0000-7000-8000-0000000000aa", "Top")),
                ("top/elements/character/one.json", ElementBody));

            var read = OWWorldArchive.Read(archive);
            Assert.AreEqual("Top", read.WorldName);
            Assert.AreEqual(1, read.Elements.Count);
        }

        [Test]
        public void Read_RefusesTwoWorlds()
        {
            var archive = MakeZip("two.owworld",
                ("a/world.json", WorldJson("0695db52-0000-7000-8000-0000000000aa", "A")),
                ("b/world.json", WorldJson("0695db52-0000-7000-8000-0000000000bb", "B")));

            var e = Assert.Throws<OWFolderFormatException>(() => OWWorldArchive.Read(archive));
            StringAssert.Contains("2 worlds", e.Message);
        }

        [Test]
        public void Read_RefusesAWorldAtTheRootBesideOneInAFolder()
        {
            var archive = MakeZip("both.owworld",
                ("world.json", WorldJson("0695db52-0000-7000-8000-0000000000aa", "Root")),
                ("b/world.json", WorldJson("0695db52-0000-7000-8000-0000000000bb", "B")));

            Assert.Throws<OWFolderFormatException>(() => OWWorldArchive.Read(archive));
        }

        [Test]
        public void Read_RefusesNoWorld_IncludingOneNestedTooDeep()
        {
            var archive = MakeZip("deep.owworld",
                ("onlyworlds-atlas/abbey-0695/world.json", WorldJson("0695db52-0000-7000-8000-0000000000aa", "Deep")));

            var e = Assert.Throws<OWFolderFormatException>(() => OWWorldArchive.Read(archive));
            StringAssert.Contains("No world.json", e.Message);
        }

        [Test]
        public void Read_RefusesAFileThatIsNotAZip()
        {
            var path = Path.Combine(_temp, "json.owworld");
            File.WriteAllText(path, WorldJson("0695db52-0000-7000-8000-0000000000aa", "Not a zip"));

            var e = Assert.Throws<OWFolderFormatException>(() => OWWorldArchive.Read(path));
            StringAssert.Contains("not a zip", e.Message);
        }

        // -- The budget (a zip bomb is refused before it is inflated) ---------------------------

        [Test]
        public void Read_HoldsTheSpecsFloor_ByDefault()
        {
            // Folder spec §4.7 (OW-15): every reader accepts 500,000 entries and 2 GiB. The defaults were the
            // TypeScript reader's (1 GiB, 200,000), below the floor.
            Assert.AreEqual(2147483648L, OWWorldArchive.DefaultMaxBytes);
            Assert.AreEqual(500000, OWWorldArchive.DefaultMaxEntries);

            var options = new OWWorldArchiveReadOptions();
            Assert.AreEqual(OWWorldArchive.DefaultMaxBytes, options.MaxBytes);
            Assert.AreEqual(OWWorldArchive.DefaultMaxEntries, options.MaxEntries);
        }

        [Test]
        public void Read_AcceptsUpToTheFloorsBytes_ByDefault_AndRefusesOneByteMore()
        {
            // The budget is held from declared sizes alone, so declaring a media file's size puts the archive at the
            // floor without a 2 GiB file. That file then lies (it holds 1 byte), which the later check of every entry
            // refuses as a broken entry: inside the budget means refused for that, never for MaxBytes.
            var world = WorldJson(WId, "W");
            var archive = MakeZip("floor.owworld",
                ("w/world.json", world), ("w/elements/character/one.json", ElementBody), ("w/media/big.bin", "x"));
            long others = Encoding.UTF8.GetByteCount(world) + Encoding.UTF8.GetByteCount(ElementBody);

            void InsideTheBudget(string why, OWWorldArchiveReadOptions options = null)
            {
                var past = Assert.Throws<OWFolderFormatException>(() => OWWorldArchive.Read(archive, options: options));
                Assert.AreEqual(OWWorldArchiveReason.NotOpenable, past.Reason, why);
                StringAssert.Contains("holds 1 bytes and declares", past.Message, why);
            }

            DeclareSize(archive, "w/media/big.bin", (uint)(OWWorldArchive.DefaultMaxBytes - others));
            InsideTheBudget("Exactly 2 GiB declared: inside the floor.");

            DeclareSize(archive, "w/media/big.bin", (uint)(OWWorldArchive.DefaultMaxBytes - others + 1));
            var over = Refused(archive);
            Assert.AreEqual(OWWorldArchiveReason.TooBig, over.Reason);
            StringAssert.Contains("MaxBytes", over.Message);

            // A caller's lower budget still holds.
            DeclareSize(archive, "w/media/big.bin", 1u << 30);
            var lower = new OWWorldArchiveReadOptions { MaxBytes = 1L << 30 };
            StringAssert.Contains("MaxBytes", Assert.Throws<OWFolderFormatException>(() => OWWorldArchive.Read(archive, options: lower)).Message);
            InsideTheBudget("1 GiB and more is inside the floor.");
        }

        [Test]
        public void Read_RefusesPastTheBudget_ByDeclaredSize_AndByCount_AndAcceptsUpToIt()
        {
            // The TypeScript reader's vector (world-file.test.mjs, "refuses past the budget"): a media file past the
            // bytes, one file past the count. Media is never inflated here, so only its declared size can refuse it.
            var world = WorldJson("0695db52-0000-7000-8000-0000000000aa", "W");
            var archive = MakeZip("budget.owworld",
                ("w/world.json", world),
                ("w/elements/character/one.json", ElementBody),
                ("w/media/big.bin", new string('x', 64)));
            long total = Encoding.UTF8.GetByteCount(world) + Encoding.UTF8.GetByteCount(ElementBody) + 64;

            var bytes = Assert.Throws<OWFolderFormatException>(() =>
                OWWorldArchive.Read(archive, options: new OWWorldArchiveReadOptions { MaxBytes = total - 1 }));
            StringAssert.Contains("MaxBytes", bytes.Message);
            Assert.AreEqual(1, OWWorldArchive.Read(archive, options: new OWWorldArchiveReadOptions { MaxBytes = total }).Elements.Count);

            var count = Assert.Throws<OWFolderFormatException>(() =>
                OWWorldArchive.Read(archive, options: new OWWorldArchiveReadOptions { MaxEntries = 2 }));
            StringAssert.Contains("MaxEntries", count.Message);
            Assert.AreEqual(1, OWWorldArchive.Read(archive, options: new OWWorldArchiveReadOptions { MaxEntries = 3 }).Elements.Count);

            Assert.AreEqual(1, OWWorldArchive.Read(archive).Elements.Count, "The defaults take a small world.");
        }

        [Test]
        public void Read_RefusesAnEntryThatInflatesPastItsDeclaredSize()
        {
            // The TypeScript reader's lying size (world-file.test.mjs, lyingSize): the headers say 1 byte, the entry
            // holds 4 KiB. The declared total passes the budget; the inflated bytes must not.
            var big = "{\n  \"id\": \"0695db83-0000-7000-8000-0000000000b1\",\n  \"pad\": \"" + new string('x', 4096) + "\"\n}\n";
            var archive = MakeZip("lying.owworld",
                ("w/world.json", WorldJson("0695db52-0000-7000-8000-0000000000aa", "W")),
                ("w/elements/character/big.json", big));
            var budget = new OWWorldArchiveReadOptions { MaxBytes = 1024 };

            var honest = Assert.Throws<OWFolderFormatException>(() => OWWorldArchive.Read(archive, options: budget));
            StringAssert.Contains("MaxBytes", honest.Message, "Told the truth, the size refuses it unopened.");

            DeclareSize(archive, "w/elements/character/big.json", 1);
            var e = Assert.Throws<OWFolderFormatException>(() => OWWorldArchive.Read(archive, options: budget));
            StringAssert.Contains("inflates past the 1 bytes it declares", e.Message);
        }

        [Test]
        public void Read_RefusesAStoredEntryWhoseSizeLies_WithoutReadingIt()
        {
            // Folder spec §4.7 (OW-14): a stored entry holding more than it declares is the zip bomb's lie, too_big,
            // found from the directory alone, so in media/ too, which is never read. Mono writes no stored entry, so
            // the method is declared over a deflated one: its stored bytes are then the deflate stream.
            var more = WorldZip("stored-more.owworld", ("w/media/a.png", "png")); // 3 bytes deflate to 5
            SetMethod(more, "w/media/a.png", 0);
            var e = Refused(more);
            Assert.AreEqual(OWWorldArchiveReason.TooBig, e.Reason);
            StringAssert.Contains("is stored and declares 3 bytes but holds", e.Message);

            var fewer = WorldZip("stored-fewer.owworld", ("w/media/a.png", new string('x', 64))); // 64 deflate to a few
            SetMethod(fewer, "w/media/a.png", 0);
            Assert.AreEqual(OWWorldArchiveReason.NotOpenable, Refused(fewer).Reason, "Holding less is a broken entry.");
        }

        // -- Bytes the zip was made with (folder spec §4.7, OW-14's CRC) ------------------------------

        [Test]
        public void Read_RefusesAnElementWhoseBytesFailItsCrc_NeverReadsItAsOtherText()
        {
            // The conformance set's fail-OW-14-crc: one bit off in the CRC its headers declare. Mono's ZipArchive does
            // not compare it, so before the check the element was read as written.
            var archive = WorldZip("crc.owworld");
            Assert.AreEqual(1, OWWorldArchive.Read(archive).Elements.Count, "Untouched, the world reads.");

            FlipCrc(archive, "w/elements/character/one.json");
            var e = Refused(archive);
            Assert.AreEqual(OWWorldArchiveReason.NotOpenable, e.Reason);
            StringAssert.Contains("'w/elements/character/one.json' fails its CRC-32", e.Message);
        }

        [Test]
        public void Read_RefusesAWorldJsonOrAMarkerWhoseBytesFailTheirCrc_ForThatCause()
        {
            var world = WorldZip("crc-world.owworld");
            FlipCrc(world, "w/world.json");
            Assert.AreEqual(OWWorldArchiveReason.NotOpenable, Refused(world).Reason, "Not not_a_world: the file is broken, not unworldly.");

            // A marker of the right bytes and a wrong CRC is a broken entry, not a marker that says something else.
            var marker = WorldZip("crc-marker.owworld", ("mimetype", OWWorldArchive.MediaType));
            Assert.AreEqual(1, OWWorldArchive.Read(marker).Elements.Count);
            FlipCrc(marker, "mimetype");
            Assert.AreEqual(OWWorldArchiveReason.NotOpenable, Refused(marker).Reason);
        }

        [Test]
        public void Read_RefusesABrokenDeflateStream_AndAnEntryHoldingLessThanItDeclares()
        {
            // A broken stream was an unreadable text, skipped as a folder skips a file it cannot read; in an archive it
            // is a broken archive (the TypeScript reader and the reference checker: not_openable).
            var broken = WorldZip("broken.owworld");
            PatchData(broken, "w/elements/character/one.json", (bytes, at) => bytes[at] = 0x07); // a final block of reserved type 3
            var e = Refused(broken);
            Assert.AreEqual(OWWorldArchiveReason.NotOpenable, e.Reason);
            StringAssert.Contains("cannot be inflated", e.Message);

            var shorter = WorldZip("short.owworld");
            DeclareSize(shorter, "w/elements/character/one.json", (uint)Encoding.UTF8.GetByteCount(ElementBody) + 10);
            var s = Refused(shorter);
            Assert.AreEqual(OWWorldArchiveReason.NotOpenable, s.Reason);
            StringAssert.Contains($"holds {Encoding.UTF8.GetByteCount(ElementBody)} bytes and declares", s.Message);
        }

        [TestCase("w/media/a.png")]
        [TestCase("w/records/lab/ledger.bin")]
        [TestCase("README.txt")] // beside the world, outside it
        public void Read_RefusesAnyFileWhoseBytesFailItsCrc_NotOnlyTheOnesItReads(string name)
        {
            // §4.7: "an entry whose bytes fail its CRC refuses the archive". The read returns none of these, but the
            // TypeScript reader and the reference checker check every file, so a corrupt image refuses here too.
            var archive = WorldZip("crc-any.owworld", (name, "bytes the world never reads"));
            Assert.AreEqual(1, OWWorldArchive.Read(archive).Elements.Count, "Untouched, the world reads.");

            FlipCrc(archive, name);
            var e = Refused(archive);
            Assert.AreEqual(OWWorldArchiveReason.NotOpenable, e.Reason);
            StringAssert.Contains($"'{name}' fails its CRC-32", e.Message);
        }

        [Test]
        public void Read_RefusesAMediaFileThatInflatesPastItsDeclaredSize()
        {
            // The lying size (OW-14) in a deflated image: refused when it is checked, as in an element.
            var archive = WorldZip("media-lying.owworld", ("w/media/a.png", new string('x', 4096)));
            DeclareSize(archive, "w/media/a.png", 64);
            var e = Refused(archive);
            Assert.AreEqual(OWWorldArchiveReason.TooBig, e.Reason);
            StringAssert.Contains("inflates past the 64 bytes it declares", e.Message);
        }

        /// <summary>Flips the lowest bit of the CRC-32 an entry declares, in its local header (14) and its central record (16).</summary>
        private static void FlipCrc(string archive, string name)
            => PatchEntry(archive, name, (bytes, at, central) => bytes[at + (central ? 16 : 14)] ^= 1);

        /// <summary>Rewrites an entry's stored data in place: <paramref name="patch"/> gets the bytes and where its data starts.</summary>
        private static void PatchData(string archive, string name, Action<byte[], int> patch)
            => PatchEntry(archive, name, (bytes, at, central) =>
            {
                if (central) return;
                patch(bytes, at + 30 + BitConverter.ToUInt16(bytes, at + 26) + BitConverter.ToUInt16(bytes, at + 28));
            });

        [Test]
        public void Read_RefusesAUtf8FlaggedNameWhoseBytesAreNotUtf8()
        {
            // Folder spec §4.7 (OW-22): the flag says UTF-8; bytes that are not would be read as U+FFFD.
            var archive = WorldZip("bad-utf8.owworld", ("w/media/café.png", "png"));
            var good = Encoding.UTF8.GetBytes("w/media/café.png");
            var bad = (byte[])good.Clone();
            bad[Array.IndexOf(bad, (byte)0xC3)] = 0xE9; // a Latin-1 é, then 0xA9 as a stray continuation byte
            PatchEntry(archive, "w/media/café.png", (bytes, at, central) => Array.Copy(bad, 0, bytes, at + (central ? 46 : 30), bad.Length));

            var e = Refused(archive);
            Assert.AreEqual(OWWorldArchiveReason.Name, e.Reason);
            StringAssert.Contains("bytes are not UTF-8", e.Message);
        }

        /// <summary>
        /// Rewrites the uncompressed size an entry declares, in its local header and the central directory: a size
        /// that lies, as <c>world-file.test.mjs</c>'s <c>lyingSize</c> makes one.
        /// </summary>
        private static void DeclareSize(string archive, string name, uint size)
            => PatchEntry(archive, name, (bytes, at, central) => PutU32(bytes, at + (central ? 24 : 22), size));

        /// <summary>
        /// Rewrites one entry's two headers in place: <paramref name="patch"/> gets the archive's bytes, where a
        /// header naming <paramref name="name"/> starts, and whether it is the central directory's record (else the
        /// local header). A reader may trust either, so both are patched where both carry the field.
        /// </summary>
        private static void PatchEntry(string archive, string name, Action<byte[], int, bool> patch)
        {
            var bytes = File.ReadAllBytes(archive);
            var target = Encoding.UTF8.GetBytes(name);
            var patched = 0;

            bool NameAt(int at, int length)
                => length == target.Length && at + length <= bytes.Length
                   && bytes.Skip(at).Take(length).SequenceEqual(target);

            for (var i = 0; i + 46 < bytes.Length; i++)
            {
                var sig = BitConverter.ToUInt32(bytes, i);
                if (sig == 0x04034b50 && NameAt(i + 30, BitConverter.ToUInt16(bytes, i + 26)))
                {
                    patch(bytes, i, false);
                    patched++;
                }
                else if (sig == 0x02014b50 && NameAt(i + 46, BitConverter.ToUInt16(bytes, i + 28)))
                {
                    patch(bytes, i, true);
                    patched++;
                }
            }

            Assert.AreEqual(2, patched, "One local header and one central-directory record name the entry.");
            File.WriteAllBytes(archive, bytes);
        }

        // Little-endian on every platform Unity runs on, as the zip format is.
        private static void PutU16(byte[] bytes, int at, int value) => Array.Copy(BitConverter.GetBytes((ushort)value), 0, bytes, at, 2);

        private static void PutU32(byte[] bytes, int at, uint value) => Array.Copy(BitConverter.GetBytes(value), 0, bytes, at, 4);

        /// <summary>Sets general-purpose flag bits on an entry (local header offset 6, central record offset 8).</summary>
        private static void SetFlags(string archive, string name, int set, int clear = 0)
            => PatchEntry(archive, name, (bytes, at, central) =>
            {
                var field = at + (central ? 8 : 6);
                PutU16(bytes, field, (BitConverter.ToUInt16(bytes, field) | set) & ~clear);
            });

        /// <summary>Declares a compression method on an entry (local header offset 8, central record offset 10).</summary>
        private static void SetMethod(string archive, string name, int method)
            => PatchEntry(archive, name, (bytes, at, central) => PutU16(bytes, at + (central ? 10 : 8), method));

        /// <summary>
        /// Gives an entry a Unix mode, as a zip made on Unix records it: the central record's "made by" host is Unix
        /// (3) and the mode sits in the external attributes' high 16 bits. The local header carries neither.
        /// </summary>
        private static void SetUnixMode(string archive, string name, int mode)
            => PatchEntry(archive, name, (bytes, at, central) =>
            {
                if (!central) return;
                bytes[at + 5] = 3;
                PutU32(bytes, at + 38, (uint)mode << 16);
            });

        // -- Hostile entry names ----------------------------------------------------------

        [TestCase("w/../escaped.json")]
        [TestCase("../escaped.json")]
        [TestCase("w/elements/../../../escaped.json")]
        [TestCase("/etc/escaped.json")]
        [TestCase(@"\\server\share\escaped.json")]
        [TestCase("C:/escaped.json")]
        [TestCase(@"C:\escaped.json")]
        public void Read_RefusesAnEntryThatIsAbsoluteOrClimbsOut(string hostile)
        {
            var archive = MakeZip("hostile.owworld",
                ("w/world.json", WorldJson("0695db52-0000-7000-8000-0000000000aa", "W")),
                ("w/elements/character/one.json", ElementBody),
                (hostile, ElementBody.Replace("e1", "e9")));

            var e = Assert.Throws<OWFolderFormatException>(() => OWWorldArchive.Read(archive));
            StringAssert.Contains("refusing the archive", e.Message);
        }

        [TestCase(@"w\elements\character\one.json", "backslash")] // would be a second spelling of one.json
        [TestCase(@"w\elements\character\two.json", "backslash")]
        [TestCase("w/elements/location/../character/two.json", "'..' segment")] // stays inside the world
        [TestCase("w/elements/./character/two.json", "'..' segment")]
        [TestCase("w//elements/character/two.json", "'..' segment")]
        [TestCase("w/elements/character//", "'..' segment")] // a folder entry with an empty segment
        [TestCase("./w/media/a.png", "'..' segment")]
        public void Read_RefusesASoftPath_WhateverItWouldResolveTo(string soft, string said)
        {
            // Folder spec §4.7, OW-16, ruled strict: an entry name is a plain relative path. Before, these were
            // resolved and read (a backslash as '/', '.' and empty segments dropped, '..' climbed back inside).
            var archive = MakeZip("soft.owworld",
                ("w/world.json", WorldJson("0695db52-0000-7000-8000-0000000000aa", "W")),
                ("w/elements/character/one.json", ElementBody),
                (soft, ElementBody.Replace("e1\"", "e2\"")));

            var e = Assert.Throws<OWFolderFormatException>(() => OWWorldArchive.Read(archive));
            StringAssert.Contains(said, e.Message);
            StringAssert.Contains("refusing the archive", e.Message);
        }

        // -- Every refusal names its cause (folder spec §4.7) --------------------------------

        [Test]
        public void Read_NamesTheCauseOfEveryRefusal_FromTheSpecsList()
        {
            string Why(string archive, OWWorldArchiveReadOptions options = null)
                => Assert.Throws<OWFolderFormatException>(() => OWWorldArchive.Read(archive, options: options)).Reason;

            var notZip = Path.Combine(_temp, "not-zip.owworld");
            File.WriteAllText(notZip, "{}");
            Assert.AreEqual(OWWorldArchiveReason.NotOpenable, Why(notZip));
            Assert.AreEqual(OWWorldArchiveReason.NotAWorld, Why(MakeZip("none.owworld", ("a/b.json", "{}"))));
            Assert.AreEqual(OWWorldArchiveReason.NotAWorld, Why(MakeZip("no-id.owworld", ("w/world.json", "{}"))));
            Assert.AreEqual(OWWorldArchiveReason.MoreThanOne, Why(MakeZip("two.owworld",
                ("a/world.json", WorldJson(WId, "A")), ("b/world.json", WorldJson(WId, "B")))));
            Assert.AreEqual(OWWorldArchiveReason.MoreThanOne, Why(WorldZip("nested.owworld", ("w/x/world.json", WorldJson(WId, "X")))));
            Assert.AreEqual(OWWorldArchiveReason.UnsafePath, Why(WorldZip("climb.owworld", ("w/../x.json", "{}"))));
            Assert.AreEqual(OWWorldArchiveReason.UnsafePath, Why(WorldZip("file-folder.owworld", ("w/media", "a"), ("w/media/a.png", "b"))));
            Assert.AreEqual(OWWorldArchiveReason.TooBig, Why(WorldZip("big.owworld"), new OWWorldArchiveReadOptions { MaxBytes = 8 }));
            Assert.AreEqual(OWWorldArchiveReason.TooMany, Why(WorldZip("many.owworld"), new OWWorldArchiveReadOptions { MaxEntries = 1 }));
            Assert.AreEqual(OWWorldArchiveReason.Duplicate, Why(WorldZip("twice.owworld", ("README.txt", "1"), ("README.txt", "2"))));
            Assert.AreEqual(OWWorldArchiveReason.Collision, Why(WorldZip("case.owworld", ("w/media/A.png", "1"), ("w/media/a.png", "2"))));
            Assert.AreEqual(OWWorldArchiveReason.Name, Why(WorldZip("con.owworld", ("w/media/CON.png", "1"))));
            Assert.AreEqual(OWWorldArchiveReason.Marker, Why(WorldZip("marker.owworld", ("mimetype", "application/zip"))));

            var link = WorldZip("link.owworld", ("w/media/l", "x"));
            SetUnixMode(link, "w/media/l", 0xA1FF);
            Assert.AreEqual(OWWorldArchiveReason.Link, Why(link));

            var method = WorldZip("method.owworld");
            SetMethod(method, "w/elements/character/one.json", 14);
            Assert.AreEqual(OWWorldArchiveReason.Method, Why(method));

            // A folder's refusal has no archive cause.
            var folder = Path.Combine(_temp, "empty-folder");
            Directory.CreateDirectory(folder);
            Assert.IsNull(Assert.Throws<OWFolderFormatException>(() => OWFolderReader.Read(folder)).Reason);
        }

        // -- The marker (folder spec §4.7, OW-6) --------------------------------------------

        [Test]
        public void Read_AcceptsTheMarker_AndNeverRequiresIt()
        {
            Assert.AreEqual("application/vnd.onlyworlds.world+zip", OWWorldArchive.MediaType);

            var marked = MakeZip("marked.owworld", ("mimetype", OWWorldArchive.MediaType),
                ("w/world.json", WorldJson(WId, "W")), ("w/elements/character/one.json", ElementBody));
            Assert.AreEqual(1, OWWorldArchive.Read(marked).Elements.Count);

            // Its shape is a writer's rule: a reader takes the right bytes deflated or late (the set's U2 reading).
            var late = WorldZip("late.owworld", ("mimetype", OWWorldArchive.MediaType));
            Assert.AreEqual(1, OWWorldArchive.Read(late).Elements.Count);

            Assert.AreEqual(1, OWWorldArchive.Read(WorldZip("unmarked.owworld")).Elements.Count, "Never required.");
        }

        [TestCase("application/zip")]
        [TestCase("application/vnd.onlyworlds.world+zip\n")]
        [TestCase("application/vnd.onlyworlds.world+zip\r\n")]
        [TestCase("﻿application/vnd.onlyworlds.world+zip")] // a BOM
        [TestCase("APPLICATION/VND.ONLYWORLDS.WORLD+ZIP")]
        [TestCase("application/vnd.onlyworlds.world+zi")]
        [TestCase("")]
        public void Read_RefusesAMarkerThatHoldsOtherBytes(string marker)
        {
            var archive = WorldZip("marker.owworld", ("mimetype", marker));
            StringAssert.Contains("is the marker but does not hold exactly", Refused(archive).Message);
        }

        [Test]
        public void Read_AMimetypeInsideTheWorld_IsNoMarker()
        {
            // The marker sits at the archive's root; w/mimetype is a file in the world's folder, never read.
            var archive = WorldZip("inner.owworld", ("w/mimetype", "text/plain"));
            Assert.AreEqual(1, OWWorldArchive.Read(archive).Elements.Count);
        }

        [Test]
        public void Read_AcceptsFolderEntries_TheirOneTrailingSlashIsNoEmptySegment()
        {
            var archive = MakeZip("folders.owworld",
                ("w/", string.Empty),
                ("w/elements/", string.Empty),
                ("w/world.json", WorldJson("0695db52-0000-7000-8000-0000000000aa", "W")),
                ("w/elements/character/one.json", ElementBody));

            Assert.AreEqual("elements/character/one.json", OWWorldArchive.Read(archive).Elements.Single().Path);
        }

        // -- Entries that land safely on any disk (owworld-as-tests OW-5, OW-17 to OW-22) ----------------

        private const string WId = "0695db52-0000-7000-8000-0000000000aa";

        /// <summary>The world <c>w/</c> with one element, plus <paramref name="more"/>.</summary>
        private string WorldZip(string fileName, params (string name, string text)[] more)
            => MakeZip(fileName, new[] { ("w/world.json", WorldJson(WId, "W")), ("w/elements/character/one.json", ElementBody) }
                .Concat(more).ToArray());

        private static OWFolderFormatException Refused(string archive)
            => Assert.Throws<OWFolderFormatException>(() => OWWorldArchive.Read(archive));

        [Test]
        public void Read_RefusesASecondWorldJsonInsideTheWorld_ButNotAProducersUnderRecords()
        {
            // OW-5: only records/ may hold a world.json inside a world (folder spec §4.5 rules 2 and 6a).
            var nested = WorldZip("nested.owworld", ("w/inner/world.json", WorldJson("0695db52-0000-7000-8000-0000000000bb", "Inner")));
            StringAssert.Contains("second world.json", Refused(nested).Message);

            var atRoot = MakeZip("nested-root.owworld",
                ("world.json", WorldJson(WId, "Root")),
                ("elements/character/one.json", ElementBody),
                ("elements/character/world.json", WorldJson("0695db52-0000-7000-8000-0000000000bb", "Inner")));
            StringAssert.Contains("second world.json", Refused(atRoot).Message);

            var records = WorldZip("records.owworld", ("w/records/lab/world.json", WorldJson("0695db52-0000-7000-8000-0000000000bb", "Lab")));
            Assert.AreEqual("W", OWWorldArchive.Read(records).WorldName);
        }

        [Test]
        public void Read_RefusesTwoEntriesOfOneName_OutsideTheWorldToo()
        {
            // OW-17: a duplicate anywhere, not only in the world's folder.
            var archive = WorldZip("twice-outside.owworld", ("README.txt", "one"), ("README.txt", "two"));
            StringAssert.Contains("second entry", Refused(archive).Message);
        }

        [TestCase("w/elements/character/Ana.json", "w/elements/character/ana.json")]
        [TestCase("w/media/café.png", "w/media/café.png")] // NFC beside NFD
        [TestCase("w/Elements/character/two.json", "w/elements/character/three.json")] // a folder, by case
        [TestCase("README.txt", "readme.TXT")] // outside the world too
        [TestCase("w/media/ς.png", "w/media/σ.png")] // final sigma: lowercasing keeps them apart, folding does not
        [TestCase("w/media/ſ.png", "w/media/s.png")] // long s
        [TestCase("w/media/K.png", "w/media/k.png")] // the Kelvin sign
        [TestCase("w/media/ꭰ.png", "w/media/Ꭰ.png")] // Cherokee, which folds to its capitals
        [TestCase("w/media/\U00010400.png", "w/media/\U00010428.png")] // Deseret, past U+FFFF
        [TestCase("w/media/ẞ.png", "w/media/ß.png")] // capital sharp s
        [TestCase("w/media/Ǆ.png", "w/media/ǆ.png")]
        public void Read_RefusesNamesThatAreOneOnceCaseAndNormalizationAreIgnored(string first, string second)
        {
            // OW-18: one name after NFC then Unicode simple case folding (folder spec §4.7: one fold, every reader).
            var archive = WorldZip("collide.owworld", (first, "{}"), (second, "{}"));
            StringAssert.Contains("once case and Unicode normalization are ignored", Refused(archive).Message);
        }

        [TestCase("w/media/straße.png", "w/media/strasse.png")] // simple folding keeps ß; full folding would join them
        [TestCase("w/media/İ.png", "w/media/i.png")] // dotted capital I: Turkic folding only
        [TestCase("w/media/ı.png", "w/media/I.png")] // dotless i
        [TestCase("w/media/ﬀ.png", "w/media/ff.png")] // a ligature folds to two letters only in full folding
        public void Read_AcceptsNamesThatOnlyFullOrTurkicFoldingWouldJoin(string first, string second)
        {
            var archive = WorldZip("apart.owworld", (first, "{}"), (second, "{}"));
            Assert.AreEqual(1, OWWorldArchive.Read(archive).Elements.Count);
        }

        [Test]
        public void Read_RefusesAPathThatIsBothAFileAndAFolder()
        {
            // OW-19: w/media is a file, and w/media/a.png sits under it.
            var archive = WorldZip("file-folder.owworld", ("w/media", "a file"), ("w/media/a.png", "png"));
            StringAssert.Contains("both a file and a folder", Refused(archive).Message);

            var entry = WorldZip("file-folder-entry.owworld", ("w/media", "a file"), ("w/media/", string.Empty));
            StringAssert.Contains("both a file and a folder", Refused(entry).Message, "A folder's own entry counts too.");
        }

        [TestCase(0xA1FF, "symbolic link")] // 0o120777, a symlink
        [TestCase(0x11A4, "special file")] // 0o010644, a FIFO
        [TestCase(0x21A4, "special file")] // 0o020644, a character device
        public void Read_RefusesALinkOrASpecialFile_ByItsUnixMode(int mode, string said)
        {
            // OW-20: the Unix mode in the external attributes' high 16 bits.
            var archive = WorldZip("link.owworld", ("w/media/passwd", "../../etc/passwd"));
            SetUnixMode(archive, "w/media/passwd", mode);
            StringAssert.Contains(said, Refused(archive).Message);
        }

        [Test]
        public void Read_AcceptsAFileAndAFolderMode_AsAUnixZipWritesThem()
        {
            var archive = WorldZip("modes.owworld", ("w/media/", string.Empty), ("w/media/a.png", "png"));
            SetUnixMode(archive, "w/media/a.png", 0x81A4); // 0o100644
            SetUnixMode(archive, "w/media/", 0x41ED); // 0o040755
            Assert.AreEqual(1, OWWorldArchive.Read(archive).Elements.Count);
        }

        [Test]
        public void Read_RefusesAnEncryptedEntry_ByItsFlag()
        {
            // OW-21: general-purpose bit 0, refused for what it is (not by a size check it happens to trip).
            var archive = WorldZip("encrypted.owworld");
            SetFlags(archive, "w/elements/character/one.json", 0x0001);
            StringAssert.Contains("is encrypted", Refused(archive).Message);
        }

        [TestCase(14)] // LZMA
        [TestCase(9)] // deflate64
        [TestCase(12)] // bzip2
        public void Read_RefusesAnEntryInAMethodOtherThanStoredOrDeflate_NeverSkipsIt(int method)
        {
            // OW-21: before, an element the runtime could not inflate was skipped and the read went on.
            var archive = WorldZip("method.owworld");
            SetMethod(archive, "w/elements/character/one.json", method);
            StringAssert.Contains($"method {method}", Refused(archive).Message);
        }

        [TestCase("w/elements/character/CON.json", "reserved name 'CON'")]
        [TestCase("w/media/nul.txt", "reserved name 'nul'")]
        [TestCase("w/media/com1.json", "reserved name 'com1'")]
        [TestCase("w/media/LPT9", "reserved name 'LPT9'")]
        [TestCase("w/aux/x.json", "reserved name 'aux'")] // a folder
        [TestCase("w/media/a.png.", "ending in a dot")]
        [TestCase("w/media/b.png ", "ending in a space")]
        [TestCase("w/media/a?.png", "character '?'")]
        [TestCase("w/media/a<b.png", "character '<'")]
        [TestCase("w/media/a>b.png", "character '>'")]
        [TestCase("w/media/a:b.png", "character ':'")]
        [TestCase("w/media/a\"b.png", "character '\"'")]
        [TestCase("w/media/a|b.png", "character '|'")]
        [TestCase("w/media/a*b.png", "character '*'")]
        [TestCase("w/media/a\u0001.png", "control character (U+0001)")]
        [TestCase("w/media/a\u007f.png", "control character (U+007F)")]
        [TestCase("NUL", "reserved name 'NUL'")] // outside the world too
        public void Read_RefusesANameThatWouldNotLandOnEveryDisk(string name, string said)
        {
            // OW-22: Windows is the strictest disk.
            var archive = WorldZip("name.owworld", (name, "{}"));
            StringAssert.Contains(said, Refused(archive).Message);
        }

        [Test]
        public void Read_AcceptsANameThatOnlyStartsLikeADevice()
        {
            // con--0000e1's stem is con--0000e1, not CON; COM10 and CONSOLE are no devices.
            var archive = WorldZip("not-devices.owworld",
                ("w/elements/character/con--0000e1.json", ElementBody.Replace("e1\"", "e2\"")),
                ("w/media/COM10.png", "png"),
                ("w/media/console.png", "png"),
                ("w/media/.cursor", "a dot-file"));
            Assert.AreEqual(2, OWWorldArchive.Read(archive).Elements.Count);
        }

        [Test]
        public void Read_RefusesANonAsciiNameWithoutTheUtf8Flag_AndReadsItWithTheFlag()
        {
            // OW-22: without bit 11 a name's bytes are in a code page the zip does not name.
            var flagged = WorldZip("flagged.owworld", ("w/media/café.png", "png"));
            Assert.AreEqual(1, OWWorldArchive.Read(flagged).Elements.Count, "Flagged UTF-8, the name is what it says.");

            SetFlags(flagged, "w/media/café.png", set: 0, clear: 0x0800);
            StringAssert.Contains("UTF-8 flag", Refused(flagged).Message);
        }

        // -- The key carries no machine's path -----------------------------------------

        private const string AbbeyGuid = "4F1A9C0E2B7D4E8FA1C3B5D7E9F0A2B4";

        [Test]
        public void LoadInto_KeysOnTheAssetGuid_NoDriveNoUsersSegment_AndIsReadOnly()
        {
            var archive = PackTo(MakeAbbey(), "abbey.owworld");
            var cache = UnityEngine.ScriptableObject.CreateInstance<OWWorldCache>();

            OWWorldArchive.LoadInto(cache, archive, AbbeyGuid);

            Assert.AreEqual(OWSourceKind.Folder, cache.Key.Source);
            Assert.AreEqual(AbbeyWorldId, cache.Key.WorldId);
            Assert.AreEqual("guid:" + AbbeyGuid.ToLowerInvariant(), cache.Key.Origin);
            Assert.AreEqual(OWWorldArchive.KeyFor(AbbeyWorldId, AbbeyGuid), cache.Key, "LoadInto keys as KeyFor does.");
            foreach (var text in new[] { cache.Key.Origin, cache.Key.ToString(), cache.Key.ToAssetKey() })
            {
                Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(text, @"[A-Za-z]:[\\/_]"), "drive letter in " + text);
                Assert.IsFalse(text.Split('/', '\\', '_').Any(s => s.Equals("Users", StringComparison.OrdinalIgnoreCase)), "Users in " + text);
                Assert.IsFalse(text.Contains(Path.GetTempPath().TrimEnd('\\', '/')), "temp path in " + text);
                Assert.IsFalse(text.Contains(_temp), "the archive's own path in " + text);
            }

            Assert.IsTrue(cache.ReadOnly, "An archive is a capture: read-only though the world does not declare it.");
            Assert.AreEqual(5, cache.Count, "1 in elements/, 4 only in spatial/.");
            StringAssert.Contains("\"map\":", cache.AllRaw("pin").Single(), "Links under the schema's names, as a folder load.");
        }

        [Test]
        public void LoadInto_TheSerializedCacheCarriesNoAbsolutePath()
        {
            var archive = PackTo(MakeAbbey(), "abbey.owworld");
            var cache = UnityEngine.ScriptableObject.CreateInstance<OWWorldCache>();
            OWWorldArchive.LoadInto(cache, archive, AbbeyGuid);

            // Unity's own serializer, the one that writes the imported artifact: needs the Editor.
            var json = UnityEngine.JsonUtility.ToJson(cache);

            StringAssert.Contains("\"_origin\":\"guid:" + AbbeyGuid.ToLowerInvariant() + "\"", json);
            Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(json, @"[A-Za-z]:\\\\"), "a drive path in the asset");
            Assert.IsFalse(json.Contains("Users"), "a Users segment in the asset");
        }

        [TestCase(@"C:\Users\someone\Project\Assets\abbey.owworld")]
        [TestCase("C:/Users/someone/Project/Assets/abbey.owworld")]
        [TestCase("/home/someone/Project/Assets/abbey.owworld")]
        [TestCase("Assets/Worlds/abbey.owworld")]
        [TestCase("")]
        [TestCase(null)]
        [TestCase("4f1a9c0e2b7d4e8fa1c3b5d7e9f0a2b")]
        [TestCase("4f1a9c0e2b7d4e8fa1c3b5d7e9f0a2b4f")]
        [TestCase("4f1a9c0e-2b7d-4e8f-a1c3-b5d7e9f0")]
        [TestCase("zf1a9c0e2b7d4e8fa1c3b5d7e9f0a2b4")]
        [TestCase("00000000000000000000000000000000")]
        public void KeyFor_RefusesAnythingButAnAssetGuid(string notAGuid)
        {
            Assert.Throws<ArgumentException>(() => OWWorldArchive.KeyFor(AbbeyWorldId, notAGuid));
        }

        [Test]
        public void KeyFor_IsPure_CaseInsensitive_AndTellsTwoArchivesOfOneWorldApart()
        {
            var a = OWWorldArchive.KeyFor(AbbeyWorldId, AbbeyGuid);
            var again = OWWorldArchive.KeyFor(AbbeyWorldId, AbbeyGuid.ToLowerInvariant());
            var b = OWWorldArchive.KeyFor(AbbeyWorldId, "0123456789abcdef0123456789abcdef");

            Assert.AreEqual(a, again);
            Assert.AreEqual("guid:4f1a9c0e2b7d4e8fa1c3b5d7e9f0a2b4", a.Origin);
            Assert.AreNotEqual(a, b);
        }

        // -- LoadInto(cache, read) is LoadInto(cache, path) ---------------------------

        [Test]
        public void LoadInto_ResultForm_EqualsPathForm_TheFixture()
        {
            var root = RequireFixture();
            AssertSameCache(root);
        }

        [Test]
        public void LoadInto_ResultForm_EqualsPathForm_AnAbbeyInTheLegacyLayout()
        {
            AssertSameCache(MakeAbbey());
        }

        [Test]
        public void LoadInto_ResultForm_HonoursAFrozenSnapshot_AsThePathFormDoes()
        {
            var root = Path.Combine(_temp, "frozen");
            Write(root, "world.json",
                "{\n  \"id\": \"0695db52-0000-7000-8000-0000000000aa\",\n  \"name\": \"Frozen\",\n  \"writable\": false\n}\n");
            Write(root, "elements/character/one.json", ElementBody);

            var byResult = AssertSameCache(root);
            Assert.IsTrue(byResult.ReadOnly);
        }

        private static OWWorldCache AssertSameCache(string root)
        {
            var byPath = UnityEngine.ScriptableObject.CreateInstance<OWWorldCache>();
            var pathRead = OWFolderLoader.LoadInto(byPath, root);

            var byResult = UnityEngine.ScriptableObject.CreateInstance<OWWorldCache>();
            var read = OWFolderReader.Read(root);
            var returned = OWFolderLoader.LoadInto(byResult, read, OWWorldKey.FromFolder(read.WorldId, root));

            Assert.AreSame(read, returned);
            Assert.That(byPath.Count, Is.GreaterThan(0));
            CollectionAssert.AreEqual(Digest(byPath), Digest(byResult));
            Assert.AreEqual(pathRead.Skipped.Count, read.Skipped.Count);
            return byResult;
        }

        // -- The menu's default name ----------------------------------------------------

        [Test]
        public void DefaultArchiveName_IsTheWorldNameSlug_ElseTheIdTail()
        {
            Assert.AreEqual("watabou-s-abbey.owworld", OWWorldArchive.DefaultArchiveName(MakeAbbey()));

            var root = Path.Combine(_temp, "unnamed");
            Write(root, "world.json", WorldJson("0695db52-0000-7000-8000-0000000000aa", ""));
            Assert.AreEqual("000000aa.owworld", OWWorldArchive.DefaultArchiveName(root));
        }
    }
}
